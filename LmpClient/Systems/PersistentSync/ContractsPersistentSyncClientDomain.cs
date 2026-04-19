using Contracts;
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
                ShareContractsSystem.LogMcUiContractInventory("PersistentSyncSnapshotApply:afterReplaceContractsFromSnapshot");
                // Keep ShareContractsSystem ignoring events through UI refresh. RefreshContractUiAdapters uses
                // psApply-safe paths (no RefreshContracts / contract GameEvents) while rebuilding ContractsApp and
                // Mission Control lists from the already-replaced ContractSystem model.
                ShareContractsSystem.Singleton.RefreshContractUiAdapters("PersistentSyncSnapshotApply");
                ShareContractsSystem.Singleton.CancelPendingControlledStockContractRefresh("PersistentSyncSnapshotApply:PostUi");
                ShareContractsSystem.Singleton.NotifyAuthoritativeContractsSnapshotApplied("PersistentSyncSnapshotApply");
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

            foreach (var contractInfo in contracts ?? Array.Empty<ContractSnapshotInfo>())
            {
                var contract = DeserializeContract(contractInfo);
                if (contract == null)
                {
                    continue;
                }

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
                }
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
            var node = contractInfo.Data.DeserializeToConfigNode(contractInfo.NumBytes);
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
    }
}
