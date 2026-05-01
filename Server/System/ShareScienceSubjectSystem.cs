using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
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
                var payload = PersistentSyncPayloadSerializer.Serialize(new[]
                {
                    new ScienceSubjectSnapshotInfo
                    {
                        Id = data.ScienceSubject.Id,
                        Data = data.ScienceSubject.Data
                    }
                });
                PersistentSyncRegistry.ApplyServerMutation(PersistentSyncDomainNames.ScienceSubjects, payload, $"LegacyScienceSubject:{data.ScienceSubject.Id}");
                return;
            }

            //send the science subject update to all other clients
            MessageQueuer.RelayMessage<ShareProgressSrvMsg>(client, data);
            ScenarioDataUpdater.WriteScienceSubjectDataToFile(data.ScienceSubject);
        }
    }
}

