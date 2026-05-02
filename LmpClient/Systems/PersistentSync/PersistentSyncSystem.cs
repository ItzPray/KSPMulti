using LmpClient;
using LmpClient.Base;
using LmpClient.Events;
using LmpClient.Systems.Network;
using LmpClient.Systems.SettingsSys;
using LmpClient.Utilities;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LmpClient.Systems.PersistentSync
{
    public class PersistentSyncSystem : MessageSystem<PersistentSyncSystem, PersistentSyncMessageSender, PersistentSyncMessageHandler>
    {
        public override string SystemName { get; } = nameof(PersistentSyncSystem);

        public override int ExecutionOrder => -50;

        protected override ClientState EnableStage => ClientState.ScenariosSynced;

        /// <summary>
        /// Per join session: <see cref="PersistentSyncDomainNames.GameLaunchId"/> is requested only after the mandatory
        /// persistent-sync handshake completes so older servers (no domain 11) do not block the join batch.
        /// </summary>
        private bool _optionalGameLaunchIdPullSent;

        public PersistentSyncReconciler Reconciler { get; } = new PersistentSyncReconciler();

        private static PersistentSyncDomainDefinition[] _clientLocalDefinitions;

        public Dictionary<string, IPersistentSyncClientDomain> Domains { get; } = InitDomains();

        private static Dictionary<string, IPersistentSyncClientDomain> InitDomains()
        {
            return CreateRegisteredDomains(typeof(PersistentSyncSystem).Assembly, out _clientLocalDefinitions);
        }

        /// <summary>
        /// Applies the server-authoritative persistent sync catalog from the settings reply.
        /// </summary>
        public static bool TryApplyPersistentSyncCatalog(byte catalogWireVersion, PersistentSyncCatalogRowWire[] rows, out string failureReason)
        {
            failureReason = null;
            if (_clientLocalDefinitions == null || _clientLocalDefinitions.Length == 0)
            {
                failureReason = "Persistent sync local definitions are not initialized.";
                return false;
            }

            PersistentSyncDomainDefinition[] merged;
            if (rows == null || rows.Length == 0)
            {
                failureReason = "[KSPMP] Server did not advertise a persistent sync catalog. Update client or server build.";
                return false;
            }
            else
            {
                if (catalogWireVersion != PersistentSyncCatalogWire.CurrentVersion)
                {
                    failureReason =
                        $"[KSPMP] Unsupported persistent sync catalog wire version {catalogWireVersion}. Update this client.";
                    return false;
                }

                if (!PersistentSyncCatalogMerger.TryMerge(_clientLocalDefinitions, rows, out merged, out failureReason))
                {
                    return false;
                }
            }

            PersistentSyncDomainCatalog.Configure(merged);
            return true;
        }

        public static bool IsLiveFor<TDomain>() where TDomain : IPersistentSyncClientDomain
        {
            var domainName = PersistentSyncDomainNaming.InferDomainName(typeof(TDomain));
            return IsLiveForDomain(domainName);
        }

        protected override void OnEnabled()
        {
            base.OnEnabled();
            SetupRoutine(new RoutineDefinition(1000, RoutineExecution.Update, FlushPendingState));
            GameEvents.onLevelWasLoadedGUIReady.Add(OnSceneReady);
            GameEvents.onGUIRnDComplexSpawn.Add(OnRnDComplexSpawn);
            EnableDomainLifecycles();
        }

        /// <summary>
        /// Single live-predicate every <c>Share*MessageSender</c> must use when deciding whether to publish a client
        /// intent to the server. Returns true only when:
        /// <list type="bullet">
        /// <item><description>the system singleton exists and is Enabled (post-scenario-sync, pre-disconnect), AND</description></item>
        /// <item><description>a client-side domain handler for <paramref name="domainId"/> is registered (i.e., the
        /// scenario-domain layer for <paramref name="domainId"/> is actually live in this session).</description></item>
        /// </list>
        ///
        /// Scenario Sync Domain Contract rule: per-domain predicates (<c>IsPersistentSyncLiveForContracts</c>, inline
        /// <c>PersistentSyncSystem.Singleton.Enabled</c> checks, missing checks) are forbidden. Use this instead.
        /// </summary>
        public static bool IsLiveForDomain(string domainId)
        {
            var singleton = Singleton;
            return singleton != null
                   && singleton.Enabled
                   && singleton.Domains != null
                   && singleton.Domains.ContainsKey(domainId)
                   && PersistentSyncDomainCatalog.TryGet(domainId, out _);
        }

        protected override void OnDisabled()
        {
            DisableDomainLifecycles();
            base.OnDisabled();
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnSceneReady);
            GameEvents.onGUIRnDComplexSpawn.Remove(OnRnDComplexSpawn);
            Reconciler.Reset(new string[0]);
            _optionalGameLaunchIdPullSent = false;
        }

        public void StartInitialSync()
        {
            _optionalGameLaunchIdPullSent = false;
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            var requiredDomains = PersistentSyncDomainApplicability
                .GetRequiredDomainsForInitialSync(SettingsSystem.ServerSettings.GameMode, caps)
                .ToArray();
            Reconciler.Reset(requiredDomains);

            if (!requiredDomains.Any())
            {
                LunaLog.Log("[PersistentSync] StartInitialSync no required domains for current game mode; advancing to PersistentStateSynced");
                MainSystem.NetworkState = ClientState.PersistentStateSynced;
                RequestOptionalGameLaunchIdSnapshotAfterMandatorySync();
                return;
            }

            var domainList = string.Join(",", requiredDomains.Select(d => d.ToString()));
            LunaLog.Log($"[PersistentSync] StartInitialSync requesting snapshots for domains=[{domainList}]");
            MessageSender.SendRequest(requiredDomains);
        }

        /// <summary>
        /// Pulls <see cref="PersistentSyncDomainNames.GameLaunchId"/> in a second request after mandatory domains so
        /// servers without that domain still complete the initial snapshot round-trip.
        /// </summary>
        internal void RequestOptionalGameLaunchIdSnapshotAfterMandatorySync()
        {
            if (_optionalGameLaunchIdPullSent)
            {
                return;
            }

            if (!IsLiveForDomain(PersistentSyncDomainNames.GameLaunchId))
            {
                return;
            }

            _optionalGameLaunchIdPullSent = true;
            LunaLog.Log("[PersistentSync] optional post-handshake snapshot request domain=GameLaunchId");
            MessageSender.SendRequest(PersistentSyncDomainNames.GameLaunchId);
        }

        /// <summary>
        /// Re-sends snapshot requests without clearing reconciler state. Used when the join watchdog
        /// detects prolonged idle time (should be rare while <see cref="NetworkSystem.BumpPersistentSyncJoinActivity"/> runs).
        /// </summary>
        public void ResendInitialSnapshotRequest()
        {
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            var requiredDomains = PersistentSyncDomainApplicability
                .GetRequiredDomainsForInitialSync(SettingsSystem.ServerSettings.GameMode, caps)
                .ToArray();

            if (!requiredDomains.Any())
            {
                return;
            }

            var domainList = string.Join(",", requiredDomains.Select(d => d.ToString()));
            LunaLog.Log($"[PersistentSync] ResendInitialSnapshotRequest domains=[{domainList}]");
            MessageSender.SendRequest(requiredDomains);
        }

        /// <summary>
        /// Whether the client may leave <see cref="ClientState.PersistentStateSynced"/> for lock sync.
        /// Uses the same completion rule as leaving <see cref="ClientState.SyncingPersistentState"/>:
        /// join-time snapshot acceptance (including deferred-until-ingame payloads), not live KSP writes.
        /// Live apply continues via <see cref="FlushPendingState"/> and scene hooks.
        /// </summary>
        public bool IsPersistentSnapshotPhaseCompleteForCurrentSession()
        {
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            var required = PersistentSyncDomainApplicability
                .GetRequiredDomainsForInitialSync(SettingsSystem.ServerSettings.GameMode, caps)
                .ToArray();
            return !required.Any() || Reconciler.State.AreAllJoinHandshakesComplete();
        }

        public long GetKnownRevision(string domainId)
        {
            return Reconciler.GetKnownRevision(domainId);
        }

        /// <summary>Delegates to <see cref="Singleton"/> message sender; no-op if the system is not constructed.</summary>
        public static void SendIntent<TDomain, TPayload>(TPayload payload, string reason)
            where TDomain : SyncClientDomain<TPayload>
        {
            var s = Singleton;
            if (s == null)
            {
                return;
            }

            s.MessageSender.SendIntent<TDomain, TPayload>(payload, reason);
        }

        /// <summary>Delegates to <see cref="Singleton"/> message sender.</summary>
        public static void SendIntent(string domainId, long clientKnownRevision, byte[] payload, string reason)
        {
            Singleton?.MessageSender.SendIntent(domainId, clientKnownRevision, payload, reason);
        }

        /// <summary>Delegates to <see cref="Singleton"/> message sender.</summary>
        public static void SendIntent<TPayload>(string domainId, long clientKnownRevision, TPayload payload, string reason)
        {
            Singleton?.MessageSender.SendIntent(domainId, clientKnownRevision, payload, reason);
        }

        /// <summary>
        /// Retries buffered server snapshots first, then asks domains to commit any scalar values
        /// that were deferred until live game objects existed (<see cref="PersistentSyncApplyOutcome"/>).
        /// </summary>
        private void FlushPendingState()
        {
            NetworkSystem.BumpPersistentSyncJoinActivity();
            Reconciler.RetryDeferredSnapshots();
            Reconciler.FlushPendingState();
            FlushDomainQueues();
        }

        /// <summary>
        /// Runs the same work as the timed flush routine immediately. Call right after
        /// <c>HighLogic.CurrentGame.Start()</c> on join: deferred domains (notably <see cref="ContractsPersistentSyncClientDomain"/>
        /// when <see cref="ContractSystem.Instance"/> was null during snapshot handling) otherwise wait for the
        /// 1000ms routine, leaving an empty <see cref="ContractSystem"/> while stock Mission Control / ContractsApp
        /// already bind to that empty model.
        /// </summary>
        public void FlushLivePendingPersistentSyncState(string reason)
        {
            if (!Enabled)
            {
                return;
            }

            LunaLog.Log($"[PersistentSync] FlushLivePendingPersistentSyncState source={reason}");
            FlushPendingState();
        }

        private void OnSceneReady(GameScenes data)
        {
            NetworkSystem.BumpPersistentSyncJoinActivity();

            if (ResearchAndDevelopment.Instance != null && AssetBase.RnDTechTree != null)
            {
                int available = 0, unavailable = 0, totalStates = 0;
                foreach (var tech in AssetBase.RnDTechTree.GetTreeTechs().Where(t => t != null))
                {
                    var state = ResearchAndDevelopment.Instance.GetTechState(tech.techID);
                    if (state == null) continue;
                    totalStates++;
                    if (state.state == RDTech.State.Available) available++;
                    else unavailable++;
                }
                LunaLog.Log($"[PersistentSync] OnSceneReady scene={data} rdTechStates total={totalStates} available={available} unavailable={unavailable}");
            }
            else
            {
                LunaLog.Log($"[PersistentSync] OnSceneReady scene={data} rdInstance={(ResearchAndDevelopment.Instance != null)} rdTree={(AssetBase.RnDTechTree != null)}");
            }

            Reconciler.RetryDeferredSnapshots();
            Reconciler.FlushPendingState();

            if (data != GameScenes.SPACECENTER || MainSystem.NetworkState < ClientState.PersistentStateSynced)
            {
                return;
            }

            // ScenarioUpgradeableFacilities can initialize KSC defaults after our first PersistentSync flush;
            // re-apply the last server snapshot once GUI is ready so upgraded levels stick.
            if (Domains[PersistentSyncDomainNames.UpgradeableFacilities] is UpgradeableFacilitiesPersistentSyncClientDomain facilitiesDomain &&
                facilitiesDomain.TryStageReassertFromLastServerSnapshot())
            {
                LunaLog.Log("[PersistentSync] KSC GUI ready re-staging facility snapshot for reconciler flush");
                Reconciler.FlushPendingState();
            }

            // ResearchAndDevelopment can be rebuilt from stale ProtoScenarioModule config after our first
            // apply; reassert the authoritative tech + purchases so opening R&D sees the server-correct tree.
            ReassertTechnologyAndPurchases("KSCGuiReady");

            // If we still never live-marked applied, ask the server to re-send (ClearDeferred in RequestResync).
            if (Reconciler.State.IsInitialJoinHandshakeComplete(PersistentSyncDomainNames.UpgradeableFacilities) &&
                !Reconciler.State.HasInitialSnapshot(PersistentSyncDomainNames.UpgradeableFacilities))
            {
                Reconciler.RequestResync(PersistentSyncDomainNames.UpgradeableFacilities, "KscGuiReadyFacilityReapply");
            }
        }

        /// <summary>
        /// KSP's R&D complex GUI reads tech state at spawn time; if the stock module reinitialized
        /// R&D.Instance behind us, the panel would show a fresh tree even though our apply succeeded
        /// earlier. Reassert the last server snapshots here so the panel reflects server truth.
        /// </summary>
        private void OnRnDComplexSpawn()
        {
            if (MainSystem.NetworkState < ClientState.PersistentStateSynced)
            {
                return;
            }

            if (ResearchAndDevelopment.Instance != null && AssetBase.RnDTechTree != null)
            {
                int available = 0, unavailable = 0, totalStates = 0;
                foreach (var tech in AssetBase.RnDTechTree.GetTreeTechs().Where(t => t != null))
                {
                    var state = ResearchAndDevelopment.Instance.GetTechState(tech.techID);
                    if (state == null) continue;
                    totalStates++;
                    if (state.state == RDTech.State.Available) available++;
                    else unavailable++;
                }
                LunaLog.Log($"[PersistentSync] OnRnDComplexSpawn rdTechStates total={totalStates} available={available} unavailable={unavailable}");
            }

            // Defer one frame: stock R&D UI allocates placeholder RDNodes first; an immediate flush + tree
            // refresh here races that build (empty cog / "No text" nodes) before the real tree appears.
            CoroutineUtil.StartFrameDelayedRoutine(
                "PersistentSync.RndReassertNextFrame",
                () => ReassertTechnologyAndPurchases("RnDComplexSpawn+nextFrame"),
                1);
        }

        private void ReassertTechnologyAndPurchases(string source)
        {
            var flushNeeded = false;

            if (Domains.TryGetValue(PersistentSyncDomainNames.Technology, out var techDomainObj) &&
                techDomainObj is TechnologyPersistentSyncClientDomain techDomain &&
                techDomain.TryStageReassertFromLastServerSnapshot())
            {
                LunaLog.Log($"[PersistentSync] {source} re-staging Technology snapshot for reconciler flush");
                flushNeeded = true;
            }

            if (Domains.TryGetValue(PersistentSyncDomainNames.PartPurchases, out var partsDomainObj) &&
                partsDomainObj is PartPurchasesPersistentSyncClientDomain partsDomain &&
                partsDomain.TryStageReassertFromLastServerSnapshot())
            {
                LunaLog.Log($"[PersistentSync] {source} re-staging PartPurchases snapshot for reconciler flush");
                flushNeeded = true;
            }

            if (flushNeeded)
            {
                Reconciler.FlushPendingState();
            }
        }

        private void EnableDomainLifecycles()
        {
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            foreach (var domain in Domains.Values)
            {
                if (PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                        domain.DomainId,
                        SettingsSystem.ServerSettings.GameMode,
                        in caps))
                {
                    domain.EnableDomainLifecycle();
                }
            }
        }

        private void DisableDomainLifecycles()
        {
            foreach (var domain in Domains.Values)
            {
                domain.DisableDomainLifecycle();
            }
        }

        private void FlushDomainQueues()
        {
            foreach (var domain in Domains.Values)
            {
                domain.FlushQueuedDomainActions();
            }
        }

        public static Dictionary<string, IPersistentSyncClientDomain> CreateRegisteredDomainsForTests(Assembly assembly)
        {
            var dict = CreateRegisteredDomains(assembly, out var defs);
            if (!PersistentSyncCatalogMerger.TryMergeLocalOnlyForTests(defs, out var merged, out var err))
            {
                throw new InvalidOperationException(err);
            }

            PersistentSyncDomainCatalog.Configure(merged);
            return dict;
        }

        private static Dictionary<string, IPersistentSyncClientDomain> CreateRegisteredDomains(Assembly assembly, out PersistentSyncDomainDefinition[] definitions)
        {
            var registrar = new PersistentSyncClientDomainRegistrar();
            var domainTypes = assembly
                .GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IPersistentSyncClientDomain).IsAssignableFrom(t))
                .ToList();

            foreach (var domainType in domainTypes)
            {
                InvokeDomainRegistration(domainType, registrar);
            }

            var definitionsList = registrar.BuildDefinitions();
            definitions = definitionsList.ToArray();

            var registeredTypes = new HashSet<Type>(definitionsList.Select(d => d.DomainType));
            var unregisteredTypes = domainTypes
                .Where(t => !registeredTypes.Contains(t))
                .Select(t => t.FullName)
                .OrderBy(n => n)
                .ToArray();
            if (unregisteredTypes.Length > 0)
            {
                throw new InvalidOperationException("Persistent sync client domains missing self-registration: " + string.Join(", ", unregisteredTypes));
            }

            var dict = definitionsList
                .Select(d => CreateClientDomain(d.DomainType))
                .ToDictionary(d => d.DomainId);

            PersistentSyncEventSuppressorRegistry.ReplaceAllFromClientDomains(dict);

            return dict;
        }

        private static void InvokeDomainRegistration(Type domainType, PersistentSyncClientDomainRegistrar registrar)
        {
            var method = domainType.GetMethod(
                "RegisterPersistentSyncDomain",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(PersistentSyncClientDomainRegistrar) },
                null);

            if (method == null)
            {
                return;
            }

            registrar.WithCurrentDomainType(domainType, () => method.Invoke(null, new object[] { registrar }));
        }

        private static IPersistentSyncClientDomain CreateClientDomain(Type type)
        {
            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new InvalidOperationException($"Persistent sync client domain {type.FullName} must have a public parameterless constructor.");
            }

            return (IPersistentSyncClientDomain)Activator.CreateInstance(type);
        }
    }
}
