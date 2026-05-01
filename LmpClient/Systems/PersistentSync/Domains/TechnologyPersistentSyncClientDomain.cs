using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
using System;
using System.Collections.Generic;
using System.Linq;
using KSP.UI.Screens;
using LmpClient.Extensions;
using LmpClient.Systems.ShareAchievements;
using LmpClient.Systems.ShareExperimentalParts;
using LmpClient.Systems.SharePurchaseParts;
using LmpClient.Systems.ShareScience;
using LmpClient.Systems.ShareContracts;
using LmpClient.Systems.ShareScienceSubject;
using LmpClient.Systems.ShareStrategy;
using LmpClient.Systems.ShareTechnology;
using LmpCommon.PersistentSync;
using Strategies;

namespace LmpClient.Systems.PersistentSync
{
    public class TechnologyPersistentSyncClientDomain : SyncClientDomain<TechnologyPayload>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesClientDomain<TechnologyPersistentSyncClientDomain>();
        }

        private Dictionary<string, TechnologySnapshotInfo> _pendingTechnologyById;

        /// <summary>
        /// Last deserialized server tech states. KSP can reinitialize R&D.Instance (or re-hydrate it from a
        /// stale ProtoScenarioModule configNode) after our first FlushPendingState, silently
        /// reverting techs back to Unavailable. Re-staging from this cache on scene-ready and on
        /// onGUIRnDComplexSpawn reasserts the server-authoritative state.
        /// </summary>
        private Dictionary<string, TechnologySnapshotInfo> _authoritativeTechnologyById;

        protected override void OnPayloadBuffered(PersistentSyncBufferedSnapshot snapshot, TechnologyPayload payload)
        {
            _pendingTechnologyById = (payload?.Technologies ?? new TechnologySnapshotInfo[0])
                .Where(technology => technology != null && !string.IsNullOrEmpty(technology.TechId))
                .ToDictionary(technology => technology.TechId, technology => technology, StringComparer.Ordinal);
            _authoritativeTechnologyById = new Dictionary<string, TechnologySnapshotInfo>(_pendingTechnologyById, StringComparer.Ordinal);

            LunaLog.Log($"[PersistentSync] Technology ApplySnapshot revision={snapshot.Revision} receivedTechCount={_pendingTechnologyById.Count} techIds=[{string.Join(",", _pendingTechnologyById.Keys.OrderBy(k => k))}]");
        }

        /// <summary>
        /// Stages the last server tech map for FlushPendingState via the reconciler
        /// so FlushPendingState can reassert state when
        /// KSP may have rebuilt R&D.Instance behind us (e.g. on R&D panel spawn).
        /// </summary>
        public bool TryStageReassertFromLastServerSnapshot()
        {
            if (_authoritativeTechnologyById == null || _authoritativeTechnologyById.Count == 0)
            {
                return false;
            }

            _pendingTechnologyById = new Dictionary<string, TechnologySnapshotInfo>(_authoritativeTechnologyById, StringComparer.Ordinal);
            return true;
        }

        public override PersistentSyncApplyOutcome FlushPendingState()
        {
            if (_pendingTechnologyById == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (ResearchAndDevelopment.Instance == null || AssetBase.RnDTechTree == null)
            {
                LunaLog.Log($"[PersistentSync] Technology FlushPendingState deferred: R&D.Instance={(ResearchAndDevelopment.Instance != null)} RnDTechTree={(AssetBase.RnDTechTree != null)}");
                return PersistentSyncApplyOutcome.Deferred;
            }

            ShareTechnologySystem.Singleton.StartIgnoringEvents();
            int applied = 0;
            int total = 0;
            int missedInSnapshot = 0;
            try
            {
                ApplyTechnologySnapshot(_pendingTechnologyById, out applied, out total, out missedInSnapshot);
            }
            catch (Exception ex)
            {
                LunaLog.LogError($"[PersistentSync] Technology FlushPendingState exception: {ex}");
                return PersistentSyncApplyOutcome.Rejected;
            }
            finally
            {
                ShareTechnologySystem.Singleton.StopIgnoringEvents();
            }

            var postApplyAvailable = 0;
            var postApplyUnavailable = 0;
            foreach (var tech in AssetBase.RnDTechTree.GetTreeTechs().Where(node => node != null))
            {
                var state = ResearchAndDevelopment.Instance.GetTechState(tech.techID);
                if (state == null) continue;
                if (state.state == RDTech.State.Available) postApplyAvailable++;
                else postApplyUnavailable++;
            }

            LunaLog.Log($"[PersistentSync] Technology FlushPendingState treeTechs={total} snapshotHits={applied} snapshotMisses={missedInSnapshot} pendingTechCount={_pendingTechnologyById.Count} postApplyAvailable={postApplyAvailable} postApplyUnavailable={postApplyUnavailable}");

            // R&D side panel reads partsPurchased/state from RnDTech tree nodes; SetTechState updates the
            // singleton only. Stock UnlockProtoTechNode keeps them aligned; mirror after snapshot apply.
            SyncRnDTechTreeFromResearchInstance();
            EnsureImplicitPurchasedPartsForAvailableTechsIfNeeded("TechnologyFlush");

            if (RDController.Instance != null)
            {
                ShareTechnologySystem.Singleton.SchedulePersistentSyncRnDUiCoalescedRefresh(true);
            }
            else
            {
                ShareTechnologySystem.Singleton.RefreshEditorAfterTechSnapshot("PersistentSyncSnapshotApply");
            }

            _pendingTechnologyById = null;
            return PersistentSyncApplyOutcome.Applied;
        }

        /// <summary>
        /// Legacy LMP contract (Career included): researching a node makes every part in that tech usable without a
        /// separate Research and Development purchase step. Stock can leave partsPurchased empty while the
        /// node is Available, which makes the Research and Development UI ask for purchases anyway.
        /// Also, when <c>partsPurchased</c> is non-empty but stale (e.g. server snapshot or part-purchase list
        /// captured before a mod added new parts to an already-researched node), we must merge in every current
        /// LoadedPartsList entry whose <c>TechRequired</c> matches so new mod parts are owned
        /// without asking the player to buy them again.
        /// </summary>
        public static void EnsureImplicitPurchasedPartsForAvailableTechsIfNeeded(string reason)
        {
            if (ResearchAndDevelopment.Instance == null || AssetBase.RnDTechTree == null)
            {
                return;
            }

            var partsByTechId = new Dictionary<string, List<AvailablePart>>(StringComparer.Ordinal);
            foreach (var ap in PartLoader.LoadedPartsList)
            {
                if (ap == null || string.IsNullOrEmpty(ap.TechRequired))
                {
                    continue;
                }

                if (!partsByTechId.TryGetValue(ap.TechRequired, out var list))
                {
                    list = new List<AvailablePart>();
                    partsByTechId[ap.TechRequired] = list;
                }

                list.Add(ap);
            }

            var filled = 0;
            foreach (var tech in AssetBase.RnDTechTree.GetTreeTechs().Where(node => node != null))
            {
                var state = ResearchAndDevelopment.Instance.GetTechState(tech.techID);
                if (state == null || state.state != RDTech.State.Available)
                {
                    continue;
                }

                if (!partsByTechId.TryGetValue(tech.techID, out var implied) || implied.Count == 0)
                {
                    continue;
                }

                var seenNames = new HashSet<string>(StringComparer.Ordinal);
                var merged = new List<AvailablePart>();

                if (state.partsPurchased != null)
                {
                    foreach (var p in state.partsPurchased)
                    {
                        if (p == null || string.IsNullOrEmpty(p.name) || !seenNames.Add(p.name))
                        {
                            continue;
                        }

                        merged.Add(p);
                    }
                }

                foreach (var p in implied)
                {
                    if (p == null || string.IsNullOrEmpty(p.name) || !seenNames.Add(p.name))
                    {
                        continue;
                    }

                    merged.Add(p);
                }

                var priorCount = state.partsPurchased?.Count ?? 0;
                if (priorCount == merged.Count && merged.Count > 0)
                {
                    var priorNameSet = new HashSet<string>(
                        state.partsPurchased.Where(p => p != null && !string.IsNullOrEmpty(p.name)).Select(p => p.name),
                        StringComparer.Ordinal);
                    if (priorNameSet.SetEquals(merged.Select(p => p.name)))
                    {
                        continue;
                    }
                }

                state.partsPurchased = merged;
                ResearchAndDevelopment.Instance.SetTechState(tech.techID, state);
                filled++;
            }

            if (filled > 0)
            {
                LunaLog.Log($"[PersistentSync] Implicit partsPurchased merge (full tech ownership + mod-added parts) filledTechs={filled} source={reason}");
                SyncRnDTechTreeFromResearchInstance();
            }
        }

        /// <summary>
        /// Copies state, cost, and partsPurchased from Instance into each
        /// RDTech tree node so the Research and Development UI matches gameplay (VAB/editor use the singleton).
        /// </summary>
        public static void SyncRnDTechTreeFromResearchInstance()
        {
            if (ResearchAndDevelopment.Instance == null || AssetBase.RnDTechTree == null)
            {
                return;
            }

            foreach (var tech in AssetBase.RnDTechTree.GetTreeTechs().Where(node => node != null))
            {
                var saved = ResearchAndDevelopment.Instance.GetTechState(tech.techID);
                if (saved == null)
                {
                    continue;
                }

                tech.state = saved.state;
                tech.scienceCost = saved.scienceCost;
                if (tech.partsPurchased == null)
                {
                    continue;
                }

                tech.partsPurchased.Clear();
                if (saved.partsPurchased != null)
                {
                    foreach (var part in saved.partsPurchased)
                    {
                        if (part != null)
                        {
                            tech.partsPurchased.Add(part);
                        }
                    }
                }
            }
        }

        private static void ApplyTechnologySnapshot(IReadOnlyDictionary<string, TechnologySnapshotInfo> technologyById, out int applied, out int total, out int missedInSnapshot)
        {
            applied = 0;
            total = 0;
            missedInSnapshot = 0;
            var snapshotMatched = new HashSet<string>(StringComparer.Ordinal);

            foreach (var tech in AssetBase.RnDTechTree.GetTreeTechs().Where(node => node != null))
            {
                total++;
                var saved = ResearchAndDevelopment.Instance.GetTechState(tech.techID);
                var baseParts = saved?.partsPurchased != null && saved.partsPurchased.Count > 0
                    ? saved.partsPurchased
                    : (tech.partsPurchased ?? new List<AvailablePart>());
                var protoTechNode = new ProtoTechNode
                {
                    techID = tech.techID,
                    scienceCost = tech.scienceCost,
                    state = tech.state,
                    partsPurchased = new List<AvailablePart>(baseParts.Where(part => part != null))
                };

                if (technologyById.TryGetValue(tech.techID, out var snapshot))
                {
                    ApplySnapshotInfo(protoTechNode, snapshot);
                    snapshotMatched.Add(tech.techID);
                    applied++;
                }

                ResearchAndDevelopment.Instance.SetTechState(tech.techID, protoTechNode);
            }

            foreach (var snapshotId in technologyById.Keys)
            {
                if (!snapshotMatched.Contains(snapshotId))
                {
                    missedInSnapshot++;
                    LunaLog.LogWarning($"[PersistentSync] Technology snapshot tech '{snapshotId}' not found in RnDTechTree.GetTreeTechs(); skipping");
                }
            }
        }

        private static void ApplySnapshotInfo(ProtoTechNode protoTechNode, TechnologySnapshotInfo snapshot)
        {
            // CRITICAL: the server serializes Technology Data as bare "key = value" lines with NO
            // surrounding braces (see TechnologyPersistentSyncDomainStore.BuildBareNodeText on the
            // server). KSP's brace-aware ConfigNode parser (PreFormatConfig/RecurseFormat, used by
            // ConfigNodeSerializer.DeserializeToConfigNode) returns an empty node for that format,
            // which made GetValue("state") always return null and every tech stay Unavailable.
            // Parse the bare key=value lines directly to match the server wire format.
            if (snapshot?.Data == null || snapshot.Data.Length <= 0)
            {
                LunaLog.LogWarning($"[PersistentSync] Technology snapshot for '{snapshot?.TechId}' has no data; retaining default tech state");
                return;
            }

            var text = System.Text.Encoding.UTF8.GetString(snapshot.Data, 0, snapshot.Data.Length);
            string stateValue = null;
            string costValue = null;
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();
                if (string.Equals(key, "state", StringComparison.Ordinal)) stateValue = value;
                else if (string.Equals(key, "cost", StringComparison.Ordinal)) costValue = value;
            }

            if (!string.IsNullOrEmpty(stateValue))
            {
                try
                {
                    protoTechNode.state = (RDTech.State)Enum.Parse(typeof(RDTech.State), stateValue, ignoreCase: true);
                }
                catch (Exception ex)
                {
                    LunaLog.LogWarning($"[PersistentSync] Technology snapshot for '{snapshot.TechId}' had unparseable state='{stateValue}': {ex.Message}");
                }
            }
            else
            {
                LunaLog.LogWarning($"[PersistentSync] Technology snapshot for '{snapshot.TechId}' missing 'state' value; retaining default tech state. payload='{text.Replace('\n', '|')}'");
            }

            if (int.TryParse(costValue, out var cost))
            {
                protoTechNode.scienceCost = cost;
            }
        }
    }
}
