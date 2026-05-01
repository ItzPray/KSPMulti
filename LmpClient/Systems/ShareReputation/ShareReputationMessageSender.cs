using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Systems.PersistentSync;
using LmpCommon.Message.Interface;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.ShareReputation
{
    public class ShareReputationMessageSender : SubSystem<ShareReputationSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            if (!PersistentSyncSystem.IsLiveFor<ReputationPersistentSyncClientDomain>())
            {
                return;
            }

            PersistentSyncSystem.Singleton.MessageSender.SendMessage(msg);
        }

        public void SendReputationMsg(float reputation, string reason)
        {
            if (!PersistentSyncSystem.IsLiveFor<ReputationPersistentSyncClientDomain>())
            {
                return;
            }

            PersistentSyncSystem.SendIntent<ReputationPersistentSyncClientDomain, float>(reputation, reason);
        }
    }
}
