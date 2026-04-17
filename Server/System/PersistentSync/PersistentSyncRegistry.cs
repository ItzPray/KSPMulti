using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.Message.Server;
using LmpCommon.PersistentSync;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Server;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.System.PersistentSync
{
    public static class PersistentSyncRegistry
    {
        private static readonly Dictionary<PersistentSyncDomainId, IPersistentSyncServerDomain> Domains = new Dictionary<PersistentSyncDomainId, IPersistentSyncServerDomain>();
        private static readonly HashSet<string> ServerScenarioBypasses = new HashSet<string> { "Funding", "Reputation" };
        private static bool _initialized;

        public static void Initialize(bool createdFromScratch)
        {
            Domains.Clear();
            Register(new FundsPersistentSyncDomainStore());
            Register(new SciencePersistentSyncDomainStore());
            Register(new ReputationPersistentSyncDomainStore());

            foreach (var domain in Domains.Values)
            {
                domain.LoadFromPersistence(createdFromScratch);
            }

            _initialized = true;
        }

        public static void Reset()
        {
            Domains.Clear();
            _initialized = false;
        }

        public static bool ShouldSkipServerScenarioSync(string scenarioName)
        {
            return _initialized && ServerScenarioBypasses.Contains(scenarioName);
        }

        public static void HandleRequest(ClientStructure client, PersistentSyncRequestMsgData data)
        {
            foreach (var snapshot in GetSnapshots(data.Domains.Take(data.DomainCount)))
            {
                MessageQueuer.SendToClient<PersistentSyncSrvMsg>(client, CreateSnapshotMessage(snapshot));
            }
        }

        public static void HandleIntent(ClientStructure client, PersistentSyncIntentMsgData data)
        {
            if (!_initialized || !Domains.TryGetValue(data.DomainId, out var domain))
            {
                return;
            }

            var result = domain.ApplyClientIntent(data);
            if (!result.Accepted || result.Snapshot == null)
            {
                return;
            }

            if (result.Changed)
            {
                LunaLog.Debug($"Persistent sync intent accepted for {data.DomainId} from {client.PlayerName} with reason '{data.Reason}'");
                MessageQueuer.SendToAllClients<PersistentSyncSrvMsg>(CreateSnapshotMessage(result.Snapshot));
            }
            else if (result.ReplyToOriginClient)
            {
                MessageQueuer.SendToClient<PersistentSyncSrvMsg>(client, CreateSnapshotMessage(result.Snapshot));
            }
        }

        public static PersistentSyncDomainApplyResult ApplyServerMutation(PersistentSyncDomainId domainId, byte[] payload, int numBytes, string reason)
        {
            if (!_initialized || !Domains.TryGetValue(domainId, out var domain))
            {
                return new PersistentSyncDomainApplyResult { Accepted = false };
            }

            var result = domain.ApplyServerMutation(payload, numBytes, reason);
            if (result.Accepted && result.Changed && result.Snapshot != null)
            {
                LunaLog.Debug($"Persistent sync server mutation applied for {domainId} with reason '{reason}'");
                MessageQueuer.SendToAllClients<PersistentSyncSrvMsg>(CreateSnapshotMessage(result.Snapshot));
            }

            return result;
        }

        public static IReadOnlyCollection<PersistentSyncDomainSnapshot> GetSnapshots(IEnumerable<PersistentSyncDomainId> domainIds)
        {
            if (!_initialized)
            {
                return Array.Empty<PersistentSyncDomainSnapshot>();
            }

            var snapshots = new List<PersistentSyncDomainSnapshot>();
            foreach (var domainId in domainIds ?? Enumerable.Empty<PersistentSyncDomainId>())
            {
                if (Domains.TryGetValue(domainId, out var domain))
                {
                    snapshots.Add(domain.GetCurrentSnapshot());
                }
            }

            return snapshots;
        }

        private static void Register(IPersistentSyncServerDomain domain)
        {
            Domains[domain.DomainId] = domain;
        }

        private static PersistentSyncSnapshotMsgData CreateSnapshotMessage(PersistentSyncDomainSnapshot snapshot)
        {
            var data = ServerContext.ServerMessageFactory.CreateNewMessageData<PersistentSyncSnapshotMsgData>();
            data.DomainId = snapshot.DomainId;
            data.Revision = snapshot.Revision;
            data.AuthorityPolicy = snapshot.AuthorityPolicy;
            data.NumBytes = snapshot.NumBytes;
            data.Payload = snapshot.Payload;
            return data;
        }
    }
}
