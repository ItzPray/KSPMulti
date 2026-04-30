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
using System.Reflection;

namespace Server.System.PersistentSync
{
    public static class PersistentSyncRegistry
    {
        private static readonly Dictionary<PersistentSyncDomainId, IPersistentSyncServerDomain> Domains = new Dictionary<PersistentSyncDomainId, IPersistentSyncServerDomain>();
        private static readonly ISet<string> ServerScenarioBypasses = PersistentSyncDomainCatalog.GetServerScenarioBypasses();
        private static bool _initialized;

        /// <summary>
        /// True after <see cref="Initialize"/>; used for bypass guards and diagnostics.
        /// </summary>
        public static bool IsPersistentSyncInitialized => _initialized;

        public static void Initialize(bool createdFromScratch)
        {
            Domains.Clear();
            GameLaunchIdScenarioBootstrap.EnsureScenarioInStore();
            foreach (var domain in CreateRegisteredDomains(typeof(IPersistentSyncServerDomain).Assembly))
            {
                Register(domain);
            }

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
        /// Returns the registered domain for <paramref name="domainId"/>, or null if missing. Used by
        /// projection domains (e.g. PartPurchases → Technology) to resolve their backing store without
        /// introducing a constructor-time dependency that conflicts with the registry's no-arg construction.
        /// </summary>
        public static IPersistentSyncServerDomain GetRegisteredDomain(PersistentSyncDomainId domainId)
        {
            return Domains.TryGetValue(domainId, out var domain) ? domain : null;
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

            if (!domain.AuthorizeIntent(client, data.Payload, data.NumBytes))
            {
                LogAuthorityIntentRejected(clientName, data.DomainId, domain.AuthorityPolicy, DescribeAuthorityRejectionCategory(domain));
                return RejectedAuthorityApplyResult();
            }

            var revisionBefore = domain.GetCurrentSnapshot().Revision;
            var result = domain.ApplyClientIntent(client, data);

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
                    return domain.DomainId == PersistentSyncDomainId.Contracts ? "ContractLockOwnerReject" : "LockOwnerIntentStubReject";
                case PersistentAuthorityPolicy.DesignatedProducer:
                    return "DesignatedProducerStubReject";
                default:
                    return "AuthorityPolicyReject";
            }
        }

