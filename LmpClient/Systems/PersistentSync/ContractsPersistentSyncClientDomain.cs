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

            ShareContractsSystem.Singleton.RefreshContractUiAdapters("PersistentSyncSnapshotApply");
            _pendingContracts = null;
            return PersistentSyncApplyOutcome.Applied;
        }

        private static void ReplaceContractsFromSnapshot(ContractSnapshotInfo[] contracts)
        {
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
