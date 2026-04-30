using LmpCommon.PersistentSync;
using System;

namespace LmpClient.Systems.PersistentSync
{
    /// <summary>
    /// Applies the server's <c>Game.launchID</c> high-water mark after PersistentSync snapshot delivery.
    /// Uses <see cref="Math.Max"/> with the local counter so a snapshot never regresses a client that already advanced.
    /// </summary>
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

        protected override uint DeserializePayload(byte[] payload, int numBytes)
        {
            return GameLaunchIdSnapshotPayloadSerializer.Deserialize(payload, numBytes);
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
    }
}