        private static bool EvaluateLockOwnerClientIntent(ClientStructure client, IPersistentSyncServerDomain domain)
        {
            if (client == null || domain == null)
            {
                return false;
            }

            switch (domain.DomainId)
            {
                case PersistentSyncDomainId.Contracts:
                    return LockSystem.LockQuery.ContractLockBelongsToPlayer(client.PlayerName);
                default:
                    return false;
            }
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
                        // GameLaunchId is an optional post-handshake pull on newer clients; older servers omit the domain.
                        if (domainId == PersistentSyncDomainId.GameLaunchId)
                        {
                            LunaLog.Debug($"[PersistentSync] snapshot requested for optional domain {domainId} (not registered) client={clientName}");
                        }
                        else
                        {
                            LunaLog.Error($"[PersistentSync] registry guard: snapshot requested for missing domain {domainId} client={clientName}");
                        }
                    }
                }
            }

            foreach (var snapshot in snapshots)
            {
                if (snapshot.DomainId == PersistentSyncDomainId.Contracts)
                {
                    try
                    {
                        var rows = ContractSnapshotPayloadSerializer.Deserialize(snapshot.Payload, snapshot.NumBytes)?.Count ?? 0;
                        LunaLog.Normal(
                            $"[PersistentSync] snapshot send target=singleClient client={clientName} domain={snapshot.DomainId} " +
                            $"revision={snapshot.Revision} contractWireRows={rows} payloadBytes={snapshot.NumBytes}");
                    }
                    catch (Exception ex)
                    {
                        LunaLog.Error($"[PersistentSync] snapshot send contracts payload decode failed client={clientName}: {ex.Message}");
                    }
                }
                else
                {
                    LunaLog.Debug($"[PersistentSync] snapshot send target=singleClient client={clientName} domain={snapshot.DomainId} revision={snapshot.Revision}");
                }

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
                return;
            }

            if (result.ReplyToOriginClient)
            {
                LunaLog.Debug($"[PersistentSync] snapshot broadcast target=singleClient client={clientName} domain={data.DomainId} revision={result.Snapshot.Revision}");
                MessageQueuer.SendToClient<PersistentSyncSrvMsg>(client, CreateSnapshotMessage(result.Snapshot));
            }

            if (result.ReplyToProducerClient && data.DomainId == PersistentSyncDomainId.Contracts)
            {
                var producerName = LockSystem.LockQuery.ContractLockOwner();
                if (!string.IsNullOrEmpty(producerName) && !string.Equals(producerName, client?.PlayerName, StringComparison.Ordinal))
                {
                    var producerClient = FindClientByPlayerName(producerName);
                    if (producerClient != null)
                    {
                        LunaLog.Debug($"[PersistentSync] snapshot broadcast target=producer client={producerName} domain={data.DomainId} revision={result.Snapshot.Revision} reason=OfferGenerationRequest");
                        MessageQueuer.SendToClient<PersistentSyncSrvMsg>(producerClient, CreateSnapshotMessage(result.Snapshot));
                    }
                }
            }
        }

        private static ClientStructure FindClientByPlayerName(string playerName)
        {
            if (string.IsNullOrEmpty(playerName))
            {
                return null;
            }

            foreach (var c in ServerContext.Clients.Values)
            {
                if (string.Equals(c?.PlayerName, playerName, StringComparison.Ordinal))
                {
                    return c;
                }
            }

            return null;
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
                    if (domainId == PersistentSyncDomainId.GameLaunchId)
                    {
                        LunaLog.Debug($"[PersistentSync] GetSnapshots skipped optional domain={domainId}");
                    }
                    else
                    {
                        LunaLog.Error($"[PersistentSync] registry guard: GetSnapshots skipped unknown domain={domainId}");
                    }
                }
            }

            return snapshots;
        }

        private static void Register(IPersistentSyncServerDomain domain)
        {
            Domains[domain.DomainId] = domain;
        }

        public static IReadOnlyCollection<IPersistentSyncServerDomain> CreateRegisteredDomainsForTests(Assembly assembly)
        {
            return CreateRegisteredDomains(assembly.GetTypes());
        }

        public static IReadOnlyCollection<IPersistentSyncServerDomain> CreateRegisteredDomainsForTests(params Type[] domainTypes)
        {
            return CreateRegisteredDomains(domainTypes);
        }

        private static IReadOnlyCollection<IPersistentSyncServerDomain> CreateRegisteredDomains(Assembly assembly)
        {
            return CreateRegisteredDomains(assembly.GetTypes());
        }

        private static IReadOnlyCollection<IPersistentSyncServerDomain> CreateRegisteredDomains(IEnumerable<Type> domainTypes)
        {
            var domains = domainTypes
                .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IPersistentSyncServerDomain).IsAssignableFrom(t))
                .Select(CreateDomain)
                .ToList();

            var missingCatalog = domains
                .Where(d => !PersistentSyncDomainCatalog.TryGet(d.DomainId, out _))
                .Select(d => d.GetType().FullName)
                .OrderBy(n => n)
                .ToList();
            if (missingCatalog.Count > 0)
            {
                throw new InvalidOperationException("Persistent sync server domains missing catalog entries: " + string.Join(", ", missingCatalog));
            }

            var duplicateIds = domains
                .GroupBy(d => d.DomainId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key.ToString())
                .OrderBy(n => n)
                .ToList();
            if (duplicateIds.Count > 0)
            {
                throw new InvalidOperationException("Duplicate persistent sync server domains: " + string.Join(", ", duplicateIds));
            }

            return domains
                .OrderBy(d => PersistentSyncDomainCatalog.GetOrder(d.DomainId))
                .ToArray();
        }

        private static IPersistentSyncServerDomain CreateDomain(Type type)
        {
            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new InvalidOperationException($"Persistent sync server domain {type.FullName} must have a public parameterless constructor.");
            }

            return (IPersistentSyncServerDomain)Activator.CreateInstance(type);
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
