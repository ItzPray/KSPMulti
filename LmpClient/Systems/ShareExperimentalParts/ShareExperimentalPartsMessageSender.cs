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
            if (PersistentSyncSystem.IsLiveFor<ExperimentalPartsPersistentSyncClientDomain>())
            {
                PersistentSyncSystem.SendIntent<ExperimentalPartsPersistentSyncClientDomain, ExperimentalPartsPayload>(new ExperimentalPartsPayload
                {
                    Items = new[]
                    {
                        new ExperimentalPartSnapshotInfo
                        {
                            PartName = partName,
                            Count = count
                        }
                    }
                }, $"ExperimentalPart:{partName}");
            }
        }
    }
}
