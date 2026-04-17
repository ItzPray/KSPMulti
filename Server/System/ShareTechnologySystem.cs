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
    public static class ShareTechnologySystem
    {
        public static void TechnologyReceived(ClientStructure client, ShareProgressTechnologyMsgData data)
        {
            LunaLog.Debug($"Technology unlocked: {data.TechNode.Id}");

            if (PersistentSyncRegistry.IsPersistentSyncInitialized)
            {
                var snapshotInfo = new TechnologySnapshotInfo
                {
                    TechId = data.TechNode.Id,
                    NumBytes = data.TechNode.NumBytes,
                    Data = new byte[data.TechNode.NumBytes]
                };
                global::System.Array.Copy(data.TechNode.Data, snapshotInfo.Data, data.TechNode.NumBytes);

                var payload = TechnologySnapshotPayloadSerializer.Serialize(new[] { snapshotInfo });
                PersistentSyncRegistry.ApplyServerMutation(PersistentSyncDomainId.Technology, payload, payload.Length, "LegacyShareTechnology");
                return;
            }

            //Send the technology update to all other clients
            MessageQueuer.RelayMessage<ShareProgressSrvMsg>(client, data);
            ScenarioDataUpdater.WriteTechnologyDataToFile(data);
        }
    }
}
