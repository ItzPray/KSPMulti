using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Systems.PersistentSync;
using LmpCommon.Message.Interface;

namespace LmpClient.Systems.ShareScience
{
    public class ShareScienceMessageSender : SubSystem<ShareScienceSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            PersistentSyncSystem.Singleton.MessageSender.SendMessage(msg);
        }

        public void SendScienceMessage(float science, string reason)
        {
            PersistentSyncSystem.Singleton.MessageSender.SendScienceIntent(science, reason);
            LunaLog.Log($"Science changed to: {science} with reason: {reason}");
        }
    }
}
