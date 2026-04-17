using LmpCommon.Enums;
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

        /// <summary>
        /// True after <see cref="Initialize"/>; used for bypass guards and diagnostics.
        /// </summary>
        public static bool IsPersistentSyncInitialized => _initialized;

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

        /// <summary>
        /// Unit-test / harness hook: replaces the domain instance registered for <paramref name="domainId"/>.
        /// Call only after <see cref="Initialize"/>.
        /// </summary>
        public static void ReplaceRegisteredDomainForTests(PersistentSyncDomainId domainId, IPersistentSyncServerDomain domain)
        {
            Domains[domainId] = domain;
        }

        /// <summary>
        /// Central authority gate for client-submitted persistent-sync intents.
        /// </summary>
        public static bool ValidateClientMaySubmitIntent(ClientStructure client, IPersistentSyncServerDomain domain)
        {
            if (domain == null)
            {
                return false;
            }

            switch (domain.AuthorityPolicy)
            {
                case PersistentAuthorityPolicy.AnyClientIntent:
                    return true;
                case PersistentAuthorityPolicy.ServerDerived:
                    return false;
                case PersistentAuthorityPolicy.LockOwnerIntent:
                    return EvaluateLockOwnerClientIntent(client, domain);
                case PersistentAuthorityPolicy.DesignatedProducer:
                    return EvaluateDesignatedProducerClientIntent(client, domain);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Applies a client intent only when <see cref="ValidateClientMaySubmitIntent"/> allows it.
        /// Does not mutate canonical domain state when validation fails.
        /// </summary>
        public static PersistentSyncDomainApplyResult ApplyClientIntentWithAuthority(ClientStructure client, PersistentSyncIntentMsgData data)
        {
            var clientName = client?.PlayerName ?? "<none>";

            if (!_initialized)
            {
                LunaLog.Error($"[PersistentSync] registry guard: client intent rejected registry not initialized client={clientName} domain={data.DomainId}");
                LogAuthorityIntentRejected(clientName, data.DomainId, null, "DomainMissingOrRegistryNotInitialized");
                return RejectedAuthorityApplyResult();
            }

            if (!Domains.TryGetValue(data.DomainId, out var domain))
            {
                LunaLog.Error($"[PersistentSync] registry guard: client intent for unknown domain client={clientName} domain={data.DomainId}");
                LogAuthorityIntentRejected(clientName, data.DomainId, null, "DomainMissingOrRegistryNotInitialized");
                return RejectedAuthorityApplyResult();
            }

            if (!ValidateClientMaySubmitIntent(client, domain))
            {
                LogAuthorityIntentRejected(clientName, data.DomainId, domain.AuthorityPolicy, DescribeAuthorityRejectionCategory(domain));
                return RejectedAuthorityApplyResult();
            }

            var revisionBefore = domain.GetCurrentSnapshot().Revision;
            var result = domain.ApplyClientIntent(data);

            if (!result.Accepted || result.Snapshot == null)
            {
                LunaLog.Debug($"[PersistentSync] intent rejected client={clientName} domain={data.DomainId} reason=IntentApplyNotAccepted");
                return result;
            }

            var revisionAfter = result.Snapshot.Revision;
            if (result.Changed)
            {
                LunaLog.Debug($"[PersistentSync] intent accepted client={clientName} domain={data.DomainId} semantic=changed revBefore={revisionBefore} revAfter={revisionAfter} reason={data.Reason}");
            }
            else
            {
                LunaLog.Debug($"[PersistentSync] intent accepted client={clientName} domain={data.DomainId} semantic=no-op rev={revisionAfter} reason={data.Reason}");
            }

            return result;
        }

        private static PersistentSyncDomainApplyResult RejectedAuthorityApplyResult()
        {
            return new PersistentSyncDomainApplyResult
            {
                Accepted = false,
                Changed = false,
                ReplyToOriginClient = false,
                Snapshot = null
            };
        }

        private static void LogAuthorityIntentRejected(string clientName, PersistentSyncDomainId domainId, PersistentAuthorityPolicy? policy, string rejectionReasonCategory)
        {
            var policyText = policy.HasValue ? policy.Value.ToString() : "n/a";
            LunaLog.Debug($"[PersistentSync] authority rejected intent client={clientName} domain={domainId} policy={policyText} category={rejectionReasonCategory}");
        }

        private static string DescribeAuthorityRejectionCategory(IPersistentSyncServerDomain domain)
        {
            switch (domain.AuthorityPolicy)
            {
                case PersistentAuthorityPolicy.ServerDerived:
                    return "ServerDerived";
                case PersistentAuthorityPolicy.LockOwnerIntent:
                    return "LockOwnerIntentStubReject";
                case PersistentAuthorityPolicy.DesignatedProducer:
                    return "DesignatedProducerStubReject";
                default:
                    return "AuthorityPolicyReject";
            }
        }

        /// <summary>
        /// Stub until lock-owner election is wired: reject all client intents for this policy.
        /// </summary>
        private static bool EvaluateLockOwnerClientIntent(ClientStructure client, IPersistentSyncServerDomain domain)
        {
            return false;
        }

        /// <summary>
        /// Stub until designated-producer election is wired: reject all client intents for this policy.
        /// </summary>
        private static bool EvaluateDesignatedProducerClientIntent(ClientStructure client, IPersistentSyncServerDomain domain)
        {
            return false;
        }

        public static void HandleRequest(ClientStructure client, PersistentSyncRequestMsgData data)
        {
            var requested = data.Domains.Take(data.DomainCount).ToArray();
            var snapshots = GetSnapshots(requested).ToList();
            var clientName = client?.PlayerName ?? "<none>";
            var domainList = string.Join(",", requested.Select(d => d.ToString()));
            LunaLog.Debug($"[PersistentSync] snapshot request client={clientName} domains=[{domainList}] returningCount={snapshots.Count}");

            if (_initialized)
            {
                foreach (var domainId in requested)
                {
                    if (!Domains.ContainsKey(domainId))
                    {
                        LunaLog.Error($"[PersistentSync] registry guard: snapshot requested for missing domain {domainId} client={clientName}");
                    }
                }
            }

            foreach (var snapshot in snapshots)
            {
                LunaLog.Debug($"[PersistentSync] snapshot send target=singleClient client={clientName} domain={snapshot.DomainId} revision={snapshot.Revision}");
                MessageQueuer.SendToClient<PersistentSyncSrvMsg>(client, CreateSnapshotMessage(snapshot));
            }
        }

        public static void HandleIntent(ClientStructure client, PersistentSyncIntentMsgData data)
        {
            var result = ApplyClientIntentWithAuthority(client, data);
            if (!result.Accepted || result.Snapshot == null)
            {
                return;
            }

            var clientName = client?.PlayerName ?? "<none>";

            if (result.Changed)
            {
                LunaLog.Debug($"[PersistentSync] snapshot broadcast target=allClients domain={data.DomainId} revision={result.Snapshot.Revision}");
                MessageQueuer.SendToAllClients<PersistentSyncSrvMsg>(CreateSnapshotMessage(result.Snapshot));
            }
            else if (result.ReplyToOriginClient)
            {
                LunaLog.Debug($"[PersistentSync] snapshot broadcast target=singleClient client={clientName} domain={data.DomainId} revision={result.Snapshot.Revision}");
                MessageQueuer.SendToClient<PersistentSyncSrvMsg>(client, CreateSnapshotMessage(result.Snapshot));
            }
        }

        public static PersistentSyncDomainApplyResult ApplyServerMutation(PersistentSyncDomainId domainId, byte[] payload, int numBytes, string reason)
        {
            if (!_initialized)
            {
                LunaLog.Error($"[PersistentSync] registry guard: server mutation rejected registry not initialized domain={domainId}");
                return new PersistentSyncDomainApplyResult { Accepted = false };
            }

            if (!Domains.TryGetValue(domainId, out var domain))
            {
                LunaLog.Error($"[PersistentSync] registry guard: server mutation rejected unknown domain={domainId}");
                return new PersistentSyncDomainApplyResult { Accepted = false };
            }

            var revisionBefore = domain.GetCurrentSnapshot().Revision;
            var result = domain.ApplyServerMutation(payload, numBytes, reason);

            if (!result.Accepted)
            {
                LunaLog.Debug($"[PersistentSync] server mutation not accepted domain={domainId} reason={reason}");
                return result;
            }

            if (result.Changed && result.Snapshot != null)
            {
                LunaLog.Debug($"[PersistentSync] server mutation applied domain={domainId} mutationReason={reason} semantic=changed revBefore={revisionBefore} revAfter={result.Snapshot.Revision}");
            }
            else
            {
                LunaLog.Debug($"[PersistentSync] server mutation applied domain={domainId} mutationReason={reason} semantic=no-op rev={revisionBefore}");
            }

            if (result.Accepted && result.Changed && result.Snapshot != null)
            {
                LunaLog.Debug($"[PersistentSync] snapshot broadcast target=allClients domain={domainId} revision={result.Snapshot.Revision}");
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
                else if (_initialized)
                {
                    LunaLog.Error($"[PersistentSync] registry guard: GetSnapshots skipped unknown domain={domainId}");
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
