using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
using Contracts;
using LmpClient.Extensions;
using LmpClient.Harmony;
using LmpClient.Systems.ShareContracts;
using LmpClient.Systems.ShareExperimentalParts;
using LmpClient.Systems.ShareFunds;
using LmpClient.Systems.ShareReputation;
using LmpClient.Systems.ShareScience;
using LmpClient.Utilities;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using LmpClient.Systems.PersistentSync.Domains;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LmpClient.Systems.PersistentSync
{
    public class ContractsPersistentSyncClientDomain : SyncClientDomain<ContractsPayload>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                // Contract offer generation keys off ProgressTracking; ensure achievement tree is applied before
                // contracts snapshot materialize in the same FlushPendingState pass.
                .After(PersistentSyncDomainNames.Achievements)
                .UsesClientDomain<ContractsPersistentSyncClientDomain>();
        }

        private const string LmpOfferTitleFieldName = "lmpOfferTitle";

        /// <summary>
        /// Throttle for <see cref="LogActiveReuseSkippedThrottled"/> — snapshot apply can touch many Active rows per frame.
        /// </summary>
        private const float ActiveReuseMismatchLogWindowSeconds = 5f;

        private static float _activeReuseMismatchLogWindowStartRealtime = -1f;

        private static int _activeReuseMismatchLogWindowCount;

        private static Guid _activeReuseMismatchLogSampleGuid;

        private static string _activeReuseMismatchLogSampleReason = string.Empty;

        private static string _activeReuseMismatchLogSampleTitle = string.Empty;

        /// <summary>
        /// Live contract-completion updates often arrive as a burst of adjacent contract revisions while scalar
        /// rewards and achievements settle. Coalesce those live revisions for one frame so the client applies only
        /// the latest contract snapshot instead of repeatedly rebuilding ContractSystem, mirroring the scenario
        /// proto, and refreshing UI in the same render frame. Initial join snapshots still apply immediately.
        /// </summary>
        private const int LiveContractSnapshotCoalesceFrames = 1;

        private int _deferLiveSnapshotApplyUntilFrame = -1;

        private bool _liveSnapshotApplyFlushScheduled;

        /// <summary>
        /// Contracts whose snapshot row was merged with live <see cref="ContractParameter"/> completion before
        /// <see cref="Contract.Load"/>; after snapshot apply we emit <see cref="ContractIntentPayloadKind.ParameterProgressObserved"/>
        /// so the server catches up when stock had advanced locally but the canonical row still lagged.
        /// </summary>
        private static readonly List<Guid> PostSnapshotParameterProgressGuids = new List<Guid>();

        private ContractSnapshotInfo[] _pendingContracts;

        /// <summary>
        /// True while a Contracts snapshot has been received from the server but has not yet successfully
        /// applied. The reconciler's retry loop calls <see cref="FlushPendingState"/> repeatedly until the
        /// OnLoad frame-window gate clears and the apply succeeds; between those moments the pending
        /// snapshot is authoritative — any stock mutation that observes the live
        /// <see cref="ContractSystem.Instance"/> in its cleared (post-OnAwake) / half-loaded state will
        /// generate offers from an empty progression view and our PersistentSync pipeline will publish
        /// those offers to the server <b>before</b> the snapshot wipes them out locally. The server has
        /// no way to reject them (offers that "happen to share a type+title with a Finished row" are not
        /// caught by the offered-duplicate-of-Active guard), so they land as duplicates alongside the
        /// legitimate Finished records.
        ///
        /// Callers such as <see cref="ShareContracts.ShareContractsSystem.ReplenishStockOffersAfterPersistentSnapshotApply"/>
        /// must consult this flag and short-circuit when it is true; running stock
        /// <c>RefreshContracts</c> during this window is the root cause of post-reconnect "duplicate
        /// completed contract appears in Available" reports.
        /// </summary>
        public bool HasPendingSnapshot => _pendingContracts != null;

        protected override void OnPayloadBuffered(PersistentSyncBufferedSnapshot snapshot, ContractsPayload payload)
        {
            _pendingContracts = (payload?.Snapshot?.Contracts ?? new List<ContractSnapshotInfo>())
                .OrderBy(contract => contract.Order)
                .ToArray();
            StageLiveSnapshotCoalesceIfApplicable();
            LunaLog.Log(
                $"[PersistentSync] Contracts snapshot received wireRows={_pendingContracts.Length} payloadBytes={snapshot.NumBytes}");
        }

        public override PersistentSyncApplyOutcome FlushPendingState()
        {
            if (_pendingContracts == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (ContractSystem.Instance == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (ShouldDeferForLiveSnapshotCoalesce())
            {
                ScheduleLiveSnapshotCoalescedFlush();
                return PersistentSyncApplyOutcome.Deferred;
            }

            // Frame-delta gate anchored at the last observed ContractSystem.OnLoad. Stock OnLoadRoutine is a
            // coroutine that yields one frame, then runs to completion (it clears ContractSystem.Instance.Contracts
            // before repopulating from the gameNode captured at OnLoad time). Applying between the yield and
            // the clear would be wiped; applying once the completion window has elapsed is safe. The legacy
            // gate on static ContractSystem.loaded was fundamentally unreliable — stock's OnAwake resets it to
            // false on every scenario-runner rebuild, and the coroutine only sets it to true on the non-early-exit
            // path, so the gate could stay false permanently while ContractSystem.Instance was fully alive.
            // The helper is shared with ShareContractsSystem so both the snapshot apply path and the stock-refresh
            // gate use the same invariant — see ContractSystem_OnLoad_EnsureContractsNode.IsOnLoadCoroutineWithinCompletionWindow.
            if (ContractSystem_OnLoad_EnsureContractsNode.IsOnLoadCoroutineWithinCompletionWindow())
            {
                var framesSinceOnLoad = Time.frameCount - ContractSystem_OnLoad_EnsureContractsNode.LastOnLoadFrame;
                LunaLog.Log(
                    $"[PersistentSync] Contracts snapshot deferred: OnLoad ran {framesSinceOnLoad} frame(s) ago " +
                    $"(completion window={ContractSystem_OnLoad_EnsureContractsNode.OnLoadCoroutineCompletionWindowFrames} frames); " +
                    "waiting for OnLoadRoutine coroutine to finish clearing+repopulating the list");
                return PersistentSyncApplyOutcome.Deferred;
            }

            ShareContractsSystem.Singleton.StartIgnoringEvents();
            ShareFundsSystem.Singleton.StartIgnoringEvents();
            ShareScienceSystem.Singleton.StartIgnoringEvents();
            ShareReputationSystem.Singleton.StartIgnoringEvents();
            ShareExperimentalPartsSystem.Singleton.StartIgnoringEvents();

            // Keep a reference so we can restore _pendingContracts if the apply throws BEFORE ReplaceContractsFromSnapshot
            // succeeds — that branch leaves the live ContractSystem untouched, so the reconciler must still see a
            // pending snapshot and retry on the next FlushPendingState tick. Once the authoritative replace returns,
            // we clear _pendingContracts (so the HasPendingSnapshot gate opens for downstream replenish/UI calls) and
            // DO NOT restore it on later exceptions — those post-replace steps are cosmetic (UI rebind, notifications)
            // and re-running them via a retry would re-enter ReplaceContractsFromSnapshot with fresh Guid tracking
            // that is no longer needed.
            var pendingForRestore = _pendingContracts;
            var replaceCompleted = false;

            try
            {
                // LevelLoaded / lock / warp queue a deferred stock RefreshContracts pass. If it runs after we apply
                // the server snapshot it regenerates starter missions from local ProgressTracking and stacks them on
                // top of synced offers (duplicate "completed" starters in Available + truncated list until Accept).
                ShareContractsSystem.Singleton.CancelPendingControlledStockContractRefresh("PersistentSyncSnapshotApply:PreReplace");

                // Seed the server-known contract tracker BEFORE materializing contracts. ReplaceContractsFromSnapshot
                // calls Contract.Load which — together with the stock Contract.Update sweep that follows the apply —
                // can invoke Contract.Withdraw for any offered row whose dateExpire is behind the current UT. UT jumps
                // forward after reconnect, so every snapshot-materialized offer satisfies that predicate. The
                // Contract.Withdraw Harmony guard keys its suppression decision off the tracker (a tracked GUID means
                // the server holds the authoritative row), so the tracker must already contain the incoming GUIDs by
                // the time any withdraw fires. Order-dependent: move this line after ReplaceContractsFromSnapshot and
                // the guard no-ops for a few frames, allowing stock to prune the offer pool down to only rows whose
                // dateExpire happens to still be ahead of UT.
                ShareContractsSystem.Singleton.MessageSender.ResetKnownContractSnapshots(_pendingContracts);
                ReplaceContractsFromSnapshot(_pendingContracts);
                TryReconcileStockTutorialGatesAfterContractsSnapshotReplace();
                // Stock RefreshContracts reloads from HighLogic.CurrentGame.scenarios ContractSystem proto. We mutate
                // ContractSystem.Instance lists only — without mirroring here, a later RefreshContracts (subspace lock,
                // Mission Control, etc.) rebuilds from stale proto and wipes offers while keeping ContractsFinished.
                PersistentSyncScenarioProtoMaterializer.TryMirrorScenarioModule(
                    ContractSystem.Instance,
                    "ContractSystem",
                    "PersistentSyncSnapshotApply:Contracts");

                // Clear the pending snapshot marker BEFORE calling ReplenishStockOffers below. The ContractSystem
                // has now been authoritatively replaced from the server snapshot, so downstream mutation paths
                // (including the Replenish call on the next line, which may invoke stock RefreshContracts) are
                // allowed to run. Leaving _pendingContracts set here would cause the HasPendingSnapshot gate in
                // <see cref="ShareContracts.ShareContractsSystem.ReplenishStockOffersAfterPersistentSnapshotApply"/>
                // to treat our own in-apply call as "snapshot pending" and short-circuit, which would skip the
                // controlled refresh this code path is specifically here to perform when the incoming snapshot
                // carried zero offer-pool contracts. The gate's purpose is to block stock refreshes triggered
                // by OTHER paths (ContractLockAcquire, LevelLoaded, etc.) while a snapshot is queued but not
                // yet merged into the live ContractSystem — that race is closed as soon as ReplaceContractsFromSnapshot
                // returns, because from that instant the live game state IS the server's canonical state.
                _pendingContracts = null;
                replaceCompleted = true;

                ShareContractsSystem.Singleton.ReplenishStockOffersAfterPersistentSnapshotApply("PersistentSyncSnapshotApply");
                ShareContractsSystem.LogMcUiContractInventory("PersistentSyncSnapshotApply:afterReplaceContractsFromSnapshot");
                // Keep ShareContractsSystem ignoring events through UI refresh. RefreshContractUiAdapters uses
                // psApply-safe paths (no RefreshContracts / contract GameEvents) while rebuilding ContractsApp and
                // Mission Control lists from the already-replaced ContractSystem model.
                ShareContractsSystem.Singleton.RefreshContractUiAdapters("PersistentSyncSnapshotApply");
                ShareContractsSystem.Singleton.CancelPendingControlledStockContractRefresh("PersistentSyncSnapshotApply:PostUi");
                ShareContractsSystem.Singleton.NotifyAuthoritativeContractsSnapshotApplied("PersistentSyncSnapshotApply");
                // Non-producers with an empty offer pool ask the server to route this snapshot to the current
                // producer so stock generation runs once there and new offers flow back as OfferObserved proposals.
                ShareContractsSystem.Singleton.NotifyNonProducerContractsSnapshotApplied("PersistentSyncSnapshotApply");
            }
            catch (Exception)
            {
                // Before ReplaceContractsFromSnapshot completed the live game state was unchanged, so the
                // reconciler must treat the snapshot as still-pending and retry on the next flush tick. Once
                // the replace committed we leave _pendingContracts cleared and accept the loss of the purely
                // cosmetic post-replace steps (UI rebind, notifications) for this revision — the authoritative
                // contract data is already in ContractSystem.Instance and will be picked up by the regular
                // Mission Control / ContractsApp refresh paths.
                if (!replaceCompleted)
                {
                    _pendingContracts = pendingForRestore;
                }
                return PersistentSyncApplyOutcome.Rejected;
            }
            finally
            {
                ShareFundsSystem.Singleton.StopIgnoringEvents(true);
                ShareScienceSystem.Singleton.StopIgnoringEvents(true);
                ShareReputationSystem.Singleton.StopIgnoringEvents(true);
                ShareExperimentalPartsSystem.Singleton.StopIgnoringEvents();
                ShareContractsSystem.Singleton.StopIgnoringEvents();
            }

            FlushPostSnapshotMergedContractParameterProgressProposals();

            // _pendingContracts was cleared inside the try block immediately after ReplaceContractsFromSnapshot
            // completed so the HasPendingSnapshot gate opens before we invoke ReplenishStockOffers. See the
            // comment at that clear site for the full rationale.
            return PersistentSyncApplyOutcome.Applied;
        }

        private void StageLiveSnapshotCoalesceIfApplicable()
        {
            if (MainSystem.NetworkState < ClientState.PersistentStateSynced)
            {
                _deferLiveSnapshotApplyUntilFrame = -1;
                return;
            }

            var ps = PersistentSyncSystem.Singleton;
            if (ps == null || !ps.Enabled || !ps.Reconciler.State.HasInitialSnapshot(DomainId))
            {
                _deferLiveSnapshotApplyUntilFrame = -1;
                return;
            }

            _deferLiveSnapshotApplyUntilFrame = Time.frameCount + LiveContractSnapshotCoalesceFrames;
        }

        private bool ShouldDeferForLiveSnapshotCoalesce()
        {
            return _deferLiveSnapshotApplyUntilFrame >= 0 &&
                   Time.frameCount < _deferLiveSnapshotApplyUntilFrame;
        }

        private void ScheduleLiveSnapshotCoalescedFlush()
        {
            if (_liveSnapshotApplyFlushScheduled)
            {
                return;
            }

            _liveSnapshotApplyFlushScheduled = true;
            CoroutineUtil.StartFrameDelayedRoutine(
                "PersistentSync.Contracts.CoalescedLiveSnapshotFlush",
                () =>
                {
                    _liveSnapshotApplyFlushScheduled = false;
                    PersistentSyncSystem.Singleton?.Reconciler.FlushPendingState();
                },
                LiveContractSnapshotCoalesceFrames);
        }

        /// <summary>
        /// After contracts snapshot apply, push any merged live parameter completions to the server while events
        /// are enabled so <see cref="ShareContractsMessageSender.SendProducerProposal"/> is not skipped.
        /// </summary>
        private static void FlushPostSnapshotMergedContractParameterProgressProposals()
        {
            if (PostSnapshotParameterProgressGuids.Count == 0)
            {
                return;
            }

            var copy = PostSnapshotParameterProgressGuids.ToArray();
            PostSnapshotParameterProgressGuids.Clear();

            var share = ShareContractsSystem.Singleton;
            if (share?.MessageSender == null || !PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainNames.Contracts))
            {
                return;
            }

            foreach (var guid in copy)
            {
                var contract = FindContractByGuidInRuntimeLists(guid);
                if (contract == null || contract.ContractState != Contract.State.Active)
                {
                    continue;
                }

                share.MessageSender.SendProducerProposal(
                    ContractIntentPayloadKind.ParameterProgressObserved,
                    contract,
                    $"PersistentSyncSnapshotApply:MergedLiveParamProgress:{guid:N}");
            }
        }

        /// <summary>
        /// After contracts replace, align stock ProgressTracking tutorial gates with completed rows in
        /// <see cref="ContractSystem.ContractsFinished"/> before controlled replenish mints offers — see
        /// <see cref="ShareAchievements.ShareAchievementsSystem.ReconcileStockTutorialGatesFromFinishedContracts"/>.
        /// </summary>
        private static void TryReconcileStockTutorialGatesAfterContractsSnapshotReplace()
        {
            var ps = PersistentSyncSystem.Singleton;
            if (ps?.Domains == null ||
                !ps.Domains.TryGetValue(PersistentSyncDomainNames.Achievements, out var domainObj) ||
                !(domainObj is AchievementsPersistentSyncClientDomain achievementsDomain))
            {
                return;
            }

            achievementsDomain.ReconcileStockTutorialGatesFromFinishedContractsAfterContractsSnapshot(
                "PersistentSyncSnapshotApply:AfterContractsReplace");
        }

        private static Contract FindContractByGuidInRuntimeLists(Guid guid)
        {
            if (ContractSystem.Instance == null)
            {
                return null;
            }

            foreach (var c in ContractSystem.Instance.Contracts)
            {
                if (c != null && c.ContractGuid == guid)
                {
                    return c;
                }
            }

            foreach (var c in ContractSystem.Instance.ContractsFinished)
            {
                if (c != null && c.ContractGuid == guid)
                {
                    return c;
                }
            }

            return null;
        }

        private static void ReplaceContractsFromSnapshot(ContractSnapshotInfo[] contracts)
        {
            PostSnapshotParameterProgressGuids.Clear();
            contracts = DedupeContractsByGuidPreserveOrder(contracts);

            // Index the live contracts by GUID so we can preserve whichever instances already match the incoming
            // snapshot state. Re-loading a live Contract through Contract.Load is not idempotent for subclasses
            // whose OnLoad re-resolves runtime references captured at Accept time — VesselRepairContract resolves
            // ProtoVessel / ProtoPartSnapshot indices, RecoverAsset / GrandTour / CollectScience hold Kerbal and
            // Part rosters, etc. A full serialize -> Load round-trip on an Active contract can leave those
            // indices pointing at a vessel snapshot that no longer matches the live game state, which then trips
            // Contract.Fail() during the next scene transition (e.g. SPACECENTER -> TRACKSTATION). Preserving
            // the live instance when its GUID and state already agree with the snapshot keeps stock's in-memory
            // bindings intact, mirroring how stock itself no-ops when ContractSystem.Load sees an identical row.

            var liveByGuid = new Dictionary<Guid, Contract>();
            foreach (var contract in ContractSystem.Instance.Contracts)
            {
                if (contract != null && contract.ContractGuid != Guid.Empty && !liveByGuid.ContainsKey(contract.ContractGuid))
                {
                    liveByGuid[contract.ContractGuid] = contract;
                }
            }
            foreach (var contract in ContractSystem.Instance.ContractsFinished)
            {
                if (contract != null && contract.ContractGuid != Guid.Empty && !liveByGuid.ContainsKey(contract.ContractGuid))
                {
                    liveByGuid[contract.ContractGuid] = contract;
                }
            }

            var incomingGuids = new HashSet<Guid>();
            var newMain = new List<Contract>();
            var newFinished = new List<Contract>();
            var wireRows = contracts?.Length ?? 0;
            var materialized = 0;
            var preserved = 0;
            var skippedNull = 0;

            foreach (var contractInfo in contracts ?? Array.Empty<ContractSnapshotInfo>())
            {
                if (contractInfo == null || contractInfo.ContractGuid == Guid.Empty)
                {
                    continue;
                }

                incomingGuids.Add(contractInfo.ContractGuid);

                if (liveByGuid.TryGetValue(contractInfo.ContractGuid, out var liveContract) &&
                    TryReuseLiveContract(liveContract, contractInfo, newMain, newFinished))
                {
                    preserved++;
                    continue;
                }

                // Merge live parameter completion into the server row *before* Unregister: stock Contract.Save on
                // the live instance can depend on the contract still being registered.
                var deserializeSource = contractInfo;
                if (liveContract != null &&
                    liveContract.ContractState == Contract.State.Active &&
                    IsSnapshotMarkedActive(contractInfo) &&
                    TryMergeLiveActiveParameterProgressAheadOfServer(liveContract, contractInfo, out var mergedRow))
                {
                    deserializeSource = mergedRow;
                    PostSnapshotParameterProgressGuids.Add(contractInfo.ContractGuid);
                }

                // State mismatch or GUID not live: drop the previous live subscription (if any) and re-materialize
                // from the snapshot so Accept/Decline/Complete/Fail transitions propagate to stock KSP's scorers.
                if (liveContract != null)
                {
                    try { liveContract.Unregister(); }
                    catch (Exception e) { LunaLog.LogWarning($"[PersistentSync] contract Unregister before re-materialize failed guid={liveContract.ContractGuid}: {e.Message}"); }
                }

                var contract = DeserializeContract(deserializeSource);
                if (contract == null)
                {
                    skippedNull++;
                    continue;
                }

                materialized++;

                // Finished: runtime terminal states (and snapshot-safe archive semantics).
                if (ShouldPlaceInContractsFinished(contract))
                {
                    contract.Unregister();
                    newFinished.Add(contract);
                }
                // Active: trust snapshot metadata when Contract.Load still leaves Offered/Available — otherwise MC
                // lists active contracts under "available" until some unrelated Accept forces a full refresh.
                else if (IsSnapshotOrRuntimeActive(contract, contractInfo))
                {
                    newMain.Add(contract);
                    if (NeedsAcceptToRestoreActiveFromSnapshot(contract, contractInfo))
                    {
                        try
                        {
                            contract.Accept();
                        }
                        catch (Exception e)
                        {
                            LunaLog.LogError($"[PersistentSync] contract snapshot Accept() restore failed guid={contract.ContractGuid}: {e}");
                        }
                    }

                    contract.Register();
                }
                else
                {
                    newMain.Add(contract);
                    try
                    {
                        contract.Register();
                    }
                    catch (Exception e)
                    {
                        LunaLog.LogError($"[PersistentSync] contract Register failed guid={contract.ContractGuid}: {e.Message}");
                    }
                }
            }

            // Any live contracts whose GUIDs no longer appear in the snapshot must be dropped; the server is the
            // source of truth for membership and a missing GUID here means the row was retired.
            foreach (var kv in liveByGuid)
            {
                if (incomingGuids.Contains(kv.Key)) continue;
                try { kv.Value.Unregister(); }
                catch (Exception e) { LunaLog.LogWarning($"[PersistentSync] contract Unregister for dropped row failed guid={kv.Key}: {e.Message}"); }
            }

            ContractSystem.Instance.Contracts.Clear();
            ContractSystem.Instance.ContractsFinished.Clear();
            foreach (var c in newMain) ContractSystem.Instance.Contracts.Add(c);
            foreach (var c in newFinished) ContractSystem.Instance.ContractsFinished.Add(c);

            // Active rows deserialized from the server never run FinePrint OnAccepted(), so VesselSystemsParameter
            // launchID can stay at the server's Game.launchID while this client's Game.launchID is lower — impossible
            // for VesselLaunchedAfterID. Align requireNew thresholds to local Game.launchID (stock accept semantics).
            VesselSystemsParameterLaunchIdFix.ClampRequireNewLaunchIdsToLocalGame();
            // Join path uses CreateBlankGame (launchID defaults low) while vessels carry server-era part launchIDs;
            // bump the global counter to match the universe so future pad launches do not reuse stale ids.
            VesselSystemsParameterLaunchIdFix.AdvanceGameLaunchIdIfBelowMaxProtoPartLaunchIdAcrossVessels();

            if (wireRows > 0 && materialized == 0 && preserved == 0)
            {
                LunaLog.LogError(
                    $"[PersistentSync] Contracts snapshot had {wireRows} wire rows but none deserialized into Contract objects " +
                    $"(skippedNull={skippedNull}). First guid={(contracts != null && contracts.Length > 0 ? contracts[0].ContractGuid.ToString() : "n/a")}");
            }
        }

        /// <summary>
        /// Returns <c>true</c> and appends the live instance to the target list if its current runtime state already
        /// matches the snapshot placement/state. Called during snapshot apply to avoid unnecessary
        /// <see cref="Contract.Load"/> round-trips that destabilize subclass-specific runtime references.
        /// </summary>
        private static bool TryReuseLiveContract(Contract live, ContractSnapshotInfo info, List<Contract> newMain, List<Contract> newFinished)
        {
            if (live == null || info == null) return false;

            var snapshotFinished = info.Placement == ContractSnapshotPlacement.Finished ||
                                   IsSnapshotStateFinishedLike(info.ContractState);
            if (snapshotFinished)
            {
                if (ShouldPlaceInContractsFinished(live))
                {
                    live.Unregister();
                    newFinished.Add(live);
                    return true;
                }
                return false;
            }

            if (IsSnapshotMarkedActive(info))
            {
                if (live.ContractState == Contract.State.Active)
                {
                    // Do not reuse the live instance solely because both sides are Active. The server snapshot
                    // carries authoritative PARAM / subclass fields; reusing live without comparing bytes drops
                    // ParameterProgressObserved merges from other clients (common when the contract lock holder is
                    // not the flying player) and makes satellite / multi-parameter missions look "half synced".
                    //
                    // Byte-identical Contract.Save output is often false between server LunaConfigNode round-trips
                    // and local stock serialization (float formatting, key order), which produced rapid-fire
                    // rematerialize+reload in one scene load and broke runtime parameter trackers. When the PARAM
                    // name/state sequence still matches the server row, keep the live instance.
                    var liveSnap = ShareContractsMessageSender.TryBuildContractSnapshot(live);
                    if (liveSnap != null &&
                        (ContractSnapshotInfoComparer.AreEquivalent(liveSnap, info) ||
                         ActiveContractSemanticsMatchForLiveReuse(liveSnap, info)))
                    {
                        newMain.Add(live);
                        return true;
                    }

                    var reason = liveSnap == null ? "live_snapshot_null" : "live_snapshot_semantics_differ_from_server";
                    LogActiveReuseSkippedThrottled(info.ContractGuid, reason, live.Title);
                    return false;
                }
                return false;
            }

            // Snapshot implies Offered-pool placement: keep the live instance only when it is still genuinely offered
            // and not finished. Any other local state (Active, Completed, Failed, Declined, Withdrawn) is a mismatch
            // and must go through the re-materialize path so stock's contract state machine stays consistent.
            if (live.ContractState == Contract.State.Offered && !live.IsFinished())
            {
                newMain.Add(live);
                return true;
            }

            return false;
        }

        /// <summary>
        /// True when the server snapshot and a freshly serialized live row carry the same contract-level state and
        /// the same pre-order <c>PARAM</c> <c>name</c> + <c>state</c> sequence. Ignores benign serialize drift
        /// (orbit floats, field order) so we do not rematerialize Active contracts every revision.
        /// </summary>
        private static bool ActiveContractSemanticsMatchForLiveReuse(ContractSnapshotInfo liveSnap, ContractSnapshotInfo serverSnap)
        {
            if (liveSnap == null || serverSnap == null)
            {
                return false;
            }

            if (liveSnap.ContractGuid != serverSnap.ContractGuid)
            {
                return false;
            }

            if (!string.Equals(
                    liveSnap.ContractState?.Trim(),
                    serverSnap.ContractState?.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var liveRoot = TryParseContractConfigNode(liveSnap);
            var serverRoot = TryParseContractConfigNode(serverSnap);
            if (liveRoot == null || serverRoot == null)
            {
                return false;
            }

            var liveType = liveRoot.GetValue("type")?.Trim() ?? string.Empty;
            var serverType = serverRoot.GetValue("type")?.Trim() ?? string.Empty;
            if (!string.Equals(liveType, serverType, StringComparison.Ordinal))
            {
                return false;
            }

            var liveSeq = new List<(string typeName, string state)>();
            var serverSeq = new List<(string typeName, string state)>();
            CollectContractParamStatesDepthFirst(liveRoot, liveSeq);
            CollectContractParamStatesDepthFirst(serverRoot, serverSeq);
            if (liveSeq.Count != serverSeq.Count)
            {
                return false;
            }

            for (var i = 0; i < liveSeq.Count; i++)
            {
                if (!string.Equals(liveSeq[i].typeName, serverSeq[i].typeName, StringComparison.Ordinal) ||
                    !string.Equals(liveSeq[i].state, serverSeq[i].state, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static void CollectContractParamStatesDepthFirst(ConfigNode node, List<(string typeName, string state)> acc)
        {
            if (node == null)
            {
                return;
            }

            if (string.Equals(node.name, "PARAM", StringComparison.OrdinalIgnoreCase))
            {
                var typeName = node.GetValue("name")?.Trim() ?? string.Empty;
                var state = node.GetValue("state")?.Trim() ?? string.Empty;
                acc.Add((typeName, state));
            }

            foreach (ConfigNode child in node.GetNodes())
            {
                CollectContractParamStatesDepthFirst(child, acc);
            }
        }

        /// <summary>
        /// When the flying client has stock-valid <c>PARAM</c> completions that the authoritative snapshot has not
        /// yet recorded (common across revision applies while orbiting), merge those scalar fields into a copy of
        /// the server contract node before <see cref="Contract.Load"/> so we do not wipe local progress. Caller
        /// then publishes <see cref="ContractIntentPayloadKind.ParameterProgressObserved"/> so the server converges.
        /// </summary>
        private static bool TryMergeLiveActiveParameterProgressAheadOfServer(
            Contract live,
            ContractSnapshotInfo serverRow,
            out ContractSnapshotInfo merged)
        {
            merged = null;
            if (live == null || serverRow == null)
            {
                return false;
            }

            if (live.ContractState != Contract.State.Active || !IsSnapshotMarkedActive(serverRow))
            {
                return false;
            }

            var liveSnap = ShareContractsMessageSender.TryBuildContractSnapshot(live);
            if (liveSnap == null)
            {
                return false;
            }

            var serverRoot = TryParseContractConfigNode(serverRow);
            var liveRoot = TryParseContractConfigNode(liveSnap);
            if (serverRoot == null || liveRoot == null)
            {
                return false;
            }

            var liveParamsByName = new Dictionary<string, ConfigNode>(StringComparer.Ordinal);
            IndexContractParamNodesByStockName(liveRoot, liveParamsByName);

            var promoted = false;
            PromoteServerParamsWhereLiveAhead(serverRoot, liveParamsByName, ref promoted);
            if (!promoted)
            {
                return false;
            }

            var data = serverRoot.Serialize();
            merged = new ContractSnapshotInfo
            {
                ContractGuid = serverRow.ContractGuid,
                ContractState = serverRow.ContractState,
                Placement = serverRow.Placement,
                Order = serverRow.Order,
                Data = data
            };
            return true;
        }

        private static void IndexContractParamNodesByStockName(ConfigNode node, Dictionary<string, ConfigNode> acc)
        {
            if (node == null)
            {
                return;
            }

            if (string.Equals(node.name, "PARAM", StringComparison.OrdinalIgnoreCase))
            {
                var typeName = node.GetValue("name")?.Trim();
                if (!string.IsNullOrEmpty(typeName))
                {
                    acc[typeName] = node;
                }
            }

            foreach (ConfigNode child in node.GetNodes())
            {
                IndexContractParamNodesByStockName(child, acc);
            }
        }

        private static void PromoteServerParamsWhereLiveAhead(
            ConfigNode node,
            Dictionary<string, ConfigNode> liveParamsByName,
            ref bool promoted)
        {
            if (node == null)
            {
                return;
            }

            if (string.Equals(node.name, "PARAM", StringComparison.OrdinalIgnoreCase))
            {
                var typeName = node.GetValue("name")?.Trim();
                if (!string.IsNullOrEmpty(typeName) &&
                    liveParamsByName.TryGetValue(typeName, out var liveParam) &&
                    IsContractParamStateComplete(liveParam) &&
                    !IsContractParamStateComplete(node))
                {
                    OverlayStockConfigNodeScalarValues(liveParam, node);
                    promoted = true;
                }
            }

            foreach (ConfigNode child in node.GetNodes())
            {
                PromoteServerParamsWhereLiveAhead(child, liveParamsByName, ref promoted);
            }
        }

        private static bool IsContractParamStateComplete(ConfigNode paramNode)
        {
            if (paramNode == null)
            {
                return false;
            }

            var st = paramNode.GetValue("state")?.Trim();
            return string.Equals(st, "Complete", StringComparison.OrdinalIgnoreCase);
        }

        private static void OverlayStockConfigNodeScalarValues(ConfigNode src, ConfigNode dest)
        {
            if (src?.values == null || dest == null)
            {
                return;
            }

            foreach (var name in src.values.DistinctNames())
            {
                var v = src.GetValue(name);
                if (v == null)
                {
                    continue;
                }

                dest.RemoveValues(name);
                dest.AddValue(name, v);
            }
        }

        /// <summary>
        /// One line per throttle window when an Active contract is rematerialized from the server snapshot because
        /// the live instance did not serialize equivalently to the incoming row (diagnoses cross-client PARAM drift).
        /// </summary>
        private static void LogActiveReuseSkippedThrottled(Guid contractGuid, string reason, string title)
        {
            var now = Time.realtimeSinceStartup;
            if (_activeReuseMismatchLogWindowStartRealtime < 0f)
            {
                _activeReuseMismatchLogWindowStartRealtime = now;
            }

            _activeReuseMismatchLogWindowCount++;
            if (_activeReuseMismatchLogWindowCount == 1)
            {
                _activeReuseMismatchLogSampleGuid = contractGuid;
                _activeReuseMismatchLogSampleReason = reason ?? string.Empty;
                _activeReuseMismatchLogSampleTitle = title ?? string.Empty;
            }

            if (now - _activeReuseMismatchLogWindowStartRealtime < ActiveReuseMismatchLogWindowSeconds)
            {
                return;
            }

            const int maxTitleLen = 80;
            var sampleTitle = _activeReuseMismatchLogSampleTitle;
            if (sampleTitle.Length > maxTitleLen)
            {
                sampleTitle = sampleTitle.Substring(0, maxTitleLen) + "...";
            }

            LunaLog.Log(
                $"[PersistentSync] Contracts: skipped Active live reuse ({_activeReuseMismatchLogWindowCount} in " +
                $"{ActiveReuseMismatchLogWindowSeconds:0.#}s); rematerializing from server snapshot. " +
                $"sample guid={_activeReuseMismatchLogSampleGuid} reason={_activeReuseMismatchLogSampleReason} title=\"{sampleTitle}\"");

            _activeReuseMismatchLogWindowStartRealtime = now;
            _activeReuseMismatchLogWindowCount = 0;
        }

        private static bool IsSnapshotStateFinishedLike(string state)
        {
            if (string.IsNullOrWhiteSpace(state)) return false;
            switch (state.Trim().ToLowerInvariant())
            {
                case "completed":
                case "failed":
                case "cancelled":
                case "deadlineexpired":
                case "declined":
                case "withdrawn":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsSnapshotMarkedActive(ContractSnapshotInfo info)
        {
            if (info == null)
            {
                return false;
            }

            if (info.Placement == ContractSnapshotPlacement.Active)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(info.ContractState) &&
                   string.Equals(info.ContractState.Trim(), nameof(Contract.State.Active), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSnapshotOrRuntimeActive(Contract contract, ContractSnapshotInfo info)
        {
            return contract != null && (contract.ContractState == Contract.State.Active || IsSnapshotMarkedActive(info));
        }

        private static bool NeedsAcceptToRestoreActiveFromSnapshot(Contract contract, ContractSnapshotInfo info)
        {
            if (contract == null || !IsSnapshotMarkedActive(info))
            {
                return false;
            }

            if (contract.IsFinished() || contract.ContractState == Contract.State.Active)
            {
                return false;
            }

            // Stock commonly deserializes an active-on-server contract as Offered until Accept() runs.
            return contract.ContractState == Contract.State.Offered;
        }

        /// <summary>
        /// Mirrors stock archive semantics: finished contracts live in <see cref="ContractSystem.ContractsFinished"/>.
        /// </summary>
        private static bool ShouldPlaceInContractsFinished(Contract contract)
        {
            if (contract == null)
            {
                return false;
            }

            if (contract.IsFinished())
            {
                return true;
            }

            // Declined / Withdrawn are not always reported as IsFinished depending on stock version, but they belong
            // in the archive list rather than the active offer pool.
            switch (contract.ContractState)
            {
                case Contract.State.Completed:
                case Contract.State.Failed:
                case Contract.State.Cancelled:
                case Contract.State.DeadlineExpired:
                case Contract.State.Declined:
                case Contract.State.Withdrawn:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Snapshot payloads should be unique by contract GUID; if duplicates appear (wire bugs or repeated intents),
        /// keep a single row per GUID so Mission Control does not list the same offer multiple times.
        /// </summary>
        private static ContractSnapshotInfo[] DedupeContractsByGuidPreserveOrder(ContractSnapshotInfo[] contracts)
        {
            if (contracts == null || contracts.Length == 0)
            {
                return Array.Empty<ContractSnapshotInfo>();
            }

            var ordered = contracts
                .Where(c => c != null && c.ContractGuid != Guid.Empty)
                .OrderBy(c => c.Order >= 0 ? c.Order : int.MaxValue)
                .ThenBy(c => c.ContractGuid);

            var seen = new HashSet<Guid>();
            var list = new List<ContractSnapshotInfo>();
            foreach (var c in ordered)
            {
                if (seen.Add(c.ContractGuid))
                {
                    list.Add(c);
                }
            }

            return list.ToArray();
        }

        private static Contract DeserializeContract(ContractSnapshotInfo contractInfo)
        {
            try
            {
                var node = TryParseContractConfigNode(contractInfo);
                if (node == null)
                {
                    return null;
                }

                var typeValue = node.GetValue("type");
                if (typeValue == null)
                {
                    return null;
                }

                node.RemoveValues("type");
                node.RemoveValues(LmpOfferTitleFieldName);
                var contractType = ContractSystem.GetContractType(typeValue);
                return Contract.Load((Contract)Activator.CreateInstance(contractType), node);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[PersistentSync] Contract materialize failed guid={contractInfo?.ContractGuid}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Server snapshots store LunaConfigNode UTF-8 text per contract. Parse through the same line-based pipeline as
        /// <see cref="ConfigNodeSerializer.DeserializeToConfigNode"/> (do not use <c>new ConfigNode(string)</c> for full cfg
        /// text — in stock KSP that constructor treats the argument as a <b>node name</b>, not serialized config, which
        /// produced no <c>type</c> value and caused every snapshot row to fail).
        /// </summary>
        private static ConfigNode TryParseContractConfigNode(ContractSnapshotInfo contractInfo)
        {
            if (contractInfo == null || contractInfo.Data == null || contractInfo.Data.Length == 0)
            {
                return null;
            }

            var raw = Encoding.UTF8.GetString(contractInfo.Data, 0, contractInfo.Data.Length);
            var node = DeserializeContractConfigText(raw);
            var resolved = FindFirstConfigNodeWithType(node);
            if (resolved != null)
            {
                return resolved;
            }

            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            if (!trimmed.StartsWith("CONTRACT", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "CONTRACT\n{\n" + trimmed + "\n}\n";
            }

            node = DeserializeContractConfigText(trimmed);
            return FindFirstConfigNodeWithType(node);
        }

        private static ConfigNode DeserializeContractConfigText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            // Luna/server templates use tabs; the reflected stock line parser is happier with spaces.
            var normalized = text.Replace("\t", "    ").TrimEnd();
            var bytes = Encoding.UTF8.GetBytes(normalized);
            return bytes.DeserializeToConfigNode(bytes.Length);
        }

        private static ConfigNode FindFirstConfigNodeWithType(ConfigNode root, int depth = 0)
        {
            if (root == null || depth > 24)
            {
                return null;
            }

            if (root.GetValue("type") != null)
            {
                return root;
            }

            foreach (ConfigNode child in root.GetNodes())
            {
                var hit = FindFirstConfigNodeWithType(child, depth + 1);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }
    }
}

