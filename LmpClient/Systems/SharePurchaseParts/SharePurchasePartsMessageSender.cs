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
using System.Linq;

namespace LmpClient.Systems.SharePurchaseParts
{
    public class SharePurchasePartsMessageSender : SubSystem<SharePurchasePartsSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<ShareProgressCliMsg>(msg)));
        }

        public void SendPartPurchasedMessage(string techId, string partName)
        {
            if (PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainNames.PartPurchases))
            {
                var techState = ResearchAndDevelopment.Instance?.GetTechState(techId);
                if (techState == null)
                {
                    return;
                }

                PersistentSyncSystem.Singleton.MessageSender.SendPartPurchasesIntent(new[]
                {
                    new PartPurchaseSnapshotInfo
                    {
                        TechId = techId,
                        PartNames = techState.partsPurchased.Where(part => part != null).Select(part => part.name).Distinct().ToArray()
                    }
                }, $"PartPurchase:{techId}:{partName}");
                return;
            }

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<ShareProgressPartPurchaseMsgData>();
            msgData.PartName = partName;
            msgData.TechId = techId;

            SendMessage(msgData);
        }
    }
}
