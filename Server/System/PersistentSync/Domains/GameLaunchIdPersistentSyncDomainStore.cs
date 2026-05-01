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
    [PersistentSyncOwnedScenario(GameLaunchIdScenarioBootstrap.ScenarioKey, ScalarField = "launchID")]
    public sealed class GameLaunchIdPersistentSyncDomainStore : SyncDomainStore<uint>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncServerDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesServerDomain<GameLaunchIdPersistentSyncDomainStore>();
        }

        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        protected override uint CreateDefaultPayload()
        {
            return 1u;
        }

        protected override uint LoadPayload(ConfigNode scenario, bool createdFromScratch)
        {
            var fromFile = ParseLaunchIdFromScenario(scenario);
            var fromVessels = ScanMaxLaunchIdAcrossLoadedVessels();
            var merged = global::System.Math.Max(fromFile, fromVessels);
            if (merged < 1)
            {
                merged = 1;
            }

            return merged;
        }

        protected override SyncChangeResult<uint> HandleIncomingPayload(
            ClientStructure client,
            uint current,
            uint incoming,
            string reason,
            bool isServerMutation)
        {
            try
            {
                var next = global::System.Math.Max(current < 1 ? 1u : current, incoming < 1 ? 1u : incoming);
                if (next < 1)
                {
                    next = 1;
                }

                return SyncChangeResult<uint>.Accept(next);
            }
            catch
            {
                return SyncChangeResult<uint>.Reject();
            }
        }

        protected override ConfigNode WritePayload(ConfigNode scenario, uint payload)
        {
            if (scenario == null)
            {
                return scenario;
            }

            scenario.UpdateValue("launchID", payload.ToString(CultureInfo.InvariantCulture));
            return scenario;
        }

        protected override bool ShouldWriteBackAfterLoad(uint loaded, ConfigNode scenario)
        {
            if (scenario == null)
            {
                return false;
            }

            var fileVal = ParseLaunchIdFromScenario(scenario);
            return loaded != fileVal;
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
