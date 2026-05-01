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

        public void SendIntent<TPayload>(PersistentSyncDomainId domainId, long clientKnownRevision, TPayload payload, string reason)
        {
            SendIntent(domainId, clientKnownRevision, PersistentSyncPayloadSerializer.Serialize(payload), reason);
        }

        public void SendFundsIntent(double funds, string reason)
        {
            SendIntent(PersistentSyncDomainId.Funds, System.GetKnownRevision(PersistentSyncDomainId.Funds), new PersistentSyncValueWithReason<double>(funds, reason), reason);
        }

        public void SendScienceIntent(float science, string reason)
        {
            SendIntent(PersistentSyncDomainId.Science, System.GetKnownRevision(PersistentSyncDomainId.Science), new PersistentSyncValueWithReason<float>(science, reason), reason);
        }

        public void SendReputationIntent(float reputation, string reason)
        {
            SendIntent(PersistentSyncDomainId.Reputation, System.GetKnownRevision(PersistentSyncDomainId.Reputation), new PersistentSyncValueWithReason<float>(reputation, reason), reason);
        }

        public void SendUpgradeableFacilityIntent(string facilityId, int level, string reason)
        {
            SendIntent(PersistentSyncDomainId.UpgradeableFacilities, System.GetKnownRevision(PersistentSyncDomainId.UpgradeableFacilities), new UpgradeableFacilityLevelPayload { FacilityId = facilityId, Level = level }, reason);
        }

        public void SendContractsIntentPayload(ContractIntentPayload payload, string reason)
        {
            SendIntent(PersistentSyncDomainId.Contracts, System.GetKnownRevision(PersistentSyncDomainId.Contracts), payload, reason);
        }

        public void SendTechnologyIntent(TechnologySnapshotInfo[] technologies, string reason)
        {
            SendIntent(PersistentSyncDomainId.Technology, System.GetKnownRevision(PersistentSyncDomainId.Technology), technologies, reason);
        }

        public void SendStrategyIntent(StrategySnapshotInfo[] strategies, string reason)
        {
            SendIntent(PersistentSyncDomainId.Strategy, System.GetKnownRevision(PersistentSyncDomainId.Strategy), strategies, reason);
        }

        public void SendAchievementsIntent(AchievementSnapshotInfo[] achievements, string reason)
        {
            SendIntent(PersistentSyncDomainId.Achievements, System.GetKnownRevision(PersistentSyncDomainId.Achievements), achievements, reason);
        }

        public void SendScienceSubjectsIntent(ScienceSubjectSnapshotInfo[] subjects, string reason)
        {
            SendIntent(PersistentSyncDomainId.ScienceSubjects, System.GetKnownRevision(PersistentSyncDomainId.ScienceSubjects), subjects, reason);
        }

        public void SendExperimentalPartsIntent(ExperimentalPartSnapshotInfo[] parts, string reason)
        {
            SendIntent(PersistentSyncDomainId.ExperimentalParts, System.GetKnownRevision(PersistentSyncDomainId.ExperimentalParts), parts, reason);
        }

        public void SendPartPurchasesIntent(PartPurchaseSnapshotInfo[] purchases, string reason)
        {
            SendIntent(PersistentSyncDomainId.PartPurchases, System.GetKnownRevision(PersistentSyncDomainId.PartPurchases), purchases, reason);
        }
    }
}
