using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
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

            if (PersistentSyncSystem.IsLiveFor<ScienceSubjectsPersistentSyncClientDomain>())
            {
                PersistentSyncSystem.SendIntent<ScienceSubjectsPersistentSyncClientDomain, ScienceSubjectsPayload>(new ScienceSubjectsPayload
                {
                    Items = new[]
                    {
                        new ScienceSubjectSnapshotInfo
                        {
                            Id = subject.id,
                            Data = data
                        }
                    }
                }, $"ScienceSubjectUpdate:{subject.id}");
                LunaLog.Log($"Science experiment \"{subject.id}\" sent as persistent sync intent");
            }
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

