using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Extensions;
using LmpClient.Network;
using LmpClient.Systems.PersistentSync;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Interface;
using LmpCommon.PersistentSync;
using Strategies;
using System;

namespace LmpClient.Systems.ShareStrategy
{
    public class ShareStrategyMessageSender : SubSystem<ShareStrategySystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<ShareProgressCliMsg>(msg)));
        }

        public void SendStrategyMessage(Strategy strategy)
        {
            var configNode = ConvertStrategyToConfigNode(strategy);
            if (configNode == null) return;

            var data = configNode.Serialize();
            var numBytes = data.Length;

            if (PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainId.Strategy))
            {
                PersistentSyncSystem.Singleton.MessageSender.SendStrategyIntent(new[]
                {
                    new StrategySnapshotInfo
                    {
                        Name = strategy.Config.Name,
                        NumBytes = numBytes,
                        Data = data
                    }
                }, $"StrategyUpdate:{strategy.Config.Name}");
                return;
            }

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<ShareProgressStrategyMsgData>();
            msgData.Strategy.Name = strategy.Config.Name;

            msgData.Strategy.NumBytes = numBytes;
            if (msgData.Strategy.Data.Length < numBytes)
                msgData.Strategy.Data = new byte[numBytes];

            Array.Copy(data, msgData.Strategy.Data, numBytes);

            SendMessage(msgData);
        }

        private static ConfigNode ConvertStrategyToConfigNode(Strategy strategy)
        {
            var configNode = new ConfigNode();
            try
            {
                strategy.Save(configNode);
                configNode.AddValue("isActive", strategy.IsActive); //Add isActive to the node because normaly it is not saved.
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[KSPMP]: Error while saving strategy: {e}");
                return null;
            }

            return configNode;
        }


    }
}
