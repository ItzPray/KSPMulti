using Contracts;
using KSP.UI.Screens;
using LmpClient;
using LmpClient.Base;
using LmpClient.Events;
using LmpClient.Systems.Lock;
using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.ShareProgress;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.Warp;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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

        private bool _suppressStockContractRefreshForTimeJump;

        private bool _pendingStockContractRefreshAfterTransientState;

        private bool _allowStockContractRefreshWindow;

        private bool _awaitingAuthoritativeSnapshotAfterControlledRefresh;

        private int _lastAppliedGenerateContractIterations = int.MinValue;

        private string _lastControlledStockRefreshBlockReason;

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
            _lastAppliedGenerateContractIterations = int.MinValue;

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

                if (PersistentSyncSystem.Singleton == null || !PersistentSyncSystem.Singleton.Enabled)
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
                RequestControlledStockContractRefresh("LargeUniversalTimeJumpStart");
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
                MessageSender.SendContractMessage(contract);
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

            return string.Empty;
        }

        private string DescribeControlledStockRefreshState()
        {
            var warp = WarpSystem.Singleton;
            var owner = LockSystem.LockQuery.ContractLockOwner();
            return $"pending={_pendingStockContractRefreshAfterTransientState} awaiting={_awaitingAuthoritativeSnapshotAfterControlledRefresh} allowWindow={_allowStockContractRefreshWindow} suppressForJump={_suppressStockContractRefreshForTimeJump} contractLockOwner={(string.IsNullOrEmpty(owner) ? "none" : owner)} currentSubspace={(warp != null ? warp.CurrentSubspace.ToString() : "null")} waitingSubspace={(warp != null && warp.WaitingSubspaceIdFromServer)} timeWarpRateIndex={TimeWarp.CurrentRateIndex} timeWarpRate={TimeWarp.CurrentRate:0.00}";
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

        private void TryRunPostTransientStockRefresh(string source)
        {
            _pendingStockContractRefreshAfterTransientState = false;
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
        /// Refreshes derived contract UI after local contract truth was already updated.
        /// Keep this separate from snapshot correctness so UI refreshes do not become the data model.
        /// </summary>
        public void RefreshContractUiAdapters(string source)
        {
            RefreshContractLists(source);
            // PersistentSync: do not touch ContractsApp / MissionControl here. Stock's CreateContractsList,
            // RebuildContractList, and RefreshUIControls can schedule or immediately run offer generation on the
            // contract-lock client; that fires onOffered after this snapshot's IgnoreEvents window ends, producing
            // duplicate tutorial offers and endless snapshot revision churn.
            if (IsPersistentSyncSnapshotContractUiRefresh(source))
            {
                return;
            }

            RefreshContractsApp(source);
            RefreshMissionControl(source);
        }

        private static bool IsPersistentSyncSnapshotContractUiRefresh(string source) =>
            string.Equals(source, "PersistentSyncSnapshotApply", StringComparison.Ordinal);

        private static void RefreshContractLists(string source)
        {
            if (ContractSystem.Instance == null)
            {
                return;
            }

            try
            {
                var psApply = IsPersistentSyncSnapshotContractUiRefresh(source);

                // RefreshContracts() asks stock to rebuild/regenerate the offer pool. After a PersistentSync snapshot
                // we already replaced ContractSystem from server truth — calling RefreshContracts spawns duplicate
                // ContractOffered events (same titles, new GUIDs) and thrashes Mission Control.
                if (!psApply)
                {
                    InvokeOptionalMethod(ContractSystem.Instance, "RefreshContracts");
                }

                // Firing these after a snapshot makes stock think the contract DB was reloaded from disk and it will
                // generate default progression offers on top of the synced list (tutorial-tier spam, duplicate titles).
                if (!psApply)
                {
                    GameEvents.Contract.onContractsListChanged.Fire();
                    GameEvents.Contract.onContractsLoaded.Fire();
                }

                LunaLog.Log($"[PersistentSync] contract UI refresh source={source} adapter=contract-lists offered={ContractSystem.Instance.Contracts.Count} finished={ContractSystem.Instance.ContractsFinished.Count}");
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

            try
            {
                if (IsPersistentSyncSnapshotContractUiRefresh(source))
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

                LunaLog.Log($"[PersistentSync] contract UI refresh source={source} adapter=contracts-app");
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[PersistentSync] contract UI refresh failed source={source} adapter=contracts-app error={e}");
            }
        }

        private static void RefreshMissionControl(string source)
        {
            if (MissionControl.Instance == null)
            {
                return;
            }

            try
            {
                if (!IsPersistentSyncSnapshotContractUiRefresh(source))
                {
                    InvokeOptionalMethod(MissionControl.Instance, "RefreshContracts");
                }

                InvokeOptionalMethod(MissionControl.Instance, "RebuildContractList");
                InvokeOptionalMethod(MissionControl.Instance, "RefreshUIControls");
                LunaLog.Log($"[PersistentSync] contract UI refresh source={source} adapter=mission-control");
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
