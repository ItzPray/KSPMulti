using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Systems.PersistentSync;
using LmpCommon.Message.Interface;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.ShareScience
{
    public class ShareScienceMessageSender : SubSystem<ShareScienceSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            if (!PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainId.Science))
            {
                return;
            }

            PersistentSyncSystem.Singleton.MessageSender.SendMessage(msg);
        }

        public void SendScienceMessage(float science, string reason)
        {
            if (!PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainId.Science))
            {
                return;
            }

            PersistentSyncSystem.Singleton.MessageSender.SendScienceIntent(science, reason);
            LunaLog.Log($"Science changed to: {science} with reason: {reason}");
        }
    }
}
