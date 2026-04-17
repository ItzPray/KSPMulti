using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Systems.PersistentSync;
using LmpCommon.Message.Interface;

namespace LmpClient.Systems.ShareReputation
{
    public class ShareReputationMessageSender : SubSystem<ShareReputationSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            PersistentSyncSystem.Singleton.MessageSender.SendMessage(msg);
        }

        public void SendReputationMsg(float reputation, string reason)
        {
            PersistentSyncSystem.Singleton.MessageSender.SendReputationIntent(reputation, reason);
        }
    }
}
