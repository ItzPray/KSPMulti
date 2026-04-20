using LmpCommon.Enums;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using Server.Client;
using Server.Log;
using Server.System;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Server.System.PersistentSync
{
    public class UpgradeableFacilitiesPersistentSyncDomainStore : IPersistentSyncServerDomain
    {
        private const string ScenarioName = "ScenarioUpgradeableFacilities";
        private const string LevelFieldName = "lvl";
        private const float MaxPersistentLevel = 2f;

        private static readonly string[] KnownFacilityIds =
        {
            "SpaceCenter/LaunchPad",
            "SpaceCenter/Runway",
            "SpaceCenter/VehicleAssemblyBuilding",
            "SpaceCenter/SpaceplaneHangar",
            "SpaceCenter/TrackingStation",
            "SpaceCenter/AstronautComplex",
            "SpaceCenter/MissionControl",
            "SpaceCenter/ResearchAndDevelopment",
            "SpaceCenter/Administration"
        };

        private readonly Dictionary<string, int> _facilityLevels = new Dictionary<string, int>();

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.UpgradeableFacilities;
        public PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        private long Revision { get; set; }

        public void LoadFromPersistence(bool createdFromScratch)
        {
            _facilityLevels.Clear();
            foreach (var facilityId in KnownFacilityIds)
            {
                _facilityLevels[facilityId] = 0;
            }

            lock (ScenarioStoreSystem.ConfigTreeAccessLock)
            {
                if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
                {
                    return;
                }

                foreach (var facilityId in KnownFacilityIds)
                {
                    if (!UpgradeableFacilitiesScenarioNodes.TryGetFacilityNode(scenario, facilityId, out var facilityNode))
                    {
                        continue;
                    }

                    var rawValue = facilityNode.GetValue(LevelFieldName)?.Value;
                    if (string.IsNullOrEmpty(rawValue))
                    {
                        continue;
                    }

                    if (float.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsedLevel))
                    {
                        _facilityLevels[facilityId] = DeserializePersistentLevel(parsedLevel);
                    }
                }

                if (createdFromScratch)
                {
                    PersistCurrentState();
                }
            }

            LogFacilityLevelsSnapshot("LoadFromPersistence");
        }

        private void LogFacilityLevelsSnapshot(string context)
        {
            var summary = string.Join(",", _facilityLevels
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => $"{FacilityShortName(kvp.Key)}={kvp.Value}"));
            LunaLog.Debug($"[PersistentSync] UpgradeableFacilities {context} revision={Revision} levels=[{summary}]");
        }

        private static string FacilityShortName(string facilityId)
        {
            if (string.IsNullOrEmpty(facilityId))
            {
                return facilityId;
            }

            var slash = facilityId.LastIndexOf('/');
            return slash >= 0 && slash < facilityId.Length - 1 ? facilityId.Substring(slash + 1) : facilityId;
        }

        public PersistentSyncDomainSnapshot GetCurrentSnapshot()
        {
            LogFacilityLevelsSnapshot("GetCurrentSnapshot");
            var snapshotMap = new Dictionary<string, int>(_facilityLevels);
            var payload = UpgradeableFacilitiesSnapshotPayloadSerializer.Serialize(snapshotMap);
            return new PersistentSyncDomainSnapshot
            {
                DomainId = DomainId,
                Revision = Revision,
                AuthorityPolicy = AuthorityPolicy,
                Payload = payload,
                NumBytes = payload.Length
            };
        }

        public PersistentSyncDomainApplyResult ApplyClientIntent(ClientStructure client, PersistentSyncIntentMsgData data)
        {
            UpgradeableFacilitiesIntentPayloadSerializer.Deserialize(data.Payload, data.NumBytes, out var facilityId, out var level);
            return ApplyFacilityMutation(facilityId, level, data.ClientKnownRevision);
        }

        public PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, int numBytes, string reason)
        {
            UpgradeableFacilitiesIntentPayloadSerializer.Deserialize(payload, numBytes, out var facilityId, out var level);
            return ApplyFacilityMutation(facilityId, level, null);
        }

        private PersistentSyncDomainApplyResult ApplyFacilityMutation(string facilityId, int level, long? clientKnownRevision)
        {
            if (string.IsNullOrEmpty(facilityId))
            {
                return new PersistentSyncDomainApplyResult { Accepted = false };
            }

            if (!TryResolveCanonicalFacilityId(facilityId, out facilityId))
            {
                return new PersistentSyncDomainApplyResult { Accepted = false };
            }

            if (_facilityLevels.TryGetValue(facilityId, out var existingLevel))
            {
                // KSC init fires OnKSCFacilityUpgraded with FacilityLevel 0 before the client applies the
                // PersistentSync snapshot; those intents must not clobber a copied universe that already
                // has higher persisted levels.
                if (level < existingLevel)
                {
                    PersistCurrentState();
                    return new PersistentSyncDomainApplyResult
                    {
                        Accepted = true,
                        Changed = false,
                        ReplyToOriginClient = clientKnownRevision.HasValue && clientKnownRevision.Value != Revision,
                        Snapshot = GetCurrentSnapshot()
                    };
                }

                if (existingLevel == level)
                {
                    PersistCurrentState();
                    return new PersistentSyncDomainApplyResult
                    {
                        Accepted = true,
                        Changed = false,
                        ReplyToOriginClient = clientKnownRevision.HasValue && clientKnownRevision.Value != Revision,
                        Snapshot = GetCurrentSnapshot()
                    };
                }
            }

            _facilityLevels[facilityId] = level;
            Revision++;
            PersistCurrentState();

            return new PersistentSyncDomainApplyResult
            {
                Accepted = true,
                Changed = true,
                ReplyToOriginClient = false,
                Snapshot = GetCurrentSnapshot()
            };
        }

        private void PersistCurrentState()
        {
            lock (ScenarioStoreSystem.ConfigTreeAccessLock)
            {
                if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
                {
                    return;
                }

                foreach (var facility in _facilityLevels.OrderBy(kvp => kvp.Key))
                {
                    var lvlText = SerializePersistentLevel(facility.Value).ToString(CultureInfo.InvariantCulture);
                    UpgradeableFacilitiesScenarioNodes.EnsureFacilityLevelValue(scenario, facility.Key, lvlText);
                }
            }
        }

        private static int DeserializePersistentLevel(float normalizedLevel)
        {
            return (int)global::System.Math.Round(normalizedLevel * MaxPersistentLevel, global::System.MidpointRounding.AwayFromZero);
        }

        private static float SerializePersistentLevel(int level)
        {
            if (level <= 0)
            {
                return 0f;
            }

            return level / MaxPersistentLevel;
        }

        private static bool TryResolveCanonicalFacilityId(string rawFacilityId, out string canonicalFacilityId)
        {
            canonicalFacilityId = null;
            if (string.IsNullOrEmpty(rawFacilityId))
            {
                return false;
            }

            var normalized = rawFacilityId.Replace('\\', '/');
            foreach (var known in KnownFacilityIds)
            {
                if (string.Equals(known, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    canonicalFacilityId = known;
                    return true;
                }
            }

            if (!normalized.StartsWith("SpaceCenter/", StringComparison.OrdinalIgnoreCase))
            {
                var withPrefix = "SpaceCenter/" + normalized;
                foreach (var known in KnownFacilityIds)
                {
                    if (string.Equals(known, withPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        canonicalFacilityId = known;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
