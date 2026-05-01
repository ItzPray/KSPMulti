using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Server.Client;
using Server.System;
using System.Globalization;

namespace Server.System.PersistentSync
{
    /// <summary>
    /// Owns the LMP scenario <c>LmpGameLaunchId</c>: authoritative high-water <c>Game.launchID</c> for the universe.
    /// Server canonical advances with Max on each intent or server mutation so clients
    /// reporting vessel-era part stamps cannot lower the counter. LoadCanonical also reconciles
    /// against VesselStoreSystem so legacy saves without this file still pick up existing vessels.
    /// </summary>
    public sealed class GameLaunchIdPersistentSyncDomainStore : ScenarioSyncDomainStore<ScalarCanonical<uint>, PersistentSyncValueWithReason<uint>, uint>
    {
        public static readonly PersistentSyncDomainKey Domain = PersistentSyncDomain.Define("GameLaunchId", 11);

        public static void RegisterPersistentSyncDomain(PersistentSyncServerDomainRegistrar registrar)
        {
            registrar.Register(Domain)
                .OwnsStockScenario("LmpGameLaunchId")
                .UsesServerDomain<GameLaunchIdPersistentSyncDomainStore>();
        }

        public override PersistentSyncDomainId DomainId => Domain.LegacyId;

        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        protected override string ScenarioName => GameLaunchIdScenarioBootstrap.ScenarioKey;

        protected override ScalarCanonical<uint> CreateEmpty()
        {
            return new ScalarCanonical<uint>(1);
        }

        protected override ScalarCanonical<uint> LoadCanonical(ConfigNode scenario, bool createdFromScratch)
        {
            var fromFile = ParseLaunchIdFromScenario(scenario);
            var fromVessels = ScanMaxLaunchIdAcrossLoadedVessels();
            var merged = global::System.Math.Max(fromFile, fromVessels);
            if (merged < 1)
            {
                merged = 1;
            }

            return new ScalarCanonical<uint>(merged);
        }

        protected override ReduceResult<ScalarCanonical<uint>> ReduceIntent(
            ClientStructure client,
            ScalarCanonical<uint> current,
            PersistentSyncValueWithReason<uint> intent,
            string reason,
            bool isServerMutation)
        {
            try
            {
                var incoming = intent?.Value ?? 1u;
                var next = global::System.Math.Max(current?.Value ?? 1u, incoming);
                if (next < 1)
                {
                    next = 1;
                }

                return ReduceResult<ScalarCanonical<uint>>.Accept(new ScalarCanonical<uint>(next));
            }
            catch
            {
                return ReduceResult<ScalarCanonical<uint>>.Reject();
            }
        }

        protected override ConfigNode WriteCanonical(ConfigNode scenario, ScalarCanonical<uint> canonical)
        {
            if (scenario == null || canonical == null)
            {
                return scenario;
            }

            scenario.UpdateValue("launchID", canonical.Value.ToString(CultureInfo.InvariantCulture));
            return scenario;
        }

        protected override uint BuildSnapshotPayload(ScalarCanonical<uint> canonical)
        {
            return (canonical ?? new ScalarCanonical<uint>(1u)).Value;
        }

        protected override bool AreEquivalent(ScalarCanonical<uint> a, ScalarCanonical<uint> b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null)
            {
                return false;
            }

            return a.Value == b.Value;
        }

        protected override bool ShouldWriteBackAfterLoad(ScalarCanonical<uint> loaded, ConfigNode scenario)
        {
            if (scenario == null)
            {
                return false;
            }

            var fileVal = ParseLaunchIdFromScenario(scenario);
            return loaded.Value != fileVal;
        }

        private static uint ParseLaunchIdFromScenario(ConfigNode scenario)
        {
            if (scenario == null)
            {
                return 1u;
            }

            var raw = scenario.GetValue("launchID")?.Value;
            if (!string.IsNullOrEmpty(raw) &&
                uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed < 1 ? 1u : parsed;
            }

            return 1u;
        }

        private static uint ScanMaxLaunchIdAcrossLoadedVessels()
        {
            uint max = 0;
            foreach (var kv in VesselStoreSystem.CurrentVessels)
            {
                var text = kv.Value?.ToString();
                if (VesselProtoLaunchIdScanner.TryGetMaxLaunchId(text, out var m))
                {
                    max = global::System.Math.Max(max, m);
                }
            }

            return max;
        }
    }
}
