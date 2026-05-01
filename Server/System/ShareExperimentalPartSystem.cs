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
    public class ShareExperimentalPartSystem
    {
        public static void ExperimentalPartReceived(ClientStructure client, ShareProgressExperimentalPartMsgData data)
        {
            LunaLog.Debug($"Experimental part received: {data.PartName} Count: {data.Count}");

            if (PersistentSyncRegistry.IsPersistentSyncInitialized)
            {
                var payload = PersistentSyncPayloadSerializer.Serialize(new[]
                {
                    new ExperimentalPartSnapshotInfo
                    {
                        PartName = data.PartName,
                        Count = data.Count
                    }
                });
                PersistentSyncRegistry.ApplyServerMutation(PersistentSyncDomainId.ExperimentalParts, payload, payload.Length, $"LegacyExperimentalPart:{data.PartName}");
                return;
            }

            //send the experimental part to all other clients
            MessageQueuer.RelayMessage<ShareProgressSrvMsg>(client, data);
            ScenarioDataUpdater.WriteExperimentalPartDataToFile(data);
        }
    }
}
