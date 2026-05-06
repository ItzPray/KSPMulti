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
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.Message.Interface;
using LmpCommon.PersistentSync;
using System;

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

        /// <summary>DEBUG Domain Analyzer: compare-only server snapshot pull (does not apply client-side).</summary>
        public void SendAuditRequest(int correlationId, bool includeRawPayload, params string[] domainIds)
        {
            if (domainIds == null || domainIds.Length == 0)
            {
                return;
            }

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<PersistentSyncAuditRequestMsgData>();
            msgData.CorrelationId = correlationId;
            msgData.IncludeRawPayload = includeRawPayload;
            msgData.DomainCount = domainIds.Length;
            msgData.Domains = domainIds;
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

        public void SendIntent<TDomain, TPayload>(TPayload payload, string reason)
            where TDomain : SyncClientDomain<TPayload>
        {
            var domainId = PersistentSyncDomainNaming.InferDomainName(typeof(TDomain));
            SendIntent(domainId, System.GetKnownRevision(domainId), payload, reason);
        }

    }
}
