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
    public static class ShareAchievementsSystem
    {
        public static void AchievementsReceived(ClientStructure client, ShareProgressAchievementsMsgData data)
        {
            LunaLog.Debug($"Achievements data received: {data.Id}");

            if (PersistentSyncRegistry.IsPersistentSyncInitialized)
            {
                var payload = PersistentSyncPayloadSerializer.Serialize(new AchievementsPayload
                {
                    Items = new[]
                    {
                        new AchievementSnapshotInfo
                        {
                            Id = data.Id,
                            Data = data.Data
                        }
                    }
                });
                PersistentSyncRegistry.ApplyServerMutation(PersistentSyncDomainNames.Achievements, payload, $"LegacyAchievements:{data.Id}");
                return;
            }

            //send the achievements update to all other clients
            MessageQueuer.RelayMessage<ShareProgressSrvMsg>(client, data);
            ScenarioDataUpdater.WriteAchievementDataToFile(data);
        }
    }
}

