using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Server;
using LmpCommon.PersistentSync;
using Server.Client;
using Server.Log;
using Server.Server;
using Server.System.PersistentSync;
using Server.System.Scenario;

namespace Server.System
{
    public static class ShareStrategySystem
    {
        public static void StrategyReceived(ClientStructure client, ShareProgressStrategyMsgData data)
        {
            LunaLog.Debug($"strategy changed: {data.Strategy.Name}");

            if (PersistentSyncRegistry.IsPersistentSyncInitialized)
            {
                var payload = PersistentSyncPayloadSerializer.Serialize(new[]
                {
                    new StrategySnapshotInfo
                    {
                        Name = data.Strategy.Name,
                        Data = data.Strategy.Data
                    }
                });
                PersistentSyncRegistry.ApplyServerMutation(PersistentSyncDomainNames.Strategy, payload, payload.Length, $"LegacyStrategy:{data.Strategy.Name}");
                return;
            }

            //Send the strategy update to all other clients
            MessageQueuer.RelayMessage<ShareProgressSrvMsg>(client, data);
            ScenarioDataUpdater.WriteStrategyDataToFile(data.Strategy);
        }
    }
}

