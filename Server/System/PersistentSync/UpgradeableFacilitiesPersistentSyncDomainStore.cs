using LmpCommon.Enums;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using Server.System;
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

            if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
            {
                return;
            }

            foreach (var facilityId in KnownFacilityIds)
            {
                var facilityNode = scenario.GetNode(facilityId)?.Value;
                var rawValue = facilityNode?.GetValue(LevelFieldName)?.Value;
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

        public PersistentSyncDomainSnapshot GetCurrentSnapshot()
        {
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

        public PersistentSyncDomainApplyResult ApplyClientIntent(PersistentSyncIntentMsgData data)
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

            if (_facilityLevels.TryGetValue(facilityId, out var existingLevel) && existingLevel == level)
            {
                return new PersistentSyncDomainApplyResult
                {
                    Accepted = true,
                    Changed = false,
                    ReplyToOriginClient = clientKnownRevision.HasValue && clientKnownRevision.Value != Revision,
                    Snapshot = GetCurrentSnapshot()
                };
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
            if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
            {
                return;
            }

            foreach (var facility in _facilityLevels.OrderBy(kvp => kvp.Key))
            {
                var facilityNode = scenario.GetNode(facility.Key)?.Value;
                if (facilityNode == null)
                {
                    facilityNode = new LunaConfigNode.CfgNode.ConfigNode(facility.Key, scenario);
                    scenario.AddNode(facilityNode);
                }

                facilityNode.UpdateValue(LevelFieldName, SerializePersistentLevel(facility.Value).ToString(CultureInfo.InvariantCulture));
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
    }
}
