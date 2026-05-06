using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
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
        private static readonly Dictionary<string, IPersistentSyncServerDomain> Domains = new Dictionary<string, IPersistentSyncServerDomain>();
        private static bool _initialized;

        /// <summary>
        /// True after Initialize; used for bypass guards and diagnostics.
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
            return _initialized && PersistentSyncDomainCatalog.GetServerScenarioBypasses().Contains(scenarioName);
        }

        /// <summary>
        /// Unit-test / harness hook: replaces the domain instance registered for <paramref name="domainId"/>.
        /// Call only after Initialize.
        /// </summary>
        public static void ReplaceRegisteredDomainForTests(string domainId, IPersistentSyncServerDomain domain)
        {
            Domains[domainId] = domain;
        }

        /// <summary>
        /// Returns the registered domain for <paramref name="domainId"/>, or null if missing. Used by
        /// projection domains (e.g. PartPurchases → Technology) to resolve their backing store without
        /// introducing a constructor-time dependency that conflicts with the registry's no-arg construction.
        /// </summary>
        public static IPersistentSyncServerDomain GetRegisteredDomain(string domainId)
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
        /// Applies a client intent only when ValidateClientMaySubmitIntent allows it.
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

            if (!domain.AuthorizeIntent(client, ExactPayload(data.Payload, data.NumBytes)))
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

        private static void LogAuthorityIntentRejected(string clientName, string domainId, PersistentAuthorityPolicy? policy, string rejectionReasonCategory)
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
                    return domain.DomainId == PersistentSyncDomainNames.Contracts ? "ContractLockOwnerReject" : "LockOwnerIntentStubReject";
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
                case PersistentSyncDomainNames.Contracts:
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
                        if (domainId == PersistentSyncDomainNames.GameLaunchId)
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
                if (snapshot.DomainId == PersistentSyncDomainNames.Contracts)
                {
                    try
                    {
                        var rows = PersistentSyncPayloadSerializer.Deserialize<ContractsPayload>(snapshot.Payload, snapshot.NumBytes)?.Snapshot?.Contracts?.Count ?? 0;
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

        /// <summary>
        /// Compare-only snapshot export for DEBUG Domain Analyzer — does not broadcast or mutate canonical state.
        /// </summary>
        public static void HandleAuditRequest(ClientStructure client, PersistentSyncAuditRequestMsgData data)
        {
            var clientName = client?.PlayerName ?? "<none>";
            var correlationId = data.CorrelationId;
            var requested = data.Domains.Take(data.DomainCount).ToArray();

            if (!_initialized)
            {
                foreach (var domainId in requested)
                {
                    MessageQueuer.SendToClient<PersistentSyncSrvMsg>(
                        client,
                        CreateAuditSnapshotError(correlationId, domainId, "RegistryNotInitialized"));
                }

                return;
            }

            foreach (var domainId in requested)
            {
                if (string.IsNullOrEmpty(domainId))
                {
                    MessageQueuer.SendToClient<PersistentSyncSrvMsg>(
                        client,
                        CreateAuditSnapshotError(correlationId, "<empty>", "InvalidDomainWireId"));
                    continue;
                }

                if (!Domains.TryGetValue(domainId, out var domain))
                {
                    LunaLog.Debug($"[PersistentSync] audit request skipped unknown domain client={clientName} domain={domainId}");
                    MessageQueuer.SendToClient<PersistentSyncSrvMsg>(
                        client,
                        CreateAuditSnapshotError(correlationId, domainId, "UnknownDomain"));
                    continue;
                }

                var snap = domain.GetCurrentSnapshot();
                MessageQueuer.SendToClient<PersistentSyncSrvMsg>(
                    client,
                    CreateAuditSnapshotMessage(correlationId, snap, string.Empty));
                LunaLog.Debug(
                    $"[PersistentSync] audit snapshot send client={clientName} domain={domainId} revision={snap.Revision} payloadBytes={snap.NumBytes}");
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

            if (result.ReplyToProducerClient && data.DomainId == PersistentSyncDomainNames.Contracts)
            {
                var producerName = LockSystem.LockQuery.ContractLockOwner();
                if (!string.IsNullOrEmpty(producerName) && !string.Equals(producerName, client?.PlayerName, StringComparison.Ordinal))
                {
                    var producerClient = FindClientByPlayerName(producerName);
                    if (producerClient != null)
                    {
                        // RequestOfferGeneration is a no-op on canonical state (revision unchanged). Re-sending the same
                        // snapshot revision is dropped client-side as stale after MarkApplied — the producer never woke to
                        // run ReplenishStockOffers. Send an explicit nudge instead.
                        LunaLog.Debug(
                            $"[PersistentSync] producer offer-generation nudge target=producer client={producerName} domain={data.DomainId} revision={result.Snapshot.Revision}");
                        MessageQueuer.SendToClient<PersistentSyncSrvMsg>(
                            producerClient,
                            CreateProducerOfferGenerationNudgeMessage());
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

        public static PersistentSyncDomainApplyResult ApplyServerMutation(string domainId, byte[] payload, string reason)
        {
            return ApplyServerMutationSlice(domainId, payload, payload?.Length ?? 0, reason);
        }

        public static PersistentSyncDomainApplyResult ApplyServerMutationSlice(string domainId, byte[] payload, int numBytes, string reason)
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
            var result = domain.ApplyServerMutation(ExactPayload(payload, numBytes), reason);

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

        private static byte[] ExactPayload(byte[] payload, int numBytes)
        {
            payload = payload ?? Array.Empty<byte>();
            if (numBytes < 0)
            {
                numBytes = 0;
            }

            if (numBytes >= payload.Length)
            {
                return payload;
            }

            var exact = new byte[numBytes];
            Buffer.BlockCopy(payload, 0, exact, 0, numBytes);
            return exact;
        }

        public static IReadOnlyCollection<PersistentSyncDomainSnapshot> GetSnapshots(IEnumerable<string> domainIds)
        {
            if (!_initialized)
            {
                return Array.Empty<PersistentSyncDomainSnapshot>();
            }

            var snapshots = new List<PersistentSyncDomainSnapshot>();
            foreach (var domainId in domainIds ?? Enumerable.Empty<string>())
            {
                if (Domains.TryGetValue(domainId, out var domain))
                {
                    snapshots.Add(domain.GetCurrentSnapshot());
                }
                else if (_initialized)
                {
                    if (domainId == PersistentSyncDomainNames.GameLaunchId)
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
            var concreteDomainTypes = domainTypes
                .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IPersistentSyncServerDomain).IsAssignableFrom(t))
                .ToList();

            var registrar = new PersistentSyncServerDomainRegistrar();
            foreach (var domainType in concreteDomainTypes)
            {
                InvokeDomainRegistration(domainType, registrar);
            }

            var definitions = registrar.BuildDefinitions();
            PersistentSyncDomainCatalog.Configure(definitions);

            var registeredTypes = new HashSet<Type>(definitions.Select(d => d.DomainType));
            var unregisteredTypes = concreteDomainTypes
                .Where(t => !registeredTypes.Contains(t))
                .Select(t => t.FullName)
                .OrderBy(n => n)
                .ToArray();
            if (unregisteredTypes.Length > 0)
            {
                throw new InvalidOperationException("Persistent sync server domains missing self-registration: " + string.Join(", ", unregisteredTypes));
            }

            return definitions
                .Select(d => CreateDomain(d.DomainType))
                .ToArray();
        }

        private static void InvokeDomainRegistration(Type domainType, PersistentSyncServerDomainRegistrar registrar)
        {
            var method = domainType.GetMethod(
                "RegisterPersistentSyncDomain",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(PersistentSyncServerDomainRegistrar) },
                null);

            if (method == null)
            {
                return;
            }

            registrar.WithCurrentDomainType(domainType, () => method.Invoke(null, new object[] { registrar }));
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

        private static PersistentSyncAuditSnapshotMsgData CreateAuditSnapshotMessage(int correlationId, PersistentSyncDomainSnapshot snapshot, string error)
        {
            var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<PersistentSyncAuditSnapshotMsgData>();
            msgData.CorrelationId = correlationId;
            msgData.Error = error ?? string.Empty;
            msgData.DomainId = snapshot.DomainId;
            msgData.Revision = snapshot.Revision;
            msgData.AuthorityPolicy = snapshot.AuthorityPolicy;
            msgData.NumBytes = snapshot.NumBytes;
            msgData.Payload = snapshot.Payload ?? Array.Empty<byte>();
            return msgData;
        }

        private static PersistentSyncAuditSnapshotMsgData CreateAuditSnapshotError(int correlationId, string domainId, string errorCategory)
        {
            var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<PersistentSyncAuditSnapshotMsgData>();
            msgData.CorrelationId = correlationId;
            msgData.Error = $"{errorCategory}:{domainId}";
            msgData.Revision = -1;
            msgData.NumBytes = 0;
            msgData.Payload = Array.Empty<byte>();
            if (!string.IsNullOrEmpty(domainId) && PersistentSyncDomainCatalog.TryGet(domainId, out var def))
            {
                msgData.DomainWireId = def.WireId;
            }
            else
            {
                msgData.DomainWireId = 0;
            }

            return msgData;
        }

        private static PersistentSyncProducerOfferGenerationNudgeMsgData CreateProducerOfferGenerationNudgeMessage()
        {
            return ServerContext.ServerMessageFactory.CreateNewMessageData<PersistentSyncProducerOfferGenerationNudgeMsgData>();
        }
    }
}
