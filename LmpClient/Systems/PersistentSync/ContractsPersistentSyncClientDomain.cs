using Contracts;
using LmpClient.Extensions;
using LmpClient.Harmony;
using LmpClient.Systems.ShareContracts;
using LmpClient.Systems.ShareExperimentalParts;
using LmpClient.Systems.ShareFunds;
using LmpClient.Systems.ShareReputation;
using LmpClient.Systems.ShareScience;
using LmpCommon.PersistentSync;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LmpClient.Systems.PersistentSync
{
    public class ContractsPersistentSyncClientDomain : IPersistentSyncClientDomain
    {
        private const string LmpOfferTitleFieldName = "lmpOfferTitle";

        /// <summary>
        /// Number of Unity frames to wait after an observed stock <c>ContractSystem.OnLoad</c> call before we
        /// apply a PersistentSync snapshot. Stock <c>OnLoadRoutine</c> yields exactly one frame, then runs to
        /// completion (clearing <c>ContractSystem.Instance.Contracts</c> and repopulating from the captured
        /// gameNode). If our apply lands inside that window, the coroutine's resume wipes it; if we wait past
        /// the window the coroutine is guaranteed done and apply is safe.
        /// A small safety margin (more than the single observed yield) absorbs frame-skipping under load.
        /// </summary>
        private const int OnLoadCoroutineCompletionWindowFrames = 4;

        private ContractSnapshotInfo[] _pendingContracts;

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.Contracts;

        public PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot)
        {
            try
            {
                _pendingContracts = ContractSnapshotPayloadSerializer.Deserialize(snapshot.Payload, snapshot.NumBytes)
                    .OrderBy(contract => contract.Order)
                    .ToArray();
                LunaLog.Log(
                    $"[PersistentSync] Contracts snapshot received wireRows={_pendingContracts.Length} payloadBytes={snapshot.NumBytes}");
            }
            catch (Exception)
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            return FlushPendingState();
        }

        public PersistentSyncApplyOutcome FlushPendingState()
        {
            if (_pendingContracts == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (ContractSystem.Instance == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            // Frame-delta gate anchored at the last observed ContractSystem.OnLoad. Stock OnLoadRoutine is a
            // coroutine that yields one frame, then runs to completion (it clears ContractSystem.Instance.Contracts
            // before repopulating from the gameNode captured at OnLoad time). Applying between the yield and
            // the clear would be wiped; applying once the completion window has elapsed is safe. The legacy
            // gate on static ContractSystem.loaded was fundamentally unreliable — stock's OnAwake resets it to
            // false on every scenario-runner rebuild, and the coroutine only sets it to true on the non-early-exit
            // path, so the gate could stay false permanently while ContractSystem.Instance was fully alive.
            // If OnLoad has never been observed this session, LastOnLoadFrame sits at int.MinValue/2 so the delta
            // is always large and apply proceeds immediately (no coroutine to race).
            if (ContractSystem_OnLoad_EnsureContractsNode.HasObservedOnLoad)
            {
                var framesSinceOnLoad = Time.frameCount - ContractSystem_OnLoad_EnsureContractsNode.LastOnLoadFrame;
                if (framesSinceOnLoad >= 0 && framesSinceOnLoad < OnLoadCoroutineCompletionWindowFrames)
                {
                    LunaLog.Log(
                        $"[PersistentSync] Contracts snapshot deferred: OnLoad ran {framesSinceOnLoad} frame(s) ago " +
                        $"(completion window={OnLoadCoroutineCompletionWindowFrames} frames); waiting for OnLoadRoutine " +
                        "coroutine to finish clearing+repopulating the list");
                    return PersistentSyncApplyOutcome.Deferred;
                }
            }

            ShareContractsSystem.Singleton.StartIgnoringEvents();
            ShareFundsSystem.Singleton.StartIgnoringEvents();
            ShareScienceSystem.Singleton.StartIgnoringEvents();
            ShareReputationSystem.Singleton.StartIgnoringEvents();
            ShareExperimentalPartsSystem.Singleton.StartIgnoringEvents();

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
                // Stock RefreshContracts reloads from HighLogic.CurrentGame.scenarios ContractSystem proto. We mutate
                // ContractSystem.Instance lists only — without mirroring here, a later RefreshContracts (subspace lock,
                // Mission Control, etc.) rebuilds from stale proto and wipes offers while keeping ContractsFinished.
                PersistentSyncScenarioProtoMaterializer.TryMirrorScenarioModule(
                    ContractSystem.Instance,
                    "ContractSystem",
                    "PersistentSyncSnapshotApply:Contracts");

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

            _pendingContracts = null;
            return PersistentSyncApplyOutcome.Applied;
        }

        private static void ReplaceContractsFromSnapshot(ContractSnapshotInfo[] contracts)
        {
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

                // State mismatch or GUID not live: drop the previous live subscription (if any) and re-materialize
                // from the snapshot so Accept/Decline/Complete/Fail transitions propagate to stock KSP's scorers.
                if (liveContract != null)
                {
                    try { liveContract.Unregister(); }
                    catch (Exception e) { LunaLog.LogWarning($"[PersistentSync] contract Unregister before re-materialize failed guid={liveContract.ContractGuid}: {e.Message}"); }
                }

                var contract = DeserializeContract(contractInfo);
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
                    newMain.Add(live);
                    return true;
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
            if (contractInfo == null || contractInfo.NumBytes <= 0 || contractInfo.Data == null || contractInfo.Data.Length == 0)
            {
                return null;
            }

            var raw = Encoding.UTF8.GetString(contractInfo.Data, 0, contractInfo.NumBytes);
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
