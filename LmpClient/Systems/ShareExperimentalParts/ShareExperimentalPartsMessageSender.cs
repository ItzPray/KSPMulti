using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Network;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Interface;

namespace LmpClient.Systems.ShareExperimentalParts
{
    public class ShareExperimentalPartsMessageSender : SubSystem<ShareExperimentalPartsSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<ShareProgressCliMsg>(msg)));
        }

        /// <summary>Obsolete: publishes from <see cref="LmpClient.Systems.PersistentSync.ExperimentalPartsPersistentSyncClientDomain"/>.</summary>
        public void SendExperimentalPartMessage(string partName, int count)
        {
        }
    }
}
