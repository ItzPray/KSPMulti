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
    public class SharePartPurchaseSystem
    {
        public static void PurchaseReceived(ClientStructure client, ShareProgressPartPurchaseMsgData data)
        {
            LunaLog.Debug($"Part purchased: {data.PartName} Tech: {data.TechId}");

            if (PersistentSyncRegistry.IsPersistentSyncInitialized)
            {
                var payload = PersistentSyncPayloadSerializer.Serialize(new PartPurchasesPayload
                {
                    Items = new[]
                    {
                        new PartPurchaseSnapshotInfo
                        {
                            TechId = data.TechId,
                            PartNames = new[] { data.PartName }
                        }
                    }
                });
                PersistentSyncRegistry.ApplyServerMutation(PersistentSyncDomainNames.PartPurchases, payload, $"LegacyPartPurchase:{data.TechId}:{data.PartName}");
                return;
            }

            //send the part purchase to all other clients
            MessageQueuer.RelayMessage<ShareProgressSrvMsg>(client, data);
            ScenarioDataUpdater.WritePartPurchaseDataToFile(data);
        }
    }
}
