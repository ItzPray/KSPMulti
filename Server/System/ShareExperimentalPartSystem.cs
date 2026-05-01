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
    public class ShareExperimentalPartSystem
    {
        public static void ExperimentalPartReceived(ClientStructure client, ShareProgressExperimentalPartMsgData data)
        {
            LunaLog.Debug($"Experimental part received: {data.PartName} Count: {data.Count}");

            if (PersistentSyncRegistry.IsPersistentSyncInitialized)
            {
                var payload = PersistentSyncPayloadSerializer.Serialize(new ExperimentalPartsPayload
                {
                    Items = new[]
                    {
                        new ExperimentalPartSnapshotInfo
                        {
                            PartName = data.PartName,
                            Count = data.Count
                        }
                    }
                });
                PersistentSyncRegistry.ApplyServerMutation(PersistentSyncDomainNames.ExperimentalParts, payload, $"LegacyExperimentalPart:{data.PartName}");
                return;
            }

            //send the experimental part to all other clients
            MessageQueuer.RelayMessage<ShareProgressSrvMsg>(client, data);
            ScenarioDataUpdater.WriteExperimentalPartDataToFile(data);
        }
    }
}
