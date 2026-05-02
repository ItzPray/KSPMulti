using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Network;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Interface;
using Strategies;

namespace LmpClient.Systems.ShareStrategy
{
    public class ShareStrategyMessageSender : SubSystem<ShareStrategySystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<ShareProgressCliMsg>(msg)));
        }

        /// <summary>Obsolete: publishes from <see cref="LmpClient.Systems.PersistentSync.Domains.StrategyPersistentSyncClientDomain"/>.</summary>
        public void SendStrategyMessage(Strategy strategy)
        {
        }
    }
}
