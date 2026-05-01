using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Extensions;
using LmpClient.Network;
using LmpClient.Systems.PersistentSync;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Interface;
using LmpCommon.PersistentSync;
using System;

namespace LmpClient.Systems.ShareScienceSubject
{
    public class ShareScienceSubjectMessageSender : SubSystem<ShareScienceSubjectSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<ShareProgressCliMsg>(msg)));
        }

        public void SendScienceSubjectMessage(ScienceSubject subject)
        {
            var configNode = ConvertScienceSubjectToConfigNode(subject);
            if (configNode == null) return;

            var data = configNode.Serialize();
            var numBytes = data.Length;

            if (PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainNames.ScienceSubjects))
            {
                PersistentSyncSystem.Singleton.MessageSender.SendScienceSubjectsIntent(new[]
                {
                    new ScienceSubjectSnapshotInfo
                    {
                        Id = subject.id,
                        Data = data
                    }
                }, $"ScienceSubjectUpdate:{subject.id}");
                LunaLog.Log($"Science experiment \"{subject.id}\" sent as persistent sync intent");
                return;
            }

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<ShareProgressScienceSubjectMsgData>();
            msgData.ScienceSubject.Id = subject.id;

            msgData.ScienceSubject.NumBytes = numBytes;
            if (msgData.ScienceSubject.Data.Length < numBytes)
                msgData.ScienceSubject.Data = new byte[numBytes];

            Array.Copy(data, msgData.ScienceSubject.Data, numBytes);

            SendMessage(msgData);

            LunaLog.Log($"Science experiment \"{subject.id}\" sent");
        }

        private static ConfigNode ConvertScienceSubjectToConfigNode(ScienceSubject subject)
        {
            var configNode = new ConfigNode();
            try
            {
                subject.Save(configNode);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[KSPMP]: Error while saving science subject: {e}");
                return null;
            }

            return configNode;
        }
    }
}

