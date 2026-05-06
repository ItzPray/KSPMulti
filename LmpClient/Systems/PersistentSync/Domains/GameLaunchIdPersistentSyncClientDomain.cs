using LmpCommon.PersistentSync;
using System;

namespace LmpClient.Systems.PersistentSync
{
    // Keep Game.launchID monotonic when applying the server's high-water mark.
    public sealed class GameLaunchIdPersistentSyncClientDomain : SyncClientDomain<uint>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesClientDomain<GameLaunchIdPersistentSyncClientDomain>();
        }

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

        protected override bool TryBuildLocalAuditPayload(out uint payload, out string unavailableReason)
        {
            if (HighLogic.CurrentGame == null)
            {
                payload = default;
                unavailableReason = "HighLogic.CurrentGame is null";
                return false;
            }

            payload = HighLogic.CurrentGame.launchID;
            unavailableReason = null;
            return true;
        }
    }
}
