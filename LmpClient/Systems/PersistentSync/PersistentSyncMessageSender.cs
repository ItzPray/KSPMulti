using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Network;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.Message.Interface;
using LmpCommon.PersistentSync;

namespace LmpClient.Systems.PersistentSync
{
    public class PersistentSyncMessageSender : SubSystem<PersistentSyncSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<PersistentSyncCliMsg>(msg)));
        }

        public void SendRequest(params PersistentSyncDomainId[] domains)
        {
            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<PersistentSyncRequestMsgData>();
            msgData.DomainCount = domains.Length;
            msgData.Domains = domains;
            SendMessage(msgData);
        }

        public void SendIntent(PersistentSyncDomainId domainId, long clientKnownRevision, byte[] payload, string reason)
        {
            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<PersistentSyncIntentMsgData>();
            msgData.DomainId = domainId;
            msgData.ClientKnownRevision = clientKnownRevision;
            msgData.Payload = payload;
            msgData.NumBytes = payload.Length;
            msgData.Reason = reason;
            SendMessage(msgData);
        }

        public void SendFundsIntent(double funds, string reason)
        {
            var payload = FundsIntentPayloadSerializer.Serialize(funds, reason);
            SendIntent(PersistentSyncDomainId.Funds, System.GetKnownRevision(PersistentSyncDomainId.Funds), payload, reason);
        }

        public void SendScienceIntent(float science, string reason)
        {
            var payload = ScienceIntentPayloadSerializer.Serialize(science, reason);
            SendIntent(PersistentSyncDomainId.Science, System.GetKnownRevision(PersistentSyncDomainId.Science), payload, reason);
        }

        public void SendReputationIntent(float reputation, string reason)
        {
            var payload = ReputationIntentPayloadSerializer.Serialize(reputation, reason);
            SendIntent(PersistentSyncDomainId.Reputation, System.GetKnownRevision(PersistentSyncDomainId.Reputation), payload, reason);
        }

        public void SendUpgradeableFacilityIntent(string facilityId, int level, string reason)
        {
            var payload = UpgradeableFacilitiesIntentPayloadSerializer.Serialize(facilityId, level);
            SendIntent(PersistentSyncDomainId.UpgradeableFacilities, System.GetKnownRevision(PersistentSyncDomainId.UpgradeableFacilities), payload, reason);
        }

        public void SendContractsIntent(ContractSnapshotInfo[] contracts, string reason)
        {
            var payload = ContractSnapshotPayloadSerializer.Serialize(contracts);
            SendIntent(PersistentSyncDomainId.Contracts, System.GetKnownRevision(PersistentSyncDomainId.Contracts), payload, reason);
        }

        public void SendTechnologyIntent(TechnologySnapshotInfo[] technologies, string reason)
        {
            var payload = TechnologySnapshotPayloadSerializer.Serialize(technologies);
            SendIntent(PersistentSyncDomainId.Technology, System.GetKnownRevision(PersistentSyncDomainId.Technology), payload, reason);
        }

        public void SendStrategyIntent(StrategySnapshotInfo[] strategies, string reason)
        {
            var payload = StrategySnapshotPayloadSerializer.Serialize(strategies);
            SendIntent(PersistentSyncDomainId.Strategy, System.GetKnownRevision(PersistentSyncDomainId.Strategy), payload, reason);
        }

        public void SendAchievementsIntent(AchievementSnapshotInfo[] achievements, string reason)
        {
            var payload = AchievementSnapshotPayloadSerializer.Serialize(achievements);
            SendIntent(PersistentSyncDomainId.Achievements, System.GetKnownRevision(PersistentSyncDomainId.Achievements), payload, reason);
        }

        public void SendScienceSubjectsIntent(ScienceSubjectSnapshotInfo[] subjects, string reason)
        {
            var payload = ScienceSubjectSnapshotPayloadSerializer.Serialize(subjects);
            SendIntent(PersistentSyncDomainId.ScienceSubjects, System.GetKnownRevision(PersistentSyncDomainId.ScienceSubjects), payload, reason);
        }

        public void SendExperimentalPartsIntent(ExperimentalPartSnapshotInfo[] parts, string reason)
        {
            var payload = ExperimentalPartsSnapshotPayloadSerializer.Serialize(parts);
            SendIntent(PersistentSyncDomainId.ExperimentalParts, System.GetKnownRevision(PersistentSyncDomainId.ExperimentalParts), payload, reason);
        }

        public void SendPartPurchasesIntent(PartPurchaseSnapshotInfo[] purchases, string reason)
        {
            var payload = PartPurchasesSnapshotPayloadSerializer.Serialize(purchases);
            SendIntent(PersistentSyncDomainId.PartPurchases, System.GetKnownRevision(PersistentSyncDomainId.PartPurchases), payload, reason);
        }
    }
}
