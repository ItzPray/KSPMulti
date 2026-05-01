using System;
using System.Collections.Generic;
using System.Linq;

namespace LmpCommon.PersistentSync.Payloads.Contracts
{
    public sealed class ContractSnapshotChangeTracker
    {
        private readonly Dictionary<Guid, ContractSnapshotInfo> _knownByGuid = new Dictionary<Guid, ContractSnapshotInfo>();

        public int KnownCount => _knownByGuid.Count;

        public void Clear()
        {
            _knownByGuid.Clear();
        }

        public void Reset(IEnumerable<ContractSnapshotInfo> contracts)
        {
            _knownByGuid.Clear();

            foreach (var contract in contracts ?? Enumerable.Empty<ContractSnapshotInfo>())
            {
                if (contract == null || contract.ContractGuid == Guid.Empty)
                {
                    continue;
                }

                _knownByGuid[contract.ContractGuid] = ContractSnapshotInfoComparer.Clone(contract);
            }
        }

        public bool IsKnown(Guid contractGuid)
        {
            if (contractGuid == Guid.Empty) return false;
            return _knownByGuid.ContainsKey(contractGuid);
        }

        public ContractSnapshotInfo[] FilterChanged(IEnumerable<ContractSnapshotInfo> contracts)
        {
            var changedContracts = new List<ContractSnapshotInfo>();

            foreach (var contract in contracts ?? Enumerable.Empty<ContractSnapshotInfo>())
            {
                if (contract == null || contract.ContractGuid == Guid.Empty)
                {
                    continue;
                }

                var snapshot = ContractSnapshotInfoComparer.Clone(contract);
                if (_knownByGuid.TryGetValue(snapshot.ContractGuid, out var knownSnapshot) &&
                    ContractSnapshotInfoComparer.AreEquivalent(knownSnapshot, snapshot))
                {
                    continue;
                }

                _knownByGuid[snapshot.ContractGuid] = ContractSnapshotInfoComparer.Clone(snapshot);
                changedContracts.Add(snapshot);
            }

            return changedContracts.ToArray();
        }
    }
}
