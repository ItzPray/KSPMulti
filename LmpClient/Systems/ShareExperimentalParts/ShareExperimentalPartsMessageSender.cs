using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Network;
using LmpClient.Systems.PersistentSync;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Interface;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.ShareExperimentalParts
{
    public class ShareExperimentalPartsMessageSender : SubSystem<ShareExperimentalPartsSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<ShareProgressCliMsg>(msg)));
        }

        public void SendExperimentalPartMessage(string partName, int count)
        {
            if (PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainNames.ExperimentalParts))
            {
                PersistentSyncSystem.Singleton.MessageSender.SendExperimentalPartsIntent(new[]
                {
                    new ExperimentalPartSnapshotInfo
                    {
                        PartName = partName,
                        Count = count
                    }
                }, $"ExperimentalPart:{partName}");
                return;
            }

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<ShareProgressExperimentalPartMsgData>();
            msgData.PartName = partName;
            msgData.Count = count;

            SendMessage(msgData);
        }
    }
}
