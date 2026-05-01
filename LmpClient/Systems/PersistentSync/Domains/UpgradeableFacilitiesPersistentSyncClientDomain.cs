using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
using LmpClient;
using LmpClient.Systems.KscScene;
using LmpClient.Systems.ShareUpgradeableFacilities;
using LmpCommon.PersistentSync;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Upgradeables;

namespace LmpClient.Systems.PersistentSync
{
    public class UpgradeableFacilitiesPersistentSyncClientDomain : SyncClientDomain<UpgradeableFacilitiesPayload>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesClientDomain<UpgradeableFacilitiesPersistentSyncClientDomain>();
        }

        private Dictionary<string, int> _pendingFacilityLevels;

        /// <summary>
        /// Last deserialized server levels. KSC can reload scenario modules after our first
        /// FlushPendingState; re-applying from this cache on GUI-ready restores upgrades.
        /// </summary>
        private Dictionary<string, int> _authoritativeLevelsFromServer;

        /// <summary>
        /// KSP often fires OnKSCFacilityUpgraded on frames after SetLevel,
        /// so stopping ignore immediately produces a burst of redundant PersistentSync intents (server no-ops).
        /// </summary>
        private Coroutine _delayedStopIgnoringFacilityEventsCoroutine;

        protected override void OnPayloadBuffered(PersistentSyncBufferedSnapshot snapshot, UpgradeableFacilitiesPayload payload)
        {
            _pendingFacilityLevels = (payload?.Items ?? Array.Empty<UpgradeableFacilityLevelPayload>())
                .ToDictionary(level => level.FacilityId, level => level.Level, StringComparer.Ordinal);
            _authoritativeLevelsFromServer = CloneLevelMap(_pendingFacilityLevels);

            LunaLog.Log($"[PersistentSync] UpgradeableFacilities ApplySnapshot revision={snapshot.Revision} received levels=[{FormatLevelMap(_pendingFacilityLevels)}]");
        }

        /// <summary>
        /// Stages the last server facility map for FlushPendingState via the reconciler
        /// so FlushPendingState can run MarkApplied bookkeeping.
        /// </summary>
        public bool TryStageReassertFromLastServerSnapshot()
        {
            if (_authoritativeLevelsFromServer == null || _authoritativeLevelsFromServer.Count == 0)
            {
                return false;
            }

            _pendingFacilityLevels = CloneLevelMap(_authoritativeLevelsFromServer);
            return true;
        }

        public override PersistentSyncApplyOutcome FlushPendingState()
        {
            if (_pendingFacilityLevels == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            var facilitiesById = IndexFacilitiesById();
            var resolved = new Dictionary<string, UpgradeableFacility>(StringComparer.Ordinal);
            foreach (var facilityId in _pendingFacilityLevels.Keys)
            {
                if (!TryResolveFacility(facilitiesById, facilityId, out var facility))
                {
                    LunaLog.Log(
                        $"[PersistentSync] UpgradeableFacilities FlushPendingState deferred facility={facilityId} sceneFacilitiesIndexedCount={facilitiesById.Count} pending=[{FormatLevelMap(_pendingFacilityLevels)}]");
                    return PersistentSyncApplyOutcome.Deferred;
                }

                resolved[facilityId] = facility;
            }

            var postSetLevels = new Dictionary<string, int>(StringComparer.Ordinal);
            ShareUpgradeableFacilitiesSystem.Singleton.StartIgnoringEvents();
            try
            {
                foreach (var facilityLevel in _pendingFacilityLevels.OrderBy(kvp => kvp.Key))
                {
                    var facility = resolved[facilityLevel.Key];
                    facility.SetLevel(facilityLevel.Value);
                    postSetLevels[facilityLevel.Key] = facility.FacilityLevel;
                }
            }
            catch (Exception ex)
            {
                LunaLog.LogError($"[PersistentSync] UpgradeableFacilities FlushPendingState exception: {ex}");
                return PersistentSyncApplyOutcome.Rejected;
            }
            finally
            {
                ScheduleDelayedStopIgnoringFacilityEvents();
            }

            LunaLog.Log(
                $"[PersistentSync] UpgradeableFacilities FlushPendingState applied count={_pendingFacilityLevels.Count} requested=[{FormatLevelMap(_pendingFacilityLevels)}] observed=[{FormatLevelMap(postSetLevels)}]");
            RefreshFacilityAdapters();
            _pendingFacilityLevels = null;
            return PersistentSyncApplyOutcome.Applied;
        }

        /// <summary>
        /// After PersistentSync applies facility levels, PersistentSyncGamePersistenceMaterializer
        /// calls MaterializeUpgradeableProtosFromLiveScene so stock reload paths read current
        /// <c>lvl</c> values. This keeps protoUpgradeables in sync;
        /// without it, scene transitions fire UpgradeableFacility.OnLevelLoaded, then RegisterUpgradeable,
        /// then Load(configNode) where configNode still carries default <c>lvl = 0</c>.
        /// </summary>
        public static void MaterializeUpgradeableProtosFromLiveScene(string reason)
        {
            var byId = IndexFacilitiesById();
            var seen = new HashSet<int>();
            foreach (var facility in byId.Values)
            {
                if (facility == null || !seen.Add(facility.GetInstanceID()))
                {
                    continue;
                }

                SyncScenarioProtoUpgradeable(facility);
            }

            LunaLog.Log($"[PersistentSync] UpgradeableFacilities proto materialize ok reason={reason} uniqueFacilities={seen.Count}");
        }

        private static void SyncScenarioProtoUpgradeable(UpgradeableFacility facility)
        {
            if (facility == null || string.IsNullOrEmpty(facility.id))
            {
                return;
            }

            var protoMap = ScenarioUpgradeableFacilities.protoUpgradeables;
            if (protoMap == null)
            {
                return;
            }

            var sanitizedId = ScenarioUpgradeableFacilities.SlashSanitize(facility.id);
            var normLevel = facility.GetNormLevel();
            var lvlText = normLevel.ToString("R", CultureInfo.InvariantCulture);

            if (!protoMap.TryGetValue(sanitizedId, out var proto) || proto == null)
            {
                var configNode = new ConfigNode(sanitizedId);
                configNode.AddValue("lvl", lvlText);
                protoMap[sanitizedId] = new ScenarioUpgradeableFacilities.ProtoUpgradeable(configNode);
                return;
            }

            var existing = proto.configNode;
            if (existing == null)
            {
                existing = new ConfigNode(sanitizedId);
                proto.configNode = existing;
            }

            if (existing.HasValue("lvl"))
            {
                existing.SetValue("lvl", lvlText);
            }
            else
            {
                existing.AddValue("lvl", lvlText);
            }
        }

        private static string FormatLevelMap(Dictionary<string, int> map)
        {
            if (map == null || map.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(",", map.OrderBy(kvp => kvp.Key).Select(kvp =>
            {
                var slash = kvp.Key.LastIndexOf('/');
                var shortName = slash >= 0 && slash < kvp.Key.Length - 1 ? kvp.Key.Substring(slash + 1) : kvp.Key;
                return $"{shortName}={kvp.Value}";
            }));
        }

        private void ScheduleDelayedStopIgnoringFacilityEvents()
        {
            var host = MainSystem.Singleton;
            if (host == null)
            {
                ShareUpgradeableFacilitiesSystem.Singleton.StopIgnoringEvents();
                return;
            }

            if (_delayedStopIgnoringFacilityEventsCoroutine != null)
            {
                host.StopCoroutine(_delayedStopIgnoringFacilityEventsCoroutine);
                _delayedStopIgnoringFacilityEventsCoroutine = null;
            }

            _delayedStopIgnoringFacilityEventsCoroutine = host.StartCoroutine(DelayedStopIgnoringFacilityEventsRoutine());
        }

        private IEnumerator DelayedStopIgnoringFacilityEventsRoutine()
        {
            yield return null;
            yield return null;
            ShareUpgradeableFacilitiesSystem.Singleton.StopIgnoringEvents();
            _delayedStopIgnoringFacilityEventsCoroutine = null;
        }

        private static Dictionary<string, int> CloneLevelMap(Dictionary<string, int> source)
        {
            return new Dictionary<string, int>(source, StringComparer.Ordinal);
        }

        private static Dictionary<string, UpgradeableFacility> IndexFacilitiesById()
        {
            var dict = new Dictionary<string, UpgradeableFacility>(StringComparer.OrdinalIgnoreCase);
            var seenInstanceIds = new HashSet<int>();

            void Register(UpgradeableFacility f)
            {
                if (f == null || string.IsNullOrEmpty(f.id))
                {
                    return;
                }

                if (!seenInstanceIds.Add(f.GetInstanceID()))
                {
                    return;
                }

                AddFacilityIdAliases(dict, f.id, f);
            }

            foreach (var f in Resources.FindObjectsOfTypeAll(typeof(UpgradeableFacility)).OfType<UpgradeableFacility>())
            {
                if (f == null)
                {
                    continue;
                }

                var go = f.gameObject;
                if (!go.scene.IsValid() || !go.scene.isLoaded)
                {
                    continue;
                }

                Register(f);
            }

            foreach (var f in UnityEngine.Object.FindObjectsOfType<UpgradeableFacility>())
            {
                Register(f);
            }

            return dict;
        }

        private static void AddFacilityIdAliases(Dictionary<string, UpgradeableFacility> dict, string rawId, UpgradeableFacility facility)
        {
            void TryAdd(string key)
            {
                if (string.IsNullOrEmpty(key))
                {
                    return;
                }

                if (!dict.ContainsKey(key))
                {
                    dict[key] = facility;
                }
            }

            TryAdd(rawId);
            var norm = rawId.Replace('\\', '/');
            TryAdd(norm);

            const string prefix = "SpaceCenter/";
            if (norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                TryAdd(norm.Substring(prefix.Length));
            }
            else
            {
                TryAdd(prefix + norm);
            }
        }

        /// <summary>
        /// Snapshot keys follow server scenario paths (often forward slashes). Scene ids can differ
        /// by slash direction, casing, or SpaceCenter/ prefix; resolve leniently before deferring.
        /// </summary>
        private static bool TryResolveFacility(
            Dictionary<string, UpgradeableFacility> byId,
            string facilityIdFromSnapshot,
            out UpgradeableFacility facility)
        {
            facility = null;
            if (string.IsNullOrEmpty(facilityIdFromSnapshot))
            {
                return false;
            }

            foreach (var candidate in ExpandSnapshotFacilityIdCandidates(facilityIdFromSnapshot))
            {
                if (byId.TryGetValue(candidate, out facility))
                {
                    return true;
                }
            }

            var normalized = facilityIdFromSnapshot.Replace('\\', '/');
            foreach (var kvp in byId)
            {
                var keyNorm = kvp.Key.Replace('\\', '/');
                if (string.Equals(keyNorm, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    facility = kvp.Value;
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> ExpandSnapshotFacilityIdCandidates(string facilityIdFromSnapshot)
        {
            yield return facilityIdFromSnapshot;
            var norm = facilityIdFromSnapshot.Replace('\\', '/');
            if (!string.Equals(norm, facilityIdFromSnapshot, StringComparison.Ordinal))
            {
                yield return norm;
            }

            const string prefix = "SpaceCenter/";
            if (norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                yield return norm.Substring(prefix.Length);
            }
            else
            {
                yield return prefix + norm;
            }
        }

        private static void RefreshFacilityAdapters()
        {
            KscSceneSystem.Singleton.RefreshTrackingStationVessels();
            // Do not fire GameEvents.Contract.onContractsListChanged: stock interprets it as a contract reload and
            // spawns fresh default offers (duplicate tutorial contracts) while PersistentSync already owns contract truth.
        }
    }
}
