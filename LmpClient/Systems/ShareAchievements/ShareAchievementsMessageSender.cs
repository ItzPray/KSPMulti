using HarmonyLib;
using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Extensions;
using LmpClient.Network;
using LmpClient.Systems.PersistentSync;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Interface;
using LmpCommon.PersistentSync;
using System;

namespace LmpClient.Systems.ShareAchievements
{
    public class ShareAchievementsMessageSender : SubSystem<ShareAchievementsSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<ShareProgressCliMsg>(msg)));
        }

        public void SendAchievementsMessage(ProgressNode achievement)
        {
            //We only send the ProgressNodes that are CelestialBodySubtree
            var foundNode = ProgressTracking.Instance.FindNode(achievement.Id);
            if (foundNode == null)
            {
                var traverse = new Traverse(achievement).Field<CelestialBody>("body");

                var body = traverse.Value ? traverse.Value.name : null;
                if (body != null)
                {
                    foundNode = ProgressTracking.Instance.FindNode(body);
                }
            }

            if (foundNode != null)
            {
                var configNode = ConvertAchievementToConfigNode(foundNode);
                if (configNode == null) return;

                if (PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainId.Achievements))
                {
                    PersistentSyncSystem.Singleton.MessageSender.SendAchievementsIntent(new[]
                    {
                        new AchievementSnapshotInfo
                        {
                            Id = foundNode.Id,
                            Data = configNode.Serialize()
                        }
                    }, $"AchievementUpdate:{foundNode.Id}");
                    return;
                }

                //Build the packet and send it.
                var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<ShareProgressAchievementsMsgData>();
                msgData.Id = foundNode.Id;
                msgData.Data = configNode.Serialize();
                msgData.NumBytes = msgData.Data.Length;
                System.MessageSender.SendMessage(msgData);
            }
        }

        private static ConfigNode ConvertAchievementToConfigNode(ProgressNode achievement)
        {
            var configNode = new ConfigNode(achievement.Id);
            try
            {
                achievement.Save(configNode);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[KSPMP]: Error while saving achievement: {e}");
                return null;
            }

            return configNode;
        }
    }
}

