using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Server.Client;
using Server.Log;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Server.System.PersistentSync
{
    public sealed class UpgradeableFacilitiesPersistentSyncDomainStore : ScenarioSyncDomainStore<UpgradeableFacilitiesPersistentSyncDomainStore.Canonical, UpgradeableFacilityLevelPayload, UpgradeableFacilityLevelPayload[]>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncServerDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesServerDomain<UpgradeableFacilitiesPersistentSyncDomainStore>();
        }

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

        public override string DomainId => PersistentSyncDomainNames.UpgradeableFacilities;
        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;
        protected override string ScenarioName => "ScenarioUpgradeableFacilities";

        protected override Canonical CreateEmpty()
        {
            var map = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var facilityId in KnownFacilityIds)
            {
                map[facilityId] = 0;
            }
            return new Canonical(map);
        }

        protected override Canonical LoadCanonical(ConfigNode scenario, bool createdFromScratch)
        {
            var map = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var facilityId in KnownFacilityIds)
            {
                map[facilityId] = 0;
            }

            if (scenario != null)
            {
                foreach (var facilityId in KnownFacilityIds)
                {
                    if (!UpgradeableFacilitiesScenarioNodes.TryGetFacilityNode(scenario, facilityId, out var facilityNode))
                    {
                        continue;
                    }

                    var rawValue = facilityNode.GetValue(LevelFieldName)?.Value;
                    if (string.IsNullOrEmpty(rawValue)) continue;

                    if (float.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsedLevel))
                    {
                        map[facilityId] = DeserializePersistentLevel(parsedLevel);
                    }
                }
            }

            var canonical = new Canonical(map);
            LogFacilityLevelsSnapshot("LoadFromPersistence", canonical, 0L);
            return canonical;
        }

        protected override ReduceResult<Canonical> ReduceIntent(ClientStructure client, Canonical current, UpgradeableFacilityLevelPayload intent, string reason, bool isServerMutation)
        {
            var facilityId = intent?.FacilityId;
            var level = intent?.Level ?? 0;

            if (string.IsNullOrEmpty(facilityId))
            {
                return ReduceResult<Canonical>.Reject();
            }

            if (!TryResolveCanonicalFacilityId(facilityId, out var canonicalFacilityId))
            {
                return ReduceResult<Canonical>.Reject();
            }

            if (current.Levels.TryGetValue(canonicalFacilityId, out var existingLevel))
            {
                // KSC init fires OnKSCFacilityUpgraded with FacilityLevel 0 before the client applies the
                // PersistentSync snapshot; those intents must not clobber a copied universe that already
                // has higher persisted levels. Accept but leave state untouched so the equality short-circuit
                // collapses them into a no-op.
                if (level < existingLevel)
                {
                    return ReduceResult<Canonical>.Accept(current);
                }

                if (existingLevel == level)
                {
                    return ReduceResult<Canonical>.Accept(current);
                }
            }

            var next = new SortedDictionary<string, int>(current.Levels, StringComparer.Ordinal)
            {
                [canonicalFacilityId] = level
            };
            return ReduceResult<Canonical>.Accept(new Canonical(next));
        }

        protected override ConfigNode WriteCanonical(ConfigNode scenario, Canonical canonical)
        {
            foreach (var facility in canonical.Levels)
            {
                var lvlText = SerializePersistentLevel(facility.Value).ToString(CultureInfo.InvariantCulture);
                UpgradeableFacilitiesScenarioNodes.EnsureFacilityLevelValue(scenario, facility.Key, lvlText);
            }
            return scenario;
        }

        protected override UpgradeableFacilityLevelPayload[] BuildSnapshotPayload(Canonical canonical)
        {
            LogFacilityLevelsSnapshot("GetCurrentSnapshot", canonical, RevisionForTests);
            return canonical.Levels
                .Select(kvp => new UpgradeableFacilityLevelPayload { FacilityId = kvp.Key, Level = kvp.Value })
                .ToArray();
        }

        protected override bool AreEquivalent(Canonical a, Canonical b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Levels.Count != b.Levels.Count) return false;

            foreach (var kvp in a.Levels)
            {
                if (!b.Levels.TryGetValue(kvp.Key, out var other) || other != kvp.Value)
                {
                    return false;
                }
            }
            return true;
        }

        private static void LogFacilityLevelsSnapshot(string context, Canonical canonical, long revision)
        {
            var summary = string.Join(",", canonical.Levels
                .Select(kvp => $"{FacilityShortName(kvp.Key)}={kvp.Value}"));
            LunaLog.Debug($"[PersistentSync] UpgradeableFacilities {context} revision={revision} levels=[{summary}]");
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

        /// <summary>Typed canonical state: facility levels keyed by canonical facility id (ordinal, sorted).</summary>
        public sealed class Canonical
        {
            public Canonical(SortedDictionary<string, int> levels) => Levels = levels ?? new SortedDictionary<string, int>(StringComparer.Ordinal);

            public SortedDictionary<string, int> Levels { get; }
        }
    }
}
