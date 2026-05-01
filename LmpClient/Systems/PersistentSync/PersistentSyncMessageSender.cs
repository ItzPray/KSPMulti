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

        public void SendRequest(params string[] domains)
        {
            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<PersistentSyncRequestMsgData>();
            msgData.DomainCount = domains.Length;
            msgData.Domains = domains;
            SendMessage(msgData);
        }

        public void SendIntent(string domainId, long clientKnownRevision, byte[] payload, string reason)
        {
            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<PersistentSyncIntentMsgData>();
            msgData.DomainId = domainId;
            msgData.ClientKnownRevision = clientKnownRevision;
            msgData.Payload = payload;
            msgData.NumBytes = payload.Length;
            msgData.Reason = reason;
            SendMessage(msgData);
        }

        public void SendIntent<TPayload>(string domainId, long clientKnownRevision, TPayload payload, string reason)
        {
            SendIntent(domainId, clientKnownRevision, PersistentSyncPayloadSerializer.Serialize(payload), reason);
        }

        public void SendFundsIntent(double funds, string reason)
        {
            SendIntent(PersistentSyncDomainNames.Funds, System.GetKnownRevision(PersistentSyncDomainNames.Funds), funds, reason);
        }

        public void SendScienceIntent(float science, string reason)
        {
            SendIntent(PersistentSyncDomainNames.Science, System.GetKnownRevision(PersistentSyncDomainNames.Science), science, reason);
        }

        public void SendReputationIntent(float reputation, string reason)
        {
            SendIntent(PersistentSyncDomainNames.Reputation, System.GetKnownRevision(PersistentSyncDomainNames.Reputation), reputation, reason);
        }

        public void SendUpgradeableFacilityIntent(string facilityId, int level, string reason)
        {
            SendIntent(PersistentSyncDomainNames.UpgradeableFacilities, System.GetKnownRevision(PersistentSyncDomainNames.UpgradeableFacilities), new UpgradeableFacilityLevelPayload { FacilityId = facilityId, Level = level }, reason);
        }

        public void SendContractsIntentPayload(ContractIntentPayload payload, string reason)
        {
            SendIntent(PersistentSyncDomainNames.Contracts, System.GetKnownRevision(PersistentSyncDomainNames.Contracts), payload, reason);
        }

        public void SendTechnologyIntent(TechnologySnapshotInfo[] technologies, string reason)
        {
            SendIntent(PersistentSyncDomainNames.Technology, System.GetKnownRevision(PersistentSyncDomainNames.Technology), technologies, reason);
        }

        public void SendStrategyIntent(StrategySnapshotInfo[] strategies, string reason)
        {
            SendIntent(PersistentSyncDomainNames.Strategy, System.GetKnownRevision(PersistentSyncDomainNames.Strategy), strategies, reason);
        }

        public void SendAchievementsIntent(AchievementSnapshotInfo[] achievements, string reason)
        {
            SendIntent(PersistentSyncDomainNames.Achievements, System.GetKnownRevision(PersistentSyncDomainNames.Achievements), achievements, reason);
        }

        public void SendScienceSubjectsIntent(ScienceSubjectSnapshotInfo[] subjects, string reason)
        {
            SendIntent(PersistentSyncDomainNames.ScienceSubjects, System.GetKnownRevision(PersistentSyncDomainNames.ScienceSubjects), subjects, reason);
        }

        public void SendExperimentalPartsIntent(ExperimentalPartSnapshotInfo[] parts, string reason)
        {
            SendIntent(PersistentSyncDomainNames.ExperimentalParts, System.GetKnownRevision(PersistentSyncDomainNames.ExperimentalParts), parts, reason);
        }

        public void SendPartPurchasesIntent(PartPurchaseSnapshotInfo[] purchases, string reason)
        {
            SendIntent(PersistentSyncDomainNames.PartPurchases, System.GetKnownRevision(PersistentSyncDomainNames.PartPurchases), purchases, reason);
        }
    }
}
