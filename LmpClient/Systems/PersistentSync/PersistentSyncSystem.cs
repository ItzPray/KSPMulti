using LmpClient;
using LmpClient.Base;
using LmpClient.Events;
using LmpClient.Systems.Network;
using LmpClient.Systems.SettingsSys;
using LmpClient.Utilities;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using System.Collections.Generic;
using System.Linq;

namespace LmpClient.Systems.PersistentSync
{
    public class PersistentSyncSystem : MessageSystem<PersistentSyncSystem, PersistentSyncMessageSender, PersistentSyncMessageHandler>
    {
        public override string SystemName { get; } = nameof(PersistentSyncSystem);

        public override int ExecutionOrder => -50;

        protected override ClientState EnableStage => ClientState.ScenariosSynced;

        public PersistentSyncReconciler Reconciler { get; } = new PersistentSyncReconciler();

        public Dictionary<PersistentSyncDomainId, IPersistentSyncClientDomain> Domains { get; } =
            new Dictionary<PersistentSyncDomainId, IPersistentSyncClientDomain>
            {
                [PersistentSyncDomainId.Funds] = new FundsPersistentSyncClientDomain(),
                [PersistentSyncDomainId.Science] = new SciencePersistentSyncClientDomain(),
                [PersistentSyncDomainId.Reputation] = new ReputationPersistentSyncClientDomain(),
                [PersistentSyncDomainId.Strategy] = new StrategyPersistentSyncClientDomain(),
                [PersistentSyncDomainId.Achievements] = new AchievementsPersistentSyncClientDomain(),
                [PersistentSyncDomainId.ScienceSubjects] = new ScienceSubjectsPersistentSyncClientDomain(),
                [PersistentSyncDomainId.Technology] = new TechnologyPersistentSyncClientDomain(),
                [PersistentSyncDomainId.ExperimentalParts] = new ExperimentalPartsPersistentSyncClientDomain(),
                [PersistentSyncDomainId.PartPurchases] = new PartPurchasesPersistentSyncClientDomain(),
                [PersistentSyncDomainId.UpgradeableFacilities] = new UpgradeableFacilitiesPersistentSyncClientDomain(),
                [PersistentSyncDomainId.Contracts] = new ContractsPersistentSyncClientDomain()
            };

        protected override void OnEnabled()
        {
            base.OnEnabled();
            SetupRoutine(new RoutineDefinition(1000, RoutineExecution.Update, FlushPendingState));
            GameEvents.onLevelWasLoadedGUIReady.Add(OnSceneReady);
            GameEvents.onGUIRnDComplexSpawn.Add(OnRnDComplexSpawn);
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
        public static bool IsLiveForDomain(PersistentSyncDomainId domainId)
        {
            var singleton = Singleton;
            return singleton != null
                   && singleton.Enabled
                   && singleton.Domains != null
                   && singleton.Domains.ContainsKey(domainId);
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnSceneReady);
            GameEvents.onGUIRnDComplexSpawn.Remove(OnRnDComplexSpawn);
            Reconciler.Reset(new PersistentSyncDomainId[0]);
        }

        public void StartInitialSync()
        {
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            var requiredDomains = PersistentSyncDomainApplicability
                .GetRequiredDomainsForInitialSync(SettingsSystem.ServerSettings.GameMode, caps)
                .ToArray();
            Reconciler.Reset(requiredDomains);

            if (!requiredDomains.Any())
            {
                LunaLog.Log("[PersistentSync] StartInitialSync no required domains for current game mode; advancing to PersistentStateSynced");
                MainSystem.NetworkState = ClientState.PersistentStateSynced;
                return;
            }

            var domainList = string.Join(",", requiredDomains.Select(d => d.ToString()));
            LunaLog.Log($"[PersistentSync] StartInitialSync requesting snapshots for domains=[{domainList}]");
            MessageSender.SendRequest(requiredDomains);
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

        public long GetKnownRevision(PersistentSyncDomainId domainId)
        {
            return Reconciler.GetKnownRevision(domainId);
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

        /// <summary>
        /// Called by <see cref="MainSystem.StartGameNow"/> after <c>LoadMissingScenarioDataIntoGame</c> and before
        /// <c>HighLogic.CurrentGame.Start()</c>. Pre-populates the Contracts <see cref="ProtoScenarioModule"/>
        /// <c>moduleValues</c> from the currently-buffered server snapshot so stock's <c>OnLoadRoutine</c> loads the
        /// authoritative contract list itself — no race with stock's early <c>contracts.Clear()</c> in that coroutine,
        /// and no brief empty Mission Control between <c>Start()</c> and the deferred retry.
        /// </summary>
        public bool TryPrePopulateContractsProtoBeforeGameStart(string reason)
        {
            if (!Enabled)
            {
                return false;
            }

            if (!Domains.TryGetValue(PersistentSyncDomainId.Contracts, out var domainObj) ||
                !(domainObj is ContractsPersistentSyncClientDomain contractsDomain))
            {
                return false;
            }

            return contractsDomain.TryPrePopulateProtoFromPendingSnapshot(reason);
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
            if (Domains[PersistentSyncDomainId.UpgradeableFacilities] is UpgradeableFacilitiesPersistentSyncClientDomain facilitiesDomain &&
                facilitiesDomain.TryStageReassertFromLastServerSnapshot())
            {
                LunaLog.Log("[PersistentSync] KSC GUI ready re-staging facility snapshot for reconciler flush");
                Reconciler.FlushPendingState();
            }

            // ResearchAndDevelopment can be rebuilt from stale ProtoScenarioModule config after our first
            // apply; reassert the authoritative tech + purchases so opening R&D sees the server-correct tree.
            ReassertTechnologyAndPurchases("KSCGuiReady");

            // If we still never live-marked applied, ask the server to re-send (ClearDeferred in RequestResync).
            if (Reconciler.State.IsInitialJoinHandshakeComplete(PersistentSyncDomainId.UpgradeableFacilities) &&
                !Reconciler.State.HasInitialSnapshot(PersistentSyncDomainId.UpgradeableFacilities))
            {
                Reconciler.RequestResync(PersistentSyncDomainId.UpgradeableFacilities, "KscGuiReadyFacilityReapply");
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

            if (Domains.TryGetValue(PersistentSyncDomainId.Technology, out var techDomainObj) &&
                techDomainObj is TechnologyPersistentSyncClientDomain techDomain &&
                techDomain.TryStageReassertFromLastServerSnapshot())
            {
                LunaLog.Log($"[PersistentSync] {source} re-staging Technology snapshot for reconciler flush");
                flushNeeded = true;
            }

            if (Domains.TryGetValue(PersistentSyncDomainId.PartPurchases, out var partsDomainObj) &&
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
    }
}
