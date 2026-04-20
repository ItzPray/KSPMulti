using Contracts;
using HarmonyLib;
using LmpClient.Extensions;
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

namespace LmpClient.Systems.PersistentSync
{
    public class ContractsPersistentSyncClientDomain : IPersistentSyncClientDomain
    {
        private const string LmpOfferTitleFieldName = "lmpOfferTitle";

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

        /// <summary>
        /// Pre-populates the Contracts <see cref="ProtoScenarioModule"/> <c>moduleValues</c> directly from the
        /// currently pending server snapshot, so stock KSP's <c>ContractSystem.OnLoadRoutine</c> (triggered by
        /// <see cref="HighLogic.CurrentGame"/>.Start()) reads server-authoritative data instead of an empty
        /// template. Must be called after <see cref="ScenarioSystem.LoadMissingScenarioDataIntoGame"/> and BEFORE
        /// <c>HighLogic.CurrentGame.Start()</c>.
        /// <para/>
        /// With only the <see cref="FlushPendingState"/> deferral in place, stock's OnLoadRoutine still runs once
        /// against empty <c>moduleValues</c> before we get a chance to apply; Mission Control is empty until the
        /// deferred snapshot is retried by <c>OnSceneReady</c>. This method closes that window by letting stock
        /// do the initial load with our data.
        /// </summary>
        public bool TryPrePopulateProtoFromPendingSnapshot(string reason)
        {
            try
            {
                if (_pendingContracts == null || _pendingContracts.Length == 0)
                {
                    return false;
                }

                if (HighLogic.CurrentGame?.scenarios == null)
                {
                    return false;
                }

                var proto = HighLogic.CurrentGame.scenarios
                    .FirstOrDefault(s => s != null && s.moduleName == "ContractSystem");
                if (proto == null)
                {
                    LunaLog.LogWarning(
                        $"[PersistentSync] Contracts proto pre-populate skipped reason={reason}: no ContractSystem ProtoScenarioModule");
                    return false;
                }

                var moduleValues = new ConfigNode();
                // Stock OnLoad tolerates a missing WEIGHTS node (LoadContractWeights null-checks each lookup).
                var contractsContainer = moduleValues.AddNode("CONTRACTS");

                var dedupedContracts = DedupeContractsByGuidPreserveOrder(_pendingContracts);
                var offerCount = 0;
                var finishedCount = 0;
                var parseFailed = 0;
                foreach (var info in dedupedContracts)
                {
                    var contractNode = TryParseContractConfigNode(info);
                    if (contractNode == null)
                    {
                        parseFailed++;
                        continue;
                    }

                    var placement = ShouldPlaceInContractsFinishedFromSnapshot(contractNode, info);
                    var childName = placement ? "CONTRACT_FINISHED" : "CONTRACT";
                    var child = contractsContainer.AddNode(childName);
                    contractNode.CopyTo(child);
                    if (placement) finishedCount++; else offerCount++;
                }

                moduleValues.AddValue("update", "0");
                moduleValues.AddValue("version", "-1");

                Traverse.Create(proto).Field<ConfigNode>("moduleValues").Value = moduleValues;

                LunaLog.Log(
                    $"[PersistentSync] Contracts proto pre-populated reason={reason} " +
                    $"offerNodes={offerCount} finishedNodes={finishedCount} parseFailed={parseFailed} wireRows={_pendingContracts.Length}");
                return true;
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[PersistentSync] Contracts proto pre-populate failed reason={reason}: {e}");
                return false;
            }
        }

        /// <summary>
        /// Mirrors <see cref="ShouldPlaceInContractsFinished"/> but operates on the raw snapshot ConfigNode + info
        /// (no live Contract instance yet). Decides whether this contract should appear under CONTRACT_FINISHED
        /// (archive) or CONTRACT (main list) inside <c>ProtoScenarioModule.moduleValues</c>.
        /// </summary>
        private static bool ShouldPlaceInContractsFinishedFromSnapshot(ConfigNode contractNode, ContractSnapshotInfo info)
        {
            if (info != null && info.Placement == ContractSnapshotPlacement.Finished)
            {
                return true;
            }

            var stateValue = contractNode?.GetValue("state");
            if (string.IsNullOrEmpty(stateValue))
            {
                stateValue = info?.ContractState;
            }

            if (string.IsNullOrEmpty(stateValue))
            {
                return false;
            }

            if (!Enum.TryParse(stateValue.Trim(), ignoreCase: true, out Contract.State state))
            {
                return false;
            }

            switch (state)
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

            // ContractSystem.OnLoadRoutine is a coroutine that yields one frame, then clears the live `contracts`
            // list early, then repopulates from the gameNode captured at OnLoad time. If we populate
            // ContractSystem.Instance.Contracts BEFORE that routine finishes, the routine will clear our work when
            // it resumes. Stock sets ContractSystem.loaded=true at the end of OnLoadRoutine and =false in OnAwake;
            // deferring until loaded==true guarantees our snapshot survives scene transitions.
            if (!ContractSystem.loaded)
            {
                LunaLog.Log(
                    "[PersistentSync] Contracts snapshot deferred: ContractSystem.loaded=false (OnLoadRoutine has " +
                    "not finished; applying now would be wiped when the coroutine resumes and clears the list)");
                return PersistentSyncApplyOutcome.Deferred;
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

                ReplaceContractsFromSnapshot(_pendingContracts);
                ShareContractsSystem.Singleton.MessageSender.ResetKnownContractSnapshots(_pendingContracts);
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

            foreach (var contract in ContractSystem.Instance.Contracts.ToArray())
            {
                contract.Unregister();
            }

            foreach (var contract in ContractSystem.Instance.ContractsFinished.ToArray())
            {
                contract.Unregister();
            }

            ContractSystem.Instance.Contracts.Clear();
            ContractSystem.Instance.ContractsFinished.Clear();

            var wireRows = contracts?.Length ?? 0;
            var materialized = 0;
            var skippedNull = 0;
            foreach (var contractInfo in contracts ?? Array.Empty<ContractSnapshotInfo>())
            {
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
                    ContractSystem.Instance.ContractsFinished.Add(contract);
                }
                // Active: trust snapshot metadata when Contract.Load still leaves Offered/Available — otherwise MC
                // lists active contracts under "available" until some unrelated Accept forces a full refresh.
                else if (IsSnapshotOrRuntimeActive(contract, contractInfo))
                {
                    ContractSystem.Instance.Contracts.Add(contract);
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
                    ContractSystem.Instance.Contracts.Add(contract);
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

            if (wireRows > 0 && materialized == 0)
            {
                LunaLog.LogError(
                    $"[PersistentSync] Contracts snapshot had {wireRows} wire rows but none deserialized into Contract objects " +
                    $"(skippedNull={skippedNull}). First guid={(contracts != null && contracts.Length > 0 ? contracts[0].ContractGuid.ToString() : "n/a")}");
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
