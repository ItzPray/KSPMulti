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
                var payload = AchievementSnapshotPayloadSerializer.Serialize(new[]
                {
                    new AchievementSnapshotInfo
                    {
                        Id = data.Id,
                        NumBytes = data.NumBytes,
                        Data = data.Data
                    }
                });
                PersistentSyncRegistry.ApplyServerMutation(PersistentSyncDomainId.Achievements, payload, payload.Length, $"LegacyAchievements:{data.Id}");
                return;
            }

            //send the achievements update to all other clients
            MessageQueuer.RelayMessage<ShareProgressSrvMsg>(client, data);
            ScenarioDataUpdater.WriteAchievementDataToFile(data);
        }
    }
}
