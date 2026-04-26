using Contracts;
using KSP.UI.Screens;
using LmpClient;
using LmpClient.Base;
using LmpClient.Events;
using LmpClient.Harmony;
using LmpClient.Systems.Lock;
using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.ShareProgress;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.VesselProtoSys;
using LmpClient.Systems.Warp;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LmpClient.Systems.ShareContracts
{
    public class ShareContractsSystem : ShareProgressBaseSystem<ShareContractsSystem, ShareContractsMessageSender, ShareContractsMessageHandler>
    {
        public override string SystemName { get; } = nameof(ShareContractsSystem);

        private ShareContractsEvents ShareContractsEvents { get; } = new ShareContractsEvents();

        /// <summary>
        /// Contract offers received while time is not at ~1x; flushed when warp ends so stock generation bursts
        /// do not each become a unique server-side contract (duplicate titles in Mission Control).
        /// </summary>
        private readonly Dictionary<Guid, Contract> _deferredContractOffers = new Dictionary<Guid, Contract>();

        private readonly HashSet<Guid> _knownLocalVesselIdsBeforeContractAccept = new HashSet<Guid>();

        public int DefaultContractGenerateIterations;

        /// <summary>
        /// Large <see cref="Planetarium.SetUniversalTime"/> jumps (subspace sync) make stock ContractSystem run its
        /// progression offer pass many times in one frame when <see cref="ContractSystem.generateContractIterations"/> is
        /// non-zero for the contract lock holder. Treat the whole transient sync window as "stock refresh suspended",
        /// then force a single safe replenish pass after the clock settles.
        /// </summary>
        private const int StockFallbackContractGenerateIterations = 50;

        private Coroutine _resumeStockContractPolicyCoroutine;

        private Coroutine _clearAwaitingAuthoritativeSnapshotCoroutine;

        private Coroutine _pendingControlledStockRefreshCoroutine;

        /// <summary>
        /// Coroutine that re-attempts <see cref="ReplenishStockOffersAfterPersistentSnapshotApply"/> once both
        /// the stock <c>ContractSystem.OnLoadRoutine</c> completion window and any pending PersistentSync
        /// snapshot apply have cleared. Reused so multiple rapid Replenish calls (LevelLoaded, lock acquire,
        /// snapshot apply) during the same transition coalesce into a single deferred run instead of
        /// stacking. See <see cref="TryDeferReplenishStockOffersUntilSafe"/>.
        /// </summary>
        private Coroutine _pendingReplenishAfterOnLoadCoroutine;

        /// <summary>
        /// Defers the PersistentSync Accept intent until <see cref="ShareContractsMessageSender.SerializationShowsActiveFutureDeadline"/>
        /// passes so we never publish a post-Accept snapshot with <c>dateDeadline=0</c> (server fallback would echo
        /// it and stock expires the mission immediately).
        /// </summary>
        private Coroutine _acceptContractWireCoroutine;

        private Coroutine _contractSpawnedVesselSyncCoroutine;

        /// <summary>
        /// Most recent <c>source</c> that requested a deferred Replenish pass. The deferred coroutine
        /// forwards this string when it finally runs so the log trail shows the original caller (e.g.
        /// LevelLoaded:PostTransientPreUi) rather than the coroutine itself.
        /// </summary>
        private string _pendingReplenishAfterOnLoadSource;

        /// <summary>
        /// Upper bound on how long the Replenish retry coroutine polls for the gate to clear. Far above
        /// the expected <c>OnLoadCoroutineCompletionWindowFrames</c> delay (a few frames) and above the
        /// FlushPendingState deferral loop, so the normal path always resolves inside the window. If the
        /// gate still hasn't cleared after this timeout, something is keeping the domain in "pending
        /// snapshot" state indefinitely and we abandon the deferred pass with a loud log rather than
        /// spinning forever.
        /// </summary>
        private const float PendingReplenishMaxWaitSeconds = 30f;

        /// <summary>
        /// Poll interval for <see cref="PendingReplenishAfterOnLoadWindowCoroutine"/>. Short enough to
        /// re-check on the same frame-group as the coroutine completion window (4 frames at 60 Hz
        /// ≈ 67ms), which means the deferred Replenish typically fires the frame right after
        /// <c>OnLoadRoutine</c> finishes.
        /// </summary>
        private const float PendingReplenishPollSeconds = 0.05f;

        private bool _suppressStockContractRefreshForTimeJump;

        private bool _pendingStockContractRefreshAfterTransientState;

        private bool _allowStockContractRefreshWindow;

        private bool _awaitingAuthoritativeSnapshotAfterControlledRefresh;

        private bool _pendingStableFullContractInventoryPublish;

        private string _pendingStableFullContractInventoryPublishReason;

        private int _lastAppliedGenerateContractIterations = int.MinValue;

        private string _lastControlledStockRefreshBlockReason;

        private static string _mcDiagContractSystemRefreshThrottleSignature;

        private static float _mcDiagContractSystemRefreshThrottleRealtime = -999f;

        private const float McDiagContractSystemRefreshThrottleSeconds = 5f;

        private static string _persistentSyncUiAdapterLogSignature;

        private static float _persistentSyncUiAdapterLogRealtime = -999f;

        private const float PersistentSyncUiAdapterLogThrottleSeconds = 2.5f;

        private static string _mcDiagMissionControlNullSkipSignature;

        private static float _mcDiagMissionControlNullSkipRealtime = -999f;

        private const float McDiagMissionControlNullSkipThrottleSeconds = 5f;

        private static readonly Regex GuidPattern = new Regex(
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|(?<![0-9a-fA-F])[0-9a-fA-F]{32}(?![0-9a-fA-F])",
            RegexOptions.Compiled);

        /// <summary>
        /// When stock calls <see cref="ContractSystem.RefreshContracts"/> every frame, the PersistentSync bypass path
        /// would rebuild Mission Control / Contracts App lists each time and clear selection. Coalesce identical
        /// contract inventory snapshots; call <see cref="InvalidatePersistentSyncBypassUiRefreshCoalesce"/> when
        /// progress can change without list membership/state changes (e.g. parameter completion).
        /// </summary>
        private string _persistentSyncBypassUiRefreshCoalesceSignature;

        /// <summary>
        /// Last canonical snapshot signature for which a non-producer client issued <see cref="ContractIntentPayloadKind.RequestOfferGeneration"/>.
        /// Prevents repeated requests per applied snapshot revision while offer pool remains empty.
        /// </summary>
        private string _lastNonProducerOfferGenerationRequestSignature;

        /// <summary>
        /// UI refresh sources that must not call stock <see cref="ContractSystem.RefreshContracts"/> / contract
        /// reload GameEvents once PersistentSync has populated <see cref="ContractSystem"/> from the server snapshot.
        /// Stock refresh rebuilds from local ProgressTracking and can clear synced rows (empty Mission Control).
        /// </summary>
        private const string PersistentSyncPostTransientStockRefreshSource = "PersistentSyncPostTransientStockRefresh";

        /// <summary>
        /// <see cref="Harmony.ContractSystem_RefreshContractsPersistentSyncGuard"/> uses this source so
        /// <see cref="RefreshContractLists"/> does not call stock <see cref="ContractSystem.RefreshContracts"/> again.
        /// </summary>
        public const string ContractSystemRefreshPersistentSyncBypassSource = "ContractSystemRefreshPersistentSyncBypass";

        private const float ControlledStockRefreshSettleSeconds = 1.5f;

        private const float ControlledStockRefreshPollSeconds = 0.25f;

        //This queue system is not used because we use one big queue in ShareCareerSystem for this system.
        protected override bool ShareSystemReady => true;

        protected override GameMode RelevantGameModes => GameMode.Career;

        protected override bool UseSessionApplicabilityInsteadOfGameModeMask => true;

        protected override bool IsShareSystemApplicableForSession()
        {
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            return PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainId.Contracts,
                SettingsSystem.ServerSettings.GameMode,
                in caps);
        }

        protected override void OnEnabled()
        {
            base.OnEnabled();

            CaptureDefaultContractGenerateIterationsIfNeeded();
            ApplyStockContractMutationPolicy(nameof(OnEnabled));

            LockEvent.onLockAcquire.Add(ShareContractsEvents.LockAcquire);
            LockEvent.onLockRelease.Add(ShareContractsEvents.LockReleased);
            GameEvents.onLevelWasLoadedGUIReady.Add(ShareContractsEvents.LevelLoaded);

            GameEvents.Contract.onAccepted.Add(ShareContractsEvents.ContractAccepted);
            GameEvents.Contract.onCancelled.Add(ShareContractsEvents.ContractCancelled);
            GameEvents.Contract.onCompleted.Add(ShareContractsEvents.ContractCompleted);
            GameEvents.Contract.onContractsListChanged.Add(ShareContractsEvents.ContractsListChanged);
            GameEvents.Contract.onContractsLoaded.Add(ShareContractsEvents.ContractsLoaded);
            GameEvents.Contract.onDeclined.Add(ShareContractsEvents.ContractDeclined);
            GameEvents.Contract.onFailed.Add(ShareContractsEvents.ContractFailed);
            GameEvents.Contract.onFinished.Add(ShareContractsEvents.ContractFinished);
            GameEvents.Contract.onOffered.Add(ShareContractsEvents.ContractOffered);
            GameEvents.Contract.onParameterChange.Add(ShareContractsEvents.ContractParameterChanged);
            GameEvents.Contract.onRead.Add(ShareContractsEvents.ContractRead);
            GameEvents.Contract.onSeen.Add(ShareContractsEvents.ContractSeen);

            RefreshContractSpawnedVesselBaseline(nameof(OnEnabled));

            if (IsShareSystemApplicableForSession())
            {
                SetupRoutine(new RoutineDefinition(400, RoutineExecution.Update, FlushDeferredContractOffersIfReady));
                SetupRoutine(new RoutineDefinition(250, RoutineExecution.Update, RefreshStockContractMutationPolicy));
            }
        }

        protected override void OnDisabled()
        {
            _deferredContractOffers.Clear();
            _pendingStockContractRefreshAfterTransientState = false;
            _suppressStockContractRefreshForTimeJump = false;
            _allowStockContractRefreshWindow = false;
            _awaitingAuthoritativeSnapshotAfterControlledRefresh = false;
            _pendingStableFullContractInventoryPublish = false;
            _pendingStableFullContractInventoryPublishReason = null;
            _lastAppliedGenerateContractIterations = int.MinValue;
            _persistentSyncBypassUiRefreshCoalesceSignature = null;
            _knownLocalVesselIdsBeforeContractAccept.Clear();
            MessageSender.ClearKnownContractSnapshots();

            if (_resumeStockContractPolicyCoroutine != null && MainSystem.Singleton != null)
            {
                MainSystem.Singleton.StopCoroutine(_resumeStockContractPolicyCoroutine);
                _resumeStockContractPolicyCoroutine = null;
            }

            if (_clearAwaitingAuthoritativeSnapshotCoroutine != null && MainSystem.Singleton != null)
            {
                MainSystem.Singleton.StopCoroutine(_clearAwaitingAuthoritativeSnapshotCoroutine);
                _clearAwaitingAuthoritativeSnapshotCoroutine = null;
            }

            if (_pendingControlledStockRefreshCoroutine != null && MainSystem.Singleton != null)
            {
                MainSystem.Singleton.StopCoroutine(_pendingControlledStockRefreshCoroutine);
                _pendingControlledStockRefreshCoroutine = null;
            }

            if (_pendingReplenishAfterOnLoadCoroutine != null && MainSystem.Singleton != null)
            {
                MainSystem.Singleton.StopCoroutine(_pendingReplenishAfterOnLoadCoroutine);
                _pendingReplenishAfterOnLoadCoroutine = null;
            }
            _pendingReplenishAfterOnLoadSource = null;

            if (_acceptContractWireCoroutine != null && MainSystem.Singleton != null)
            {
                MainSystem.Singleton.StopCoroutine(_acceptContractWireCoroutine);
                _acceptContractWireCoroutine = null;
            }

            if (_contractSpawnedVesselSyncCoroutine != null && MainSystem.Singleton != null)
            {
                MainSystem.Singleton.StopCoroutine(_contractSpawnedVesselSyncCoroutine);
                _contractSpawnedVesselSyncCoroutine = null;
            }

            base.OnDisabled();

            ContractSystem.generateContractIterations = DefaultContractGenerateIterations;

            LockEvent.onLockAcquire.Remove(ShareContractsEvents.LockAcquire);
            LockEvent.onLockRelease.Remove(ShareContractsEvents.LockReleased);
            GameEvents.onLevelWasLoadedGUIReady.Remove(ShareContractsEvents.LevelLoaded);

            //Always try to remove the event, as when we disconnect from a server the server settings will get the default values
            GameEvents.Contract.onAccepted.Remove(ShareContractsEvents.ContractAccepted);
            GameEvents.Contract.onCancelled.Remove(ShareContractsEvents.ContractCancelled);
            GameEvents.Contract.onCompleted.Remove(ShareContractsEvents.ContractCompleted);
            GameEvents.Contract.onContractsListChanged.Remove(ShareContractsEvents.ContractsListChanged);
            GameEvents.Contract.onContractsLoaded.Remove(ShareContractsEvents.ContractsLoaded);
            GameEvents.Contract.onDeclined.Remove(ShareContractsEvents.ContractDeclined);
            GameEvents.Contract.onFailed.Remove(ShareContractsEvents.ContractFailed);
            GameEvents.Contract.onFinished.Remove(ShareContractsEvents.ContractFinished);
            GameEvents.Contract.onOffered.Remove(ShareContractsEvents.ContractOffered);
            GameEvents.Contract.onParameterChange.Remove(ShareContractsEvents.ContractParameterChanged);
            GameEvents.Contract.onRead.Remove(ShareContractsEvents.ContractRead);
            GameEvents.Contract.onSeen.Remove(ShareContractsEvents.ContractSeen);
        }

        /// <summary>
        /// Waits until <see cref="ShareContractsMessageSender.SerializationShowsActiveFutureDeadline"/> succeeds, then
        /// sends the PersistentSync Accept intent. Avoids echoing Active contracts whose serialized <c>dateDeadline</c>
        /// is still zero (repair / attach satellite templates are especially sensitive).
        /// </summary>
        public void EnqueueAcceptContractWireIntent(Contract contract)
        {
            if (contract == null || MainSystem.Singleton == null)
            {
                return;
            }

            if (_acceptContractWireCoroutine != null)
            {
                MainSystem.Singleton.StopCoroutine(_acceptContractWireCoroutine);
                _acceptContractWireCoroutine = null;
            }

            var guid = contract.ContractGuid;
            var reason = $"ContractCommand:Accept:{guid:N}";
            ScheduleContractSpawnedVesselProtoSync(guid, "ContractAccepted");
            _acceptContractWireCoroutine = MainSystem.Singleton.StartCoroutine(AcceptContractWireIntentCoroutine(guid, reason));
        }

        public void ScheduleContractSpawnedVesselProtoSync(Guid acceptedContractGuid, string reason)
        {
            if (acceptedContractGuid == Guid.Empty || MainSystem.Singleton == null)
            {
                return;
            }

            if (_contractSpawnedVesselSyncCoroutine != null)
            {
                MainSystem.Singleton.StopCoroutine(_contractSpawnedVesselSyncCoroutine);
                _contractSpawnedVesselSyncCoroutine = null;
            }

            _contractSpawnedVesselSyncCoroutine = MainSystem.Singleton.StartCoroutine(
                ContractSpawnedVesselProtoSyncCoroutine(acceptedContractGuid, reason));
        }

        public void RefreshContractSpawnedVesselBaseline(string reason)
        {
            _knownLocalVesselIdsBeforeContractAccept.Clear();

            var vesselProtoSystem = VesselProtoSystem.Singleton;
            if (vesselProtoSystem == null)
            {
                return;
            }

            foreach (var vesselId in vesselProtoSystem.GetLocalVesselIds())
            {
                _knownLocalVesselIdsBeforeContractAccept.Add(vesselId);
            }

            LunaLog.Log($"[PersistentSync] refreshed contract-spawned vessel baseline count={_knownLocalVesselIdsBeforeContractAccept.Count} reason={reason}");
        }

        private IEnumerator ContractSpawnedVesselProtoSyncCoroutine(Guid acceptedContractGuid, string reason)
        {
            const int maxAttempts = 40;
            var sentVesselIds = new HashSet<Guid>();

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    yield return null;
                }

                var referencedVesselIds = GetActiveContractReferencedVesselIds(acceptedContractGuid);
                var candidateVesselIds = VesselProtoSystem.Singleton.GetLocalVesselIds();
                candidateVesselIds.ExceptWith(_knownLocalVesselIdsBeforeContractAccept);
                candidateVesselIds.UnionWith(referencedVesselIds);

                foreach (var vesselId in candidateVesselIds)
                {
                    if (sentVesselIds.Contains(vesselId))
                    {
                        continue;
                    }

                    if (VesselProtoSystem.Singleton.SendLocalVesselProto(
                            vesselId,
                            $"contract-spawned-vessel:{reason}:{acceptedContractGuid:N}",
                            true))
                    {
                        sentVesselIds.Add(vesselId);
                    }
                }
            }

            if (sentVesselIds.Count > 0)
            {
                LunaLog.Log(
                    $"[PersistentSync] sent {sentVesselIds.Count} contract-spawned vessel proto(s) for accepted contract {acceptedContractGuid}");
            }
            else
            {
                LunaLog.Log(
                    $"[PersistentSync] no local contract-spawned vessel proto found for accepted contract {acceptedContractGuid}");
            }

            _contractSpawnedVesselSyncCoroutine = null;
            RefreshContractSpawnedVesselBaseline($"ContractSpawnedVesselProtoSync:{acceptedContractGuid:N}");
        }

        private static HashSet<Guid> GetActiveContractReferencedVesselIds(Guid acceptedContractGuid)
        {
            var vesselIds = new HashSet<Guid>();
            var contracts = ContractSystem.Instance?.Contracts;
            if (contracts == null)
            {
                return vesselIds;
            }

            foreach (var contract in contracts.Where(c => c != null && c.ContractState == Contract.State.Active))
            {
                if (contract.ContractGuid != acceptedContractGuid)
                {
                    continue;
                }

                var node = new ConfigNode("CONTRACT");
                try
                {
                    contract.Save(node);
                }
                catch (Exception e)
                {
                    LunaLog.LogError($"[PersistentSync] failed to inspect accepted contract {acceptedContractGuid} for spawned vessels: {e}");
                    continue;
                }

                CollectReferencedGuids(node, vesselIds, skipContractRootGuid: true);
            }

            return vesselIds;
        }

        private static void CollectReferencedGuids(ConfigNode node, ISet<Guid> vesselIds, bool skipContractRootGuid)
        {
            if (node == null)
            {
                return;
            }

            foreach (ConfigNode.Value value in node.values)
            {
                if (skipContractRootGuid && string.Equals(value.name, "guid", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                CollectGuidsFromValue(value.value, vesselIds);
            }

            foreach (ConfigNode childNode in node.nodes)
            {
                CollectReferencedGuids(childNode, vesselIds, false);
            }
        }

        private static void CollectGuidsFromValue(string value, ISet<Guid> vesselIds)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (Match match in GuidPattern.Matches(value))
            {
                if (Guid.TryParse(match.Value, out var guid) && guid != Guid.Empty)
                {
                    vesselIds.Add(guid);
                }
            }
        }

        private IEnumerator AcceptContractWireIntentCoroutine(Guid contractGuid, string reason)
        {
            const int maxAttempts = 24;

            ShareContractsSystem.LogMcUiContractInventory($"Contract.onAccepted:wireWait guid={contractGuid}");

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    yield return null;
                }

                var live = ContractSystem.Instance?.Contracts?.FirstOrDefault(c => c != null && c.ContractGuid == contractGuid);
                if (live == null)
                {
                    LunaLog.LogWarning($"[PersistentSync] Accept wire aborted: contract {contractGuid} not in main list.");
                    _acceptContractWireCoroutine = null;
                    yield break;
                }

                if (live.ContractState != Contract.State.Active)
                {
                    LunaLog.LogWarning(
                        $"[PersistentSync] Accept wire aborted: contract {contractGuid} is state={live.ContractState} (expected Active).");
                    _acceptContractWireCoroutine = null;
                    yield break;
                }

                if (ShareContractsMessageSender.SerializationShowsActiveFutureDeadline(live))
                {
                    MessageSender.SendContractCommand(ContractIntentPayloadKind.AcceptContract, live, reason);
                    LunaLog.Log($"Contract accept intent sent (after {attempt + 1} frame(s)): {contractGuid}");
                    _acceptContractWireCoroutine = null;
                    yield break;
                }
            }

            var fallback = ContractSystem.Instance?.Contracts?.FirstOrDefault(c => c != null && c.ContractGuid == contractGuid);
            if (fallback != null && fallback.ContractState == Contract.State.Active)
            {
                LunaLog.LogError(
                    $"[PersistentSync] Accept wire: deadline serialization still unhealthy after {maxAttempts} frame(s); " +
                    $"sending best-effort snapshot guid={contractGuid} title={fallback.Title}");
                MessageSender.SendContractCommand(ContractIntentPayloadKind.AcceptContract, fallback, reason);
            }
            else
            {
                LunaLog.LogError(
                    $"[PersistentSync] Accept wire exhausted without healthy deadline; no Accept command sent guid={contractGuid}.");
            }

            _acceptContractWireCoroutine = null;
        }

        /// <summary>
        /// Call immediately before <see cref="Planetarium.SetUniversalTime"/> when the jump is large (e.g. subspace
        /// catch-up). Suppresses stock contract offer generation bursts that duplicate PersistentSync contract state.
        /// </summary>
        public void GuardStockContractGenerationAroundLargeUniversalTimeJump(double targetTick)
        {
            try
            {
                if (!IsShareSystemApplicableForSession())
                {
                    LunaLog.Log($"[PersistentSync] contract refresh skip source=LargeUniversalTimeJumpStart reason=share-not-applicable targetTime={targetTick}");
                    return;
                }

                if (!PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainId.Contracts))
                {
                    LunaLog.Log($"[PersistentSync] contract refresh skip source=LargeUniversalTimeJumpStart reason=persistent-sync-disabled targetTime={targetTick}");
                    return;
                }

                if (ContractSystem.Instance == null || MainSystem.Singleton == null)
                {
                    LunaLog.Log($"[PersistentSync] contract refresh skip source=LargeUniversalTimeJumpStart reason=contract-or-main-missing targetTime={targetTick}");
                    return;
                }

                var now = Planetarium.GetUniversalTime();
                // Ignore tiny clock corrections; subspace / catch-up jumps are much larger.
                if (Math.Abs(targetTick - now) < 600d)
                {
                    return;
                }

                LunaLog.Log($"[PersistentSync] contract refresh request source=LargeUniversalTimeJumpStart targetTime={targetTick} now={now} delta={targetTick - now} state={DescribeControlledStockRefreshState()}");
                _suppressStockContractRefreshForTimeJump = true;
                // Do not queue ContractSystem.RefreshContracts here: after reconnect, the settle pass runs with
                // iterations=50 and wipes server-backed contracts; PersistentSync snapshots already own the list.
                ApplyStockContractMutationPolicy("LargeUniversalTimeJumpStart");

                if (_resumeStockContractPolicyCoroutine != null)
                {
                    MainSystem.Singleton.StopCoroutine(_resumeStockContractPolicyCoroutine);
                }

                _resumeStockContractPolicyCoroutine =
                    MainSystem.Singleton.StartCoroutine(ResumeStockContractPolicyAfterUniversalTimeJumpCoroutine());
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[PersistentSync] Guard contract generation around UT jump failed: {e}");
            }
        }

        private IEnumerator ResumeStockContractPolicyAfterUniversalTimeJumpCoroutine()
        {
            yield return null;
            yield return null;
            yield return null;
            try
            {
                if (ContractSystem.Instance != null)
                {
                    _suppressStockContractRefreshForTimeJump = false;
                    ApplyStockContractMutationPolicy("LargeUniversalTimeJumpEnd");
                }
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[PersistentSync] Resume stock contract policy after UT jump failed: {e}");
            }

            _resumeStockContractPolicyCoroutine = null;
        }

        /// <summary>
        /// When time is warped above 1x, defer syncing this offer until clock returns to ~1x (see <see cref="FlushDeferredContractOffersIfReady"/>).
        /// </summary>
        public bool TryDeferContractOfferIfTimeWarping(Contract contract)
        {
            if (contract == null || !IsShareSystemApplicableForSession())
            {
                return false;
            }

            if (IsGameClockApproximatelyOneX())
            {
                return false;
            }

            _deferredContractOffers[contract.ContractGuid] = contract;
            return true;
        }

        private void FlushDeferredContractOffersIfReady()
        {
            if (!IsShareSystemApplicableForSession() || _deferredContractOffers.Count == 0)
            {
                return;
            }

            if (!IsGameClockApproximatelyOneX())
            {
                return;
            }

            // Warp can queue many offers; keep one per normalized title so we do not spam the server.
            var byNormalizedTitle = new Dictionary<string, Contract>(StringComparer.OrdinalIgnoreCase);
            foreach (var contract in _deferredContractOffers.Values)
            {
                if (contract == null || !IsMissionControlOfferPoolContract(contract))
                {
                    continue;
                }

                var key = NormalizeOfferTitleForDedupe(contract.Title);
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                byNormalizedTitle[key] = contract;
            }

            foreach (var contract in byNormalizedTitle.Values)
            {
                MessageSender.SendProducerProposal(
                    ContractIntentPayloadKind.OfferObserved,
                    contract,
                    $"ContractProposal:DeferredOffer:{contract.ContractGuid:N}");
            }

            _deferredContractOffers.Clear();
        }

        /// <summary>
        /// Collapses whitespace so two stock titles that only differ in spacing still dedupe.
        /// </summary>
        public static string NormalizeOfferTitleForDedupe(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            return Regex.Replace(title.Trim(), @"\s+", " ");
        }

        /// <summary>
        /// Runtime-side contract identity used to suppress stock re-offers of the same visible mission while another
        /// canonical row (active or offered) already exists locally.
        /// </summary>
        public static string BuildRuntimeContractIdentityKey(Contract contract)
        {
            if (contract == null)
            {
                return string.Empty;
            }

            var type = contract.GetType().FullName ?? contract.GetType().Name;
            var title = NormalizeOfferTitleForDedupe(contract.Title);
            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(title))
            {
                return string.Empty;
            }

            return string.Concat(type, "\u001f", title);
        }

        /// <summary>
        /// Non-active, non-finished contracts in the main list are Mission Control offer pool entries (not only strict Offered).
        /// </summary>
        public static bool IsMissionControlOfferPoolContract(Contract c)
        {
            if (c == null || c.IsFinished())
            {
                return false;
            }

            switch (c.ContractState)
            {
                case Contract.State.Active:
                case Contract.State.Completed:
                case Contract.State.Cancelled:
                case Contract.State.Failed:
                case Contract.State.DeadlineExpired:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Mirrors WarpSystem's "warp stopped" heuristic: on-rails index 0 and rate ~1.
        /// </summary>
        private static bool IsGameClockApproximatelyOneX()
        {
            try
            {
                if (TimeWarp.fetch == null)
                {
                    return true;
                }

                if (TimeWarp.CurrentRateIndex > 0)
                {
                    return false;
                }

                return Math.Abs(TimeWarp.CurrentRate - 1f) < 0.11f;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Try to acquire the contract lock
        /// </summary>
        public void TryGetContractLock()
        {
            if (!LockSystem.LockQuery.ContractLockExists())
            {
                LockSystem.Singleton.AcquireContractLock();
            }
        }

        public void CaptureDefaultContractGenerateIterationsIfNeeded()
        {
            if (DefaultContractGenerateIterations > 0)
            {
                return;
            }

            DefaultContractGenerateIterations = ContractSystem.generateContractIterations > 0
                ? ContractSystem.generateContractIterations
                : StockFallbackContractGenerateIterations;
        }

        /// <summary>
        /// Used by <c>ContractSystem_GenerateMandatoryContracts</c> Harmony only. We intentionally do <b>not</b> patch
        /// <see cref="ContractSystem.RefreshContracts"/> — Mission Control tab switches call it to rebind UI lists from
        /// <see cref="ContractSystem.Instance"/>; blocking it left stale cached rows (wrong available list until Accept).
        /// </summary>
        public bool ShouldSuppressStockContractRefresh()
        {
            if (!IsShareSystemApplicableForSession() || ContractSystem.Instance == null)
            {
                return false;
            }

            if (!LockSystem.LockQuery.ContractLockBelongsToPlayer(SettingsSystem.CurrentSettings.PlayerName))
            {
                return true;
            }

            return !_allowStockContractRefreshWindow;
        }

        public void NoteSuppressedStockContractRefresh(string source)
        {
            RequestControlledStockContractRefresh(source);
        }

        /// <summary>
        /// True for the exact duration of a KSPMP-controlled <c>ContractSystem.RefreshContracts</c> call
        /// (opened by <see cref="ReplenishStockOffersAfterPersistentSnapshotApply"/> via
        /// <see cref="_allowStockContractRefreshWindow"/>). The PersistentSync Contracts domain wraps the
        /// entire snapshot apply in <see cref="StartIgnoringEvents"/>/<see cref="StopIgnoringEvents"/> to
        /// stop server-applied state from echoing back as fresh client intents; that scope also contains
        /// our controlled Replenish call, whose whole purpose is to mint NEW progression-unlocked offers
        /// on the client that the server has not seen yet. Those freshly generated contracts fire
        /// <c>Contract.onOffered</c> inside the IgnoreEvents scope but are NOT echoes of the snapshot —
        /// the handler needs a way to tell them apart from snapshot-restoration events so it can run the
        /// title-based dedup (kills duplicates of Finished/Active rows) and publish <c>OfferObserved</c>
        /// intents. Both <c>ReplaceContractsFromSnapshot</c> (state-restore path) and the proto mirror
        /// step assign state directly and do NOT fire <c>onOffered</c>, so this window is a precise
        /// signal that the current <c>onOffered</c> came from stock generating a brand-new contract.
        /// </summary>
        public bool IsInsideControlledStockContractRefresh => _allowStockContractRefreshWindow;

        public void RequestControlledStockContractRefresh(string source)
        {
            if (!IsShareSystemApplicableForSession() || ContractSystem.Instance == null)
            {
                LunaLog.Log($"[PersistentSync] contract refresh request ignored source={source} reason=share-not-applicable-or-contract-missing state={DescribeControlledStockRefreshState()}");
                return;
            }

            _pendingStockContractRefreshAfterTransientState = true;
            LunaLog.Log($"[PersistentSync] contract refresh request queued source={source} state={DescribeControlledStockRefreshState()}");

            if (_pendingControlledStockRefreshCoroutine == null && MainSystem.Singleton != null)
            {
                _pendingControlledStockRefreshCoroutine =
                    MainSystem.Singleton.StartCoroutine(WaitForSafeControlledStockRefreshCoroutine(source));
            }
        }

        /// <summary>
        /// Stops the deferred <see cref="ContractSystem.RefreshContracts"/> pass queued by
        /// <see cref="RequestControlledStockContractRefresh"/>. Call before applying an authoritative PersistentSync
        /// contract snapshot so stock does not re-seed tutorial/progression offers on top of server truth (wrong MC
        /// "available" list until the next Accept-driven refresh).
        /// </summary>
        public void CancelPendingControlledStockContractRefresh(string source)
        {
            if (_pendingStockContractRefreshAfterTransientState)
            {
                LunaLog.Log($"[PersistentSync] contract refresh pending cleared source={source} state={DescribeControlledStockRefreshState()}");
            }

            _pendingStockContractRefreshAfterTransientState = false;
            _lastControlledStockRefreshBlockReason = null;

            if (_pendingControlledStockRefreshCoroutine != null && MainSystem.Singleton != null)
            {
                MainSystem.Singleton.StopCoroutine(_pendingControlledStockRefreshCoroutine);
                _pendingControlledStockRefreshCoroutine = null;
                LunaLog.Log($"[PersistentSync] contract refresh wait coroutine stopped source={source}");
            }
        }

        public void NotifyAuthoritativeContractsSnapshotApplied(string source)
        {
            if (!_awaitingAuthoritativeSnapshotAfterControlledRefresh || MainSystem.Singleton == null)
            {
                return;
            }

            LunaLog.Log($"[PersistentSync] contract refresh authoritative snapshot applied source={source} state={DescribeControlledStockRefreshState()}");
            RestartAwaitingAuthoritativeSnapshotSettleTimer(source);
        }

        public void ApplyStockContractMutationPolicy(string source)
        {
            if (!IsShareSystemApplicableForSession() || ContractSystem.Instance == null)
            {
                return;
            }

            CaptureDefaultContractGenerateIterationsIfNeeded();

            var targetIterations = _allowStockContractRefreshWindow ? DefaultContractGenerateIterations : 0;
            if (ContractSystem.generateContractIterations != targetIterations)
            {
                ContractSystem.generateContractIterations = targetIterations;
            }

            if (_lastAppliedGenerateContractIterations != targetIterations)
            {
                _lastAppliedGenerateContractIterations = targetIterations;
                LunaLog.Log($"[PersistentSync] contract stock policy source={source} iterations={targetIterations} pendingRefresh={_pendingStockContractRefreshAfterTransientState} awaitingSnapshot={_awaitingAuthoritativeSnapshotAfterControlledRefresh}");
            }
        }

        private void RefreshStockContractMutationPolicy()
        {
            ApplyStockContractMutationPolicy("RuntimePolicyRefresh");

            if (_pendingStockContractRefreshAfterTransientState && CanRunControlledStockContractRefreshNow())
            {
                TryRunPostTransientStockRefresh("RuntimePolicyRefresh");
            }
        }

        private string GetControlledStockRefreshBlockReason()
        {
            if (!IsShareSystemApplicableForSession())
            {
                return "share-not-applicable";
            }

            if (ContractSystem.Instance == null)
            {
                return "contract-system-missing";
            }

            if (_allowStockContractRefreshWindow)
            {
                return "refresh-window-open";
            }

            if (_awaitingAuthoritativeSnapshotAfterControlledRefresh)
            {
                return "awaiting-authoritative-snapshot";
            }

            if (!LockSystem.LockQuery.ContractLockBelongsToPlayer(SettingsSystem.CurrentSettings.PlayerName))
            {
                var owner = LockSystem.LockQuery.ContractLockOwner();
                return $"contract-lock-owned-by:{(string.IsNullOrEmpty(owner) ? "none" : owner)}";
            }

            if (WarpSystem.Singleton == null)
            {
                return "warp-system-missing";
            }

            if (_suppressStockContractRefreshForTimeJump)
            {
                return "universal-time-jump-in-progress";
            }

            if (WarpSystem.Singleton.CurrentSubspace == -1)
            {
                return "current-subspace-warping";
            }

            if (WarpSystem.Singleton.WaitingSubspaceIdFromServer)
            {
                return "waiting-subspace-id-from-server";
            }

            if (!IsGameClockApproximatelyOneX())
            {
                return $"clock-not-1x(rateIndex={TimeWarp.CurrentRateIndex},rate={TimeWarp.CurrentRate:0.00})";
            }

            // CRITICAL: The same two transient windows that make the Replenish entry point unsafe also make
            // TryRunPostTransientStockRefresh's direct RefreshContracts() fallback unsafe. On reconnect, the
            // server-authoritative Contracts snapshot lands in the domain's pending buffer while OnLoad is
            // still clearing/repopulating ContractSystem.Instance.Contracts. If the wait coroutine picks
            // that exact window to consider "block reason empty", TryRunPostTransientStockRefresh takes its
            // ELSE branch (HasInitialSnapshot is still false because MarkApplied has not yet run), opens the
            // _allowStockContractRefreshWindow, and calls stock RefreshContracts against a momentarily-empty
            // local pool. Stock then mints fresh-GUID duplicates of every starter contract the snapshot was
            // about to deliver; the OfferObserved intents race ahead of the snapshot apply, the server keeps
            // the ones whose titles aren't in the Finished set, and the canonical state is permanently
            // polluted with duplicates of completed and already-active rows. IsReplenishGateBlocked encodes
            // the full "snapshot not yet applied / OnLoad coroutine still repopulating" invariant — reusing
            // it here keeps the two entry points gated by the same condition so the wait coroutine simply
            // polls past this window and fires once the snapshot has been applied.
            if (IsReplenishGateBlocked(out var replenishBlockReason))
            {
                return replenishBlockReason;
            }

            return string.Empty;
        }

        private string DescribeControlledStockRefreshState()
        {
            var warp = WarpSystem.Singleton;
            var owner = LockSystem.LockQuery.ContractLockOwner();
            return $"pending={_pendingStockContractRefreshAfterTransientState} awaiting={_awaitingAuthoritativeSnapshotAfterControlledRefresh} allowWindow={_allowStockContractRefreshWindow} suppressForJump={_suppressStockContractRefreshForTimeJump} contractLockOwner={(string.IsNullOrEmpty(owner) ? "none" : owner)} currentSubspace={(warp != null ? warp.CurrentSubspace.ToString() : "null")} waitingSubspace={(warp != null && warp.WaitingSubspaceIdFromServer)} timeWarpRateIndex={TimeWarp.CurrentRateIndex} timeWarpRate={TimeWarp.CurrentRate:0.00}";
        }

        private string GetStableFullContractInventoryPublishBlockReason()
        {
            if (!IsShareSystemApplicableForSession())
            {
                return "share-not-applicable";
            }

            if (ContractSystem.Instance == null)
            {
                return "contract-system-missing";
            }

            if (!LockSystem.LockQuery.ContractLockBelongsToPlayer(SettingsSystem.CurrentSettings.PlayerName))
            {
                var owner = LockSystem.LockQuery.ContractLockOwner();
                return $"contract-lock-owned-by:{(string.IsNullOrEmpty(owner) ? "none" : owner)}";
            }

            if (_allowStockContractRefreshWindow)
            {
                return "refresh-window-open";
            }

            if (_pendingStockContractRefreshAfterTransientState)
            {
                return "pending-controlled-refresh";
            }

            if (_awaitingAuthoritativeSnapshotAfterControlledRefresh)
            {
                return "awaiting-authoritative-snapshot";
            }

            if (IsStockContractMutationTransientState())
            {
                return "stock-mutation-transient-state";
            }

            return string.Empty;
        }

        public void QueueOrPublishStableFullContractSystemSnapshot(string source)
        {
            var blockReason = GetStableFullContractInventoryPublishBlockReason();
            if (!string.IsNullOrEmpty(blockReason))
            {
                _pendingStableFullContractInventoryPublish = true;
                _pendingStableFullContractInventoryPublishReason = source;
                LunaLog.Log(
                    $"[PersistentSync] deferred full contract inventory publish source={source} " +
                    $"reason={blockReason} state={DescribeControlledStockRefreshState()}");
                return;
            }

            _pendingStableFullContractInventoryPublish = false;
            _pendingStableFullContractInventoryPublishReason = null;
            MessageSender.SendFullContractSystemSnapshot(source);
        }

        private void PublishPendingStableFullContractInventorySnapshotIfReady(string source)
        {
            if (!_pendingStableFullContractInventoryPublish)
            {
                return;
            }

            var publishReason = _pendingStableFullContractInventoryPublishReason ?? source;
            QueueOrPublishStableFullContractSystemSnapshot(publishReason);
        }

        private bool IsStockContractMutationTransientState()
        {
            if (_suppressStockContractRefreshForTimeJump)
            {
                return true;
            }

            return WarpSystem.Singleton != null &&
                   (WarpSystem.Singleton.CurrentSubspace == -1 || WarpSystem.Singleton.WaitingSubspaceIdFromServer);
        }

        private bool CanRunControlledStockContractRefreshNow()
        {
            return string.IsNullOrEmpty(GetControlledStockRefreshBlockReason()) && !IsStockContractMutationTransientState();
        }

        private bool HasPersistentSyncAuthoritativeContractsSnapshot()
        {
            if (!IsShareSystemApplicableForSession())
            {
                return false;
            }

            var ps = PersistentSyncSystem.Singleton;
            if (ps == null || !ps.Enabled)
            {
                return false;
            }

            return ps.Reconciler.State.HasInitialSnapshot(PersistentSyncDomainId.Contracts);
        }

        private void TryRunPostTransientStockRefresh(string source)
        {
            _pendingStockContractRefreshAfterTransientState = false;

            if (HasPersistentSyncAuthoritativeContractsSnapshot())
            {
                // After MarkApplied(Contracts), stock RefreshContracts() walks local progression / generation again.
                // With a reduced iteration budget it can still end up with an empty main list (mainCount=0) while
                // ContractSystem already holds server contracts — Mission Control then stays empty. Rebind UI only.
                try
                {
                    // Snapshot may have been applied before we held the contract lock; replenish was skipped then.
                    // After transient settle we often hold the lock — mint offers once before ps-safe UI rebind.
                    ReplenishStockOffersAfterPersistentSnapshotApply(source + ":PostTransientPreUi");
                    _awaitingAuthoritativeSnapshotAfterControlledRefresh = true;
                    RestartAwaitingAuthoritativeSnapshotSettleTimer(source + ":SafeUiRefreshStarted");
                    RefreshContractUiAdapters(PersistentSyncPostTransientStockRefreshSource);
                }
                catch (Exception e)
                {
                    _pendingStockContractRefreshAfterTransientState = true;
                    LunaLog.LogError($"[PersistentSync] forced stock contract safe ui refresh failed source={source} error={e}");
                }

                return;
            }

            try
            {
                _allowStockContractRefreshWindow = true;
                _awaitingAuthoritativeSnapshotAfterControlledRefresh = true;
                RestartAwaitingAuthoritativeSnapshotSettleTimer(source + ":RefreshStarted");
                ApplyStockContractMutationPolicy(source + ":OpenWindow");
                InvokeOptionalMethod(ContractSystem.Instance, "RefreshContracts");
                LunaLog.Log($"[PersistentSync] forced stock contract refresh source={source} offered={ContractSystem.Instance.Contracts.Count} finished={ContractSystem.Instance.ContractsFinished.Count}");
            }
            catch (Exception e)
            {
                _pendingStockContractRefreshAfterTransientState = true;
                LunaLog.LogError($"[PersistentSync] forced stock contract refresh failed source={source} error={e}");
            }
            finally
            {
                _allowStockContractRefreshWindow = false;
                ApplyStockContractMutationPolicy(source + ":CloseWindow");
            }
        }

        private IEnumerator ClearAwaitingAuthoritativeSnapshotAfterSettleCoroutine(string source)
        {
            yield return new WaitForSecondsRealtime(ControlledStockRefreshSettleSeconds);
            _awaitingAuthoritativeSnapshotAfterControlledRefresh = false;
            LunaLog.Log($"[PersistentSync] contract refresh settle complete source={source} state={DescribeControlledStockRefreshState()}");
            _clearAwaitingAuthoritativeSnapshotCoroutine = null;
            ApplyStockContractMutationPolicy(source + ":SnapshotSettled");
            PublishPendingStableFullContractInventorySnapshotIfReady(source + ":SnapshotSettled");
        }

        private IEnumerator WaitForSafeControlledStockRefreshCoroutine(string source)
        {
            LunaLog.Log($"[PersistentSync] contract refresh wait start source={source} state={DescribeControlledStockRefreshState()}");
            while (Enabled && IsShareSystemApplicableForSession() && ContractSystem.Instance != null)
            {
                ApplyStockContractMutationPolicy(source + ":Wait");

                if (!_pendingStockContractRefreshAfterTransientState)
                {
                    LunaLog.Log($"[PersistentSync] contract refresh wait end source={source} reason=no-pending-refresh state={DescribeControlledStockRefreshState()}");
                    break;
                }

                var blockReason = GetControlledStockRefreshBlockReason();
                if (string.IsNullOrEmpty(blockReason) && !IsStockContractMutationTransientState())
                {
                    _lastControlledStockRefreshBlockReason = null;
                    TryRunPostTransientStockRefresh(source);
                    break;
                }

                if (!string.Equals(_lastControlledStockRefreshBlockReason, blockReason, StringComparison.Ordinal))
                {
                    _lastControlledStockRefreshBlockReason = blockReason;
                    LunaLog.Log($"[PersistentSync] contract refresh wait blocked source={source} reason={blockReason} state={DescribeControlledStockRefreshState()}");
                }

                yield return new WaitForSecondsRealtime(ControlledStockRefreshPollSeconds);
            }

            if (!Enabled || ContractSystem.Instance == null || !IsShareSystemApplicableForSession())
            {
                LunaLog.Log($"[PersistentSync] contract refresh wait abort source={source} enabled={Enabled} applicable={IsShareSystemApplicableForSession()} contractSystem={(ContractSystem.Instance != null)}");
            }

            _lastControlledStockRefreshBlockReason = null;
            _pendingControlledStockRefreshCoroutine = null;
        }

        private void RestartAwaitingAuthoritativeSnapshotSettleTimer(string source)
        {
            if (MainSystem.Singleton == null)
            {
                return;
            }

            if (_clearAwaitingAuthoritativeSnapshotCoroutine != null)
            {
                MainSystem.Singleton.StopCoroutine(_clearAwaitingAuthoritativeSnapshotCoroutine);
            }

            LunaLog.Log($"[PersistentSync] contract refresh settle timer source={source} delaySeconds={ControlledStockRefreshSettleSeconds} state={DescribeControlledStockRefreshState()}");
            _clearAwaitingAuthoritativeSnapshotCoroutine =
                MainSystem.Singleton.StartCoroutine(ClearAwaitingAuthoritativeSnapshotAfterSettleCoroutine(source));
        }

        /// <summary>
        /// After PersistentSync replaces <see cref="ContractSystem"/> from the server, the main list can contain only
        /// active contracts with no available offer-pool entries. Stock normally repopulates via
        /// <see cref="ContractSystem.RefreshContracts"/>, but mandatory generation is gated by
        /// <see cref="ContractSystem_GenerateMandatoryContracts"/> unless <see cref="_allowStockContractRefreshWindow"/>
        /// is set. Run one controlled refresh for the contract lock holder so progression can mint missing offers.
        /// </summary>
        public void ReplenishStockOffersAfterPersistentSnapshotApply(string source)
        {
            if (!IsShareSystemApplicableForSession() || ContractSystem.Instance == null)
            {
                return;
            }

            if (!LockSystem.LockQuery.ContractLockBelongsToPlayer(SettingsSystem.CurrentSettings.PlayerName))
            {
                return;
            }

            // Two independent windows in which the live ContractSystem.Instance.Contracts list is a
            // false-positive "offer pool empty" reading. In EITHER window, running stock RefreshContracts
            // right now would regenerate starter offers (including titles the server holds as Finished —
            // "Launch our first vessel!" is the canonical repro) from the client's cleared local progression.
            // ShareContracts would then publish those fresh-GUID offers as OfferObserved intents BEFORE the
            // authoritative state converges on the live ContractSystem. The intent storm it produces (several
            // offers per scene transition every time the user enters SpaceCenter after a mission) is the
            // trigger for the first-mission-after-fresh-deploy main-thread freeze: it overlaps with the heavy
            // scene-transition workload (mod initialization, Kopernicus/Parallax/Scatterer, ContractsFinished
            // reconciliation) and tips the main thread over.
            //
            // IMPORTANT: we must not just drop the call. The legitimate mid-game use of Replenish is to mint
            // new offer-pool contracts after progression advances (e.g. completing the starter missions
            // unlocks the next tier of available contracts). Stock's own offer generation is gated through
            // <see cref="ApplyStockContractMutationPolicy"/> / <see cref="_allowStockContractRefreshWindow"/>,
            // so Replenish is the only path that actually mints those new offers on the client. If we drop
            // it during scene transitions the user gets stuck with exactly their current offer pool and
            // progression never surfaces new work. Instead, defer the call onto a coroutine that re-runs it
            // once both gates have cleared.
            //
            // Window 1 — snapshot in buffer, not yet applied. The reconciler is holding a freshly-received
            // server snapshot in a "Deferred" state while stock's OnLoadRoutine coroutine completes; see
            // <see cref="ContractsPersistentSyncClientDomain.FlushPendingState"/>. During this window the
            // snapshot IS the authoritative state but has not yet been merged into the live ContractSystem.
            //
            // Window 2 — stock OnLoadRoutine still mid-clear/repopulate. OnAwake has wiped Contracts and
            // ContractsFinished; the coroutine yields one frame and then runs to completion, rebuilding both
            // lists from the gameNode captured at OnLoad time. A RefreshContracts call anywhere inside that
            // window sees mainCount==0 on a list that is about to be repopulated from the persisted save.
            // Matches the gate used in <see cref="ContractsPersistentSyncClientDomain.FlushPendingState"/> so
            // both the snapshot apply path and the stock-refresh path respect the same coroutine boundary.
            if (IsReplenishGateBlocked(out var blockReason))
            {
                TryDeferReplenishStockOffersUntilSafe(source, blockReason);
                return;
            }

            var main = ContractSystem.Instance.Contracts;
            if (main == null)
            {
                return;
            }

            // Always run stock RefreshContracts inside the controlled window. Stock's implementation iterates
            // over each prestige level and only ADDS contracts until the per-prestige cap (ContractSystem's
            // maxContracts[level]) is reached — it does not remove or replace existing offers. So calling this
            // when the pool is already partially full is a cheap no-op for prestige levels at cap and an
            // "append up to cap" for levels below it. This is exactly what we need for mid-game progression:
            // completing a starter contract advances ProgressTracking, which unlocks higher-prestige contract
            // types on the next generator pass; without this call, our gate on generateContractIterations=0
            // prevents stock's own Update-driven generator from ever minting the newly-unlocked contracts, and
            // the user gets permanently stuck with whatever offer mix was present at the last empty-pool refill.
            //
            // A previous short-circuit returned here when offeredLikeCount > 0 to avoid redundant refresh
            // calls, but that also blocked the progression-unlock refill. The snapshot/OnLoad-window guards
            // above already cover the original concern (running RefreshContracts on a cleared/half-loaded
            // list and inflating duplicate starter offers), so the old "pool already has something"
            // short-circuit is not needed for safety and is actively harmful for gameplay progression.
            var offeredLikeCount = main.Count(IsMissionControlOfferPoolContract);
            LunaLog.Log(
                $"[PersistentSync] ReplenishStockOffers: controlled RefreshContracts source={source} " +
                $"mainCount={main.Count} activeCount={main.Count(c => c != null && c.ContractState == Contract.State.Active)} " +
                $"offerPoolLikeCount={offeredLikeCount} (stock will append up to per-prestige caps; " +
                $"no-op for levels already at cap)");
            try
            {
                _allowStockContractRefreshWindow = true;
                ApplyStockContractMutationPolicy(source + ":ReplenishOpenWindow");
                InvokeOptionalMethod(ContractSystem.Instance, "RefreshContracts");
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[PersistentSync] ReplenishStockOffers: RefreshContracts failed source={source}: {e.Message}");
            }
            finally
            {
                _allowStockContractRefreshWindow = false;
                ApplyStockContractMutationPolicy(source + ":ReplenishCloseWindow");
            }
        }

        /// <summary>
        /// Returns true when <see cref="ReplenishStockOffersAfterPersistentSnapshotApply"/> must not run its
        /// inner <c>RefreshContracts</c> right now because the live <see cref="ContractSystem.Instance"/> is
        /// in a transient state where "offer pool empty" is a false positive. See the two-windows comment at
        /// the call site for the full rationale. Sets <paramref name="reason"/> to a short tag describing
        /// which window fired so log lines remain distinguishable.
        /// </summary>
        private bool IsReplenishGateBlocked(out string reason)
        {
            if (PersistentSyncSystem.Singleton != null &&
                PersistentSyncSystem.Singleton.Domains.TryGetValue(PersistentSyncDomainId.Contracts, out var contractsDomain) &&
                contractsDomain is ContractsPersistentSyncClientDomain contractsClientDomain &&
                contractsClientDomain.HasPendingSnapshot)
            {
                reason = "contracts-snapshot-pending-apply";
                return true;
            }

            if (ContractSystem_OnLoad_EnsureContractsNode.IsOnLoadCoroutineWithinCompletionWindow())
            {
                var framesSinceOnLoad = Time.frameCount - ContractSystem_OnLoad_EnsureContractsNode.LastOnLoadFrame;
                reason =
                    $"on-load-coroutine-in-window({framesSinceOnLoad}/" +
                    $"{ContractSystem_OnLoad_EnsureContractsNode.OnLoadCoroutineCompletionWindowFrames})";
                return true;
            }

            reason = null;
            return false;
        }

        /// <summary>
        /// Schedules (or extends) a single coroutine that polls the Replenish gate until it opens and then
        /// re-invokes <see cref="ReplenishStockOffersAfterPersistentSnapshotApply"/> with the most recent
        /// <paramref name="source"/>. Calls that land inside the blocked window collapse into this one
        /// deferred run so we do not stack retries per LevelLoaded / LockAcquire / snapshot tick.
        /// </summary>
        private void TryDeferReplenishStockOffersUntilSafe(string source, string blockReason)
        {
            _pendingReplenishAfterOnLoadSource = source;

            if (_pendingReplenishAfterOnLoadCoroutine != null || MainSystem.Singleton == null)
            {
                // Already waiting — next tick of the coroutine will pick up the latest source string.
                LunaLog.Log(
                    $"[PersistentSync] ReplenishStockOffers deferred (retry already queued): source={source} " +
                    $"reason={blockReason}");
                return;
            }

            LunaLog.Log(
                $"[PersistentSync] ReplenishStockOffers deferred: source={source} reason={blockReason} " +
                $"retryPoll={PendingReplenishPollSeconds:0.##}s maxWait={PendingReplenishMaxWaitSeconds:0}s");

            _pendingReplenishAfterOnLoadCoroutine =
                MainSystem.Singleton.StartCoroutine(PendingReplenishAfterOnLoadWindowCoroutine());
        }

        private IEnumerator PendingReplenishAfterOnLoadWindowCoroutine()
        {
            var waited = 0f;
            while (waited < PendingReplenishMaxWaitSeconds)
            {
                if (!Enabled || !IsShareSystemApplicableForSession() || ContractSystem.Instance == null)
                {
                    LunaLog.Log(
                        $"[PersistentSync] ReplenishStockOffers retry abort: enabled={Enabled} " +
                        $"applicable={IsShareSystemApplicableForSession()} " +
                        $"contractSystem={(ContractSystem.Instance != null)}");
                    _pendingReplenishAfterOnLoadCoroutine = null;
                    _pendingReplenishAfterOnLoadSource = null;
                    yield break;
                }

                if (!IsReplenishGateBlocked(out _))
                {
                    var source = _pendingReplenishAfterOnLoadSource ?? "DeferredReplenish";
                    _pendingReplenishAfterOnLoadCoroutine = null;
                    _pendingReplenishAfterOnLoadSource = null;

                    LunaLog.Log(
                        $"[PersistentSync] ReplenishStockOffers retry fired: source={source} waitedSeconds={waited:0.##}");
                    // Re-enter the public entry point so lock ownership and UI-hook side effects match a
                    // normal call. The gate is clear now so the inner RefreshContracts path will run.
                    ReplenishStockOffersAfterPersistentSnapshotApply(source + ":DeferredRetry");
                    yield break;
                }

                yield return new WaitForSecondsRealtime(PendingReplenishPollSeconds);
                waited += PendingReplenishPollSeconds;
            }

            LunaLog.LogWarning(
                $"[PersistentSync] ReplenishStockOffers retry timeout after {PendingReplenishMaxWaitSeconds:0}s " +
                $"source={_pendingReplenishAfterOnLoadSource ?? "<none>"}; abandoning deferred pass " +
                "(gate never cleared)");
            _pendingReplenishAfterOnLoadCoroutine = null;
            _pendingReplenishAfterOnLoadSource = null;
        }

        /// <summary>
        /// Called by <see cref="PersistentSync.ContractsPersistentSyncClientDomain"/> after a canonical snapshot is
        /// applied. If this client is NOT the contract lock owner and the snapshot has no offer-pool contracts, send a
        /// single <see cref="ContractIntentPayloadKind.RequestOfferGeneration"/> intent per revision/state signature so
        /// the server can route the snapshot to the designated producer to mint offers. Gated on revision signature to
        /// avoid a busy-loop while canonical state is unchanged.
        /// </summary>
        public void NotifyNonProducerContractsSnapshotApplied(string source)
        {
            if (!IsShareSystemApplicableForSession() || ContractSystem.Instance == null)
            {
                return;
            }

            var ps = PersistentSyncSystem.Singleton;
            if (ps == null || !ps.Enabled)
            {
                return;
            }

            if (LockSystem.LockQuery.ContractLockBelongsToPlayer(SettingsSystem.CurrentSettings.PlayerName))
            {
                return;
            }

            var main = ContractSystem.Instance.Contracts;
            if (main == null)
            {
                return;
            }

            if (main.Any(IsMissionControlOfferPoolContract))
            {
                _lastNonProducerOfferGenerationRequestSignature = null;
                return;
            }

            var revision = ps.GetKnownRevision(PersistentSyncDomainId.Contracts);
            var signature = $"rev={revision}|main={main.Count}|finished={ContractSystem.Instance.ContractsFinished?.Count ?? 0}";
            if (string.Equals(signature, _lastNonProducerOfferGenerationRequestSignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastNonProducerOfferGenerationRequestSignature = signature;
            LunaLog.Log(
                $"[PersistentSync] contract request-offer-generation sent source={source} signature={signature} " +
                $"contractLockOwner={LockSystem.LockQuery.ContractLockOwner() ?? "<none>"}");
            MessageSender.SendRequestOfferGeneration($"NonProducerEmptyOfferPool:{source}");
        }

        /// <summary>
        /// Called when THIS client acquires the contract lock. Once the controlled-refresh settle window passes and
        /// we have actually applied the server's authoritative Contracts snapshot, we own producer authority for the
        /// canonical contract domain; publish a single explicit producer full-reconcile so the server can accept a
        /// FullReplace from the rightful authority on transfer (rather than relying only on per-proposal convergence).
        ///
        /// CRITICAL GATE: we MUST NOT publish our local <c>ContractSystem</c> state as a full-reconcile before the
        /// initial authoritative snapshot has been applied in this session. On a fresh (re)connect the local list
        /// is whatever stock KSP regenerated from <c>ProgressTracking</c> in <c>HighLogic.CurrentGame.Start()</c>
        /// (typically 4 starter offers with brand-new GUIDs); pushing that as a FullReplace would overwrite the
        /// server's authoritative Active/Finished rows (accepted missions, completed archive entries).
        /// Server-side <c>ReduceFullReplace</c> enforces the contract state machine as a defense in depth, but this
        /// client-side gate avoids the wasted round trip entirely.
        /// </summary>
        public void ScheduleProducerFullReconcileAfterLockHandoff(string source)
        {
            if (!IsShareSystemApplicableForSession() || ContractSystem.Instance == null)
            {
                return;
            }

            if (MainSystem.Singleton == null)
            {
                return;
            }

            MainSystem.Singleton.StartCoroutine(PublishProducerFullReconcileAfterSettleCoroutine(source));
        }

        private IEnumerator PublishProducerFullReconcileAfterSettleCoroutine(string source)
        {
            yield return new WaitForSecondsRealtime(ControlledStockRefreshSettleSeconds);
            if (!Enabled || ContractSystem.Instance == null || !IsShareSystemApplicableForSession())
            {
                yield break;
            }

            if (!LockSystem.LockQuery.ContractLockBelongsToPlayer(SettingsSystem.CurrentSettings.PlayerName))
            {
                LunaLog.Log($"[PersistentSync] producer full reconcile skipped source={source} reason=lock-no-longer-owned");
                yield break;
            }

            var ps = PersistentSyncSystem.Singleton;
            if (ps != null && ps.Enabled && !ps.Reconciler.State.HasInitialSnapshot(PersistentSyncDomainId.Contracts))
            {
                LunaLog.Log(
                    $"[PersistentSync] producer full reconcile skipped source={source} " +
                    "reason=authoritative-snapshot-not-yet-applied " +
                    "(local ContractSystem state is stock-regenerated; publishing would overwrite server truth)");
                yield break;
            }

            LunaLog.Log(
                $"[PersistentSync] producer full reconcile publish source={source} " +
                $"mainCount={ContractSystem.Instance.Contracts?.Count ?? 0} " +
                $"finishedCount={ContractSystem.Instance.ContractsFinished?.Count ?? 0}");
            MessageSender.SendFullContractReconcile($"ProducerFullReconcile:{source}");
        }

        /// <summary>
        /// Refreshes derived contract UI after local contract truth was already updated.
        /// Keep this separate from snapshot correctness so UI refreshes do not become the data model.
        /// </summary>
        public void RefreshContractUiAdapters(string source)
        {
            if (string.Equals(source, ContractSystemRefreshPersistentSyncBypassSource, StringComparison.Ordinal))
            {
                var coalesceSig = TryBuildPersistentSyncBypassUiRefreshCoalesceSignature();
                if (_persistentSyncBypassUiRefreshCoalesceSignature != null &&
                    string.Equals(coalesceSig, _persistentSyncBypassUiRefreshCoalesceSignature, StringComparison.Ordinal))
                {
                    return;
                }

                _persistentSyncBypassUiRefreshCoalesceSignature = coalesceSig;
            }

            LogMcUiContractInventory($"RefreshContractUiAdapters:before source={source}");
            RefreshContractLists(source);
            // After a PersistentSync snapshot, rebuild UI from the already-replaced ContractSystem using the
            // psApply-safe branches below. Those branches intentionally avoid stock refresh/regeneration entrypoints
            // and only rebuild visible lists/controls so reconnects do not keep stale local Mission Control data.
            RefreshContractsApp(source);
            RefreshMissionControl(source);
            LogMcUiContractInventory($"RefreshContractUiAdapters:after source={source}");
            if (IsPersistentSyncSafeContractUiRefresh(source) && ContractSystem.Instance != null &&
                ShouldLogPersistentSyncUiAdapterRefresh(source))
            {
                LunaLog.Log(
                    $"[PersistentSync] contract UI refresh source={source} adapters=contract-lists,contracts-app,mission-control " +
                    $"offered={ContractSystem.Instance.Contracts.Count} finished={ContractSystem.Instance.ContractsFinished.Count}");
            }
        }

        /// <summary>
        /// Clears bypass-path UI coalescing so the next <see cref="RefreshContractUiAdapters"/> run rebinds lists
        /// (e.g. contract parameter progress without state transitions).
        /// </summary>
        public void InvalidatePersistentSyncBypassUiRefreshCoalesce()
        {
            _persistentSyncBypassUiRefreshCoalesceSignature = null;
        }

        private static string TryBuildPersistentSyncBypassUiRefreshCoalesceSignature()
        {
            var cs = ContractSystem.Instance;
            if (cs == null)
            {
                return "ContractSystem=null";
            }

            var main = cs.Contracts;
            var fin = cs.ContractsFinished;
            var sb = new StringBuilder(256);
            sb.Append(main?.Count ?? 0).Append('|').Append(fin?.Count ?? 0).Append(':');
            if (main != null)
            {
                foreach (var c in main.Where(x => x != null).OrderBy(x => x.ContractGuid.ToString("N")))
                {
                    sb.Append(c.ContractGuid.ToString("N")).Append('=').Append((int)c.ContractState).Append(';');
                }
            }

            sb.Append('|');
            if (fin != null)
            {
                foreach (var c in fin.Where(x => x != null).OrderBy(x => x.ContractGuid.ToString("N")))
                {
                    sb.Append(c.ContractGuid.ToString("N")).Append('=').Append((int)c.ContractState).Append(';');
                }
            }

            // Mission Control / Contracts App instances appear after the player opens UI; rebind once when they do.
            sb.Append("|MC=").Append(MissionControl.Instance != null ? "1" : "0");
            sb.Append("|CA=").Append(ContractsApp.Instance != null ? "1" : "0");

            return sb.ToString();
        }

        /// <summary>
        /// Verbose Mission Control / ContractSystem inventory for diagnosing tab/list mismatch (grep <c>[MC-Diag]</c>).
        /// </summary>
        public static void LogMcUiContractInventory(string tag)
        {
            try
            {
                if (MainSystem.NetworkState < ClientState.Connected)
                {
                    return;
                }

                if (HighLogic.LoadedScene != GameScenes.SPACECENTER)
                {
                    return;
                }

                if (ContractSystem.Instance == null)
                {
                    LunaLog.Log($"[MC-Diag] {tag} ContractSystem=null");
                    return;
                }

                var main = ContractSystem.Instance.Contracts;
                var fin = ContractSystem.Instance.ContractsFinished;

                // Mission Control / ContractSystem call stock RefreshContracts very frequently; full inventory dumps
                // spam the log unless we coalesce identical (main,finished) shapes for a short window.
                var throttleMcDiag =
                    tag.IndexOf("RefreshContracts:stock-postfix", StringComparison.Ordinal) >= 0 ||
                    tag.IndexOf("RebuildContractList:stock-postfix", StringComparison.Ordinal) >= 0 ||
                    tag.IndexOf(ContractSystemRefreshPersistentSyncBypassSource, StringComparison.Ordinal) >= 0;
                if (throttleMcDiag)
                {
                    var sig = $"{main.Count}:{fin.Count}";
                    var nowRt = Time.realtimeSinceStartup;
                    if (string.Equals(sig, _mcDiagContractSystemRefreshThrottleSignature, StringComparison.Ordinal) &&
                        nowRt - _mcDiagContractSystemRefreshThrottleRealtime < McDiagContractSystemRefreshThrottleSeconds)
                    {
                        return;
                    }

                    _mcDiagContractSystemRefreshThrottleSignature = sig;
                    _mcDiagContractSystemRefreshThrottleRealtime = nowRt;
                }

                var warp = WarpSystem.Singleton;
                var lockOwner = LockSystem.LockQuery.ContractLockOwner();
                var lockSelf = LockSystem.LockQuery.ContractLockBelongsToPlayer(SettingsSystem.CurrentSettings.PlayerName);

                LunaLog.Log(
                    $"[MC-Diag] {tag} scene={HighLogic.LoadedScene} mode={HighLogic.CurrentGame?.Mode} " +
                    $"subspace={(warp != null ? warp.CurrentSubspace.ToString() : "null")} waitingSubspace={(warp != null && warp.WaitingSubspaceIdFromServer)} " +
                    $"contractLockOwner={(string.IsNullOrEmpty(lockOwner) ? "none" : lockOwner)} lockIsSelf={lockSelf} " +
                    $"MissionControl={(MissionControl.Instance != null)} ContractsApp={(ContractsApp.Instance != null)} " +
                    $"ContractSystem.loaded={ContractSystem.loaded} ContractSystem.Instance={(ContractSystem.Instance != null ? ContractSystem.Instance.GetInstanceID().ToString() : "null")} " +
                    $"mainCount={main.Count} finishedCount={fin.Count}");

                var activeInMain = main.Count(c => c != null && c.ContractState == Contract.State.Active);
                var offerPoolLike = main.Count(c => c != null && IsMissionControlOfferPoolContract(c));
                LunaLog.Log($"[MC-Diag] {tag} mainBreakdown activeInMain={activeInMain} offerPoolLikeInMain={offerPoolLike}");
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[MC-Diag] {tag} logging failed: {e}");
            }
        }

        private static bool IsPersistentSyncSafeContractUiRefresh(string source) =>
            string.Equals(source, "PersistentSyncSnapshotApply", StringComparison.Ordinal) ||
            string.Equals(source, PersistentSyncPostTransientStockRefreshSource, StringComparison.Ordinal) ||
            string.Equals(source, ContractSystemRefreshPersistentSyncBypassSource, StringComparison.Ordinal);

        /// <summary>
        /// Coalesces repeated "[PersistentSync] contract UI refresh" lines when the same source/counts fire often.
        /// </summary>
        private static bool ShouldLogPersistentSyncUiAdapterRefresh(string source)
        {
            var cs = ContractSystem.Instance;
            var offered = cs?.Contracts?.Count ?? -1;
            var finished = cs?.ContractsFinished?.Count ?? -1;
            var signature = $"{source}|{offered}:{finished}";
            var now = Time.realtimeSinceStartup;
            if (string.Equals(_persistentSyncUiAdapterLogSignature, signature, StringComparison.Ordinal) &&
                now - _persistentSyncUiAdapterLogRealtime < PersistentSyncUiAdapterLogThrottleSeconds)
            {
                return false;
            }

            _persistentSyncUiAdapterLogSignature = signature;
            _persistentSyncUiAdapterLogRealtime = now;
            return true;
        }

        /// <summary>
        /// When PersistentSync has applied the contracts snapshot, stock <see cref="ContractSystem.RefreshContracts"/>
        /// is unsafe (it can wipe server-backed offers). Explicit replenish windows open
        /// <see cref="_allowStockContractRefreshWindow"/> and must still run the original method.
        /// </summary>
        public bool ShouldBypassStockContractSystemRefreshForPersistentContracts()
        {
            if (!IsShareSystemApplicableForSession() || ContractSystem.Instance == null)
            {
                return false;
            }

            if (_allowStockContractRefreshWindow)
            {
                return false;
            }

            var ps = PersistentSyncSystem.Singleton;
            if (ps == null || !ps.Enabled)
            {
                return false;
            }

            return ps.Reconciler.State.HasInitialSnapshot(PersistentSyncDomainId.Contracts);
        }

        /// <summary>
        /// True once PersistentSync holds the authoritative Contracts snapshot for this session. Unlike
        /// <see cref="ShouldBypassStockContractSystemRefreshForPersistentContracts"/>, this predicate is
        /// <b>not</b> silenced by <see cref="_allowStockContractRefreshWindow"/>: the controlled refresh window
        /// is meant for offer <i>generation</i>, not for stock to withdraw server-authoritative offers down to
        /// local tier caps. Used by
        /// <see cref="Harmony.ContractSystem_WithdrawSurplusContractsPersistentSyncGuard"/>.
        /// </summary>
        public bool IsPersistentSyncAuthoritativeForContracts()
        {
            if (!IsShareSystemApplicableForSession() || ContractSystem.Instance == null)
            {
                return false;
            }

            var ps = PersistentSyncSystem.Singleton;
            if (ps == null || !ps.Enabled)
            {
                return false;
            }

            return ps.Reconciler.State.HasInitialSnapshot(PersistentSyncDomainId.Contracts);
        }

        private static void RefreshContractLists(string source)
        {
            if (ContractSystem.Instance == null)
            {
                return;
            }

            try
            {
                var psSafe = IsPersistentSyncSafeContractUiRefresh(source);

                // RefreshContracts() asks stock to rebuild/regenerate the offer pool. After a PersistentSync snapshot
                // we already replaced ContractSystem from server truth — calling RefreshContracts spawns duplicate
                // ContractOffered events (same titles, new GUIDs) and thrashes Mission Control.
                if (!psSafe)
                {
                    InvokeOptionalMethod(ContractSystem.Instance, "RefreshContracts");
                }

                // Firing these after a snapshot makes stock think the contract DB was reloaded from disk and it will
                // generate default progression offers on top of the synced list (tutorial-tier spam, duplicate titles).
                if (!psSafe)
                {
                    GameEvents.Contract.onContractsListChanged.Fire();
                    GameEvents.Contract.onContractsLoaded.Fire();
                }
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[PersistentSync] contract UI refresh failed source={source} adapter=contract-lists error={e}");
            }
        }

        private static void RefreshContractsApp(string source)
        {
            if (ContractsApp.Instance == null)
            {
                return;
            }

            if (IsPersistentSyncSafeContractUiRefresh(source) && !IsContractsAppReadyForPersistentSyncRefresh())
            {
                return;
            }

            try
            {
                if (IsPersistentSyncSafeContractUiRefresh(source))
                {
                    // OnContractsLoaded runs full stock reload logic; only rebuild the UI list from current ContractSystem.
                    InvokeOptionalMethod(ContractsApp.Instance, "CreateContractsList");
                }
                else
                {
                    // OnContractsLoaded already calls CreateContractsList internally; invoking CreateContractsList again
                    // can NRE when stock UI is only partially constructed during PersistentSync flush.
                    InvokeOptionalMethod(ContractsApp.Instance, "OnContractsLoaded");
                }
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[PersistentSync] contract UI refresh failed source={source} adapter=contracts-app error={e}");
            }
        }

        private static bool IsContractsAppReadyForPersistentSyncRefresh()
        {
            var app = ContractsApp.Instance;
            if (app == null || !app.isActiveAndEnabled || app.gameObject == null || !app.gameObject.activeInHierarchy)
            {
                return false;
            }

            return HasNonNullInstanceField(app, "appFrame") &&
                   HasNonNullInstanceField(app, "cascadingList") &&
                   HasNonNullInstanceField(app, "contractList");
        }

        private static bool HasNonNullInstanceField(object target, string fieldName)
        {
            if (target == null || string.IsNullOrEmpty(fieldName))
            {
                return false;
            }

            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field != null && field.GetValue(target) != null;
        }

        private static void RefreshMissionControl(string source)
        {
            if (MissionControl.Instance == null)
            {
                var skipSig = $"MissionControlNull|{source}";
                var skipNow = Time.realtimeSinceStartup;
                if (!string.Equals(skipSig, _mcDiagMissionControlNullSkipSignature, StringComparison.Ordinal) ||
                    skipNow - _mcDiagMissionControlNullSkipRealtime >= McDiagMissionControlNullSkipThrottleSeconds)
                {
                    _mcDiagMissionControlNullSkipSignature = skipSig;
                    _mcDiagMissionControlNullSkipRealtime = skipNow;
                    LunaLog.Log($"[MC-Diag] RefreshMissionControl skip MissionControl.Instance=null source={source}");
                }

                return;
            }

            try
            {
                LogMcUiContractInventory($"RefreshMissionControl:beforeInvoke source={source}");
                if (!IsPersistentSyncSafeContractUiRefresh(source))
                {
                    InvokeOptionalMethod(MissionControl.Instance, "RefreshContracts");
                }

                InvokeOptionalMethod(MissionControl.Instance, "RebuildContractList");
                InvokeOptionalMethod(MissionControl.Instance, "RefreshUIControls");

                LogMcUiContractInventory($"RefreshMissionControl:afterInvoke source={source}");
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[PersistentSync] contract UI refresh failed source={source} adapter=mission-control error={e}");
            }
        }

        private static void InvokeOptionalMethod(object target, string methodName)
        {
            if (target == null)
            {
                return;
            }

            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                return;
            }

            method.Invoke(target, null);
        }
    }
}
