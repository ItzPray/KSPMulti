using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Systems.PersistentSync;
using LmpCommon.Message.Interface;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.ShareFunds
{
    public class ShareFundsMessageSender : SubSystem<ShareFundsSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            if (!PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainNames.Funds))
            {
                return;
            }

            PersistentSyncSystem.Singleton.MessageSender.SendMessage(msg);
        }

        public void SendFundsMessage(double funds, string reason)
        {
            if (!PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainNames.Funds))
            {
                return;
            }

            PersistentSyncSystem.Singleton.MessageSender.SendFundsIntent(funds, reason);
        }
    }
}
