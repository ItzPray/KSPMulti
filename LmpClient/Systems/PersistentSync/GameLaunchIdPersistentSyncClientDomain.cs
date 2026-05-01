using LmpCommon.PersistentSync;
using System;

namespace LmpClient.Systems.PersistentSync
{
    // Keep Game.launchID monotonic when applying the server's high-water mark.
    public sealed class GameLaunchIdPersistentSyncClientDomain : ScalarPersistentSyncClientDomain<uint>
    {
        public static readonly PersistentSyncDomainKey Domain = PersistentSyncDomain.Define("GameLaunchId", 11);

        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.Register(Domain)
                .OwnsStockScenario("LmpGameLaunchId")
                .UsesClientDomain<GameLaunchIdPersistentSyncClientDomain>();
        }

        public override PersistentSyncDomainId DomainId => Domain.LegacyId;

        protected override bool CanApplyLiveState()
        {
            return HighLogic.CurrentGame != null;
        }

        protected override void ApplyLiveState(uint value)
        {
            var local = HighLogic.CurrentGame.launchID;
            var merged = Math.Max(local, value);
            if (merged == local)
            {
                return;
            }

            HighLogic.CurrentGame.launchID = merged;
        }
    }
}
