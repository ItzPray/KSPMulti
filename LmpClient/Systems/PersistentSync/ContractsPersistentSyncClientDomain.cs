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
                ReplaceContractsFromSnapshot(_pendingContracts);
                // Keep ShareContractsSystem ignoring events through UI refresh. RefreshContractUiAdapters skips
                // ContractsApp/MissionControl for this source (those stock paths can spawn offers). We also avoid
                // onContractsLoaded / RefreshContracts here — they regenerate default offers on top of server truth.
                ShareContractsSystem.Singleton.RefreshContractUiAdapters("PersistentSyncSnapshotApply");
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

                switch (contractInfo.Placement)
                {
                    case ContractSnapshotPlacement.Finished:
                        contract.Unregister();
                        ContractSystem.Instance.ContractsFinished.Add(contract);
                        break;
                    case ContractSnapshotPlacement.Active:
                        ContractSystem.Instance.Contracts.Add(contract);
                        contract.Register();
                        break;
                    default:
                        ContractSystem.Instance.Contracts.Add(contract);
                        break;
                }
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
            var contractType = ContractSystem.GetContractType(typeValue);
            return Contract.Load((Contract)Activator.CreateInstance(contractType), node);
        }
    }
}
