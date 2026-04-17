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
    public static class ShareScienceSubjectSystem
    {
        public static void ScienceSubjectReceived(ClientStructure client, ShareProgressScienceSubjectMsgData data)
        {
            LunaLog.Debug($"Science experiment received: {data.ScienceSubject.Id}");

            if (PersistentSyncRegistry.IsPersistentSyncInitialized)
            {
                var payload = ScienceSubjectSnapshotPayloadSerializer.Serialize(new[]
                {
                    new ScienceSubjectSnapshotInfo
                    {
                        Id = data.ScienceSubject.Id,
                        NumBytes = data.ScienceSubject.NumBytes,
                        Data = data.ScienceSubject.Data
                    }
                });
                PersistentSyncRegistry.ApplyServerMutation(PersistentSyncDomainId.ScienceSubjects, payload, payload.Length, $"LegacyScienceSubject:{data.ScienceSubject.Id}");
                return;
            }

            //send the science subject update to all other clients
            MessageQueuer.RelayMessage<ShareProgressSrvMsg>(client, data);
            ScenarioDataUpdater.WriteScienceSubjectDataToFile(data.ScienceSubject);
        }
    }
}
