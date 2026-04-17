using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Systems.PersistentSync;
using LmpCommon.Message.Interface;

namespace LmpClient.Systems.ShareFunds
{
    public class ShareFundsMessageSender : SubSystem<ShareFundsSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            PersistentSyncSystem.Singleton.MessageSender.SendMessage(msg);
        }

        public void SendFundsMessage(double funds, string reason)
        {
            PersistentSyncSystem.Singleton.MessageSender.SendFundsIntent(funds, reason);
        }
    }
}
