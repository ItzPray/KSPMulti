using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LmpCommon.PersistentSync.Payloads.Contracts
{
    public enum ContractIntentPayloadKind : byte
    {
        AcceptContract = 0,
        DeclineContract = 1,
        CancelContract = 2,
        RequestOfferGeneration = 3,
        OfferObserved = 4,
        ParameterProgressObserved = 5,
        ContractCompletedObserved = 6,
        ContractFailedObserved = 7,
        FullReconcile = 8
    }

    public sealed class ContractIntentPayload
    {
        public ContractIntentPayloadKind Kind;
        public Guid ContractGuid = Guid.Empty;
        public ContractSnapshotInfo Contract;
        public ContractSnapshotInfo[] Contracts = new ContractSnapshotInfo[0];
    }

    public enum ContractSnapshotPayloadMode : byte
    {
        Delta = 0,
        FullReplace = 1
    }

    public enum ContractSnapshotPlacement : byte
    {
        Current = 0,
        Active = 1,
        Finished = 2
    }

    public sealed class ContractSnapshotInfo
    {
        public Guid ContractGuid;
        public string ContractState = string.Empty;
        public ContractSnapshotPlacement Placement;
        public int Order;
        public byte[] Data = new byte[0];
    }

    public sealed class ContractSnapshotPayload
    {
        public ContractSnapshotPayloadMode Mode = ContractSnapshotPayloadMode.Delta;
        public List<ContractSnapshotInfo> Contracts = new List<ContractSnapshotInfo>();
    }

    public sealed class ContractsPayload
    {
        public ContractIntentPayload Intent;
        public ContractSnapshotPayload Snapshot;

        public bool IsIntent => Intent != null;
    }

    public static class ContractSnapshotInfoComparer
    {
        public static bool AreEquivalent(ContractSnapshotInfo left, ContractSnapshotInfo right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return left.ContractGuid == right.ContractGuid &&
                   string.Equals(left.ContractState ?? string.Empty, right.ContractState ?? string.Empty, StringComparison.Ordinal) &&
                   left.Placement == right.Placement &&
                   string.Equals(NormalizeContractText(left), NormalizeContractText(right), StringComparison.Ordinal);
        }

        public static ContractSnapshotInfo Clone(ContractSnapshotInfo source)
        {
            if (source == null)
            {
                return null;
            }

            var safeNumBytes = GetSafeNumBytes(source);
            var data = new byte[safeNumBytes];
            if (safeNumBytes > 0)
            {
                Buffer.BlockCopy(source.Data, 0, data, 0, safeNumBytes);
            }

            return new ContractSnapshotInfo
            {
                ContractGuid = source.ContractGuid,
                ContractState = source.ContractState ?? string.Empty,
                Placement = source.Placement,
                Order = source.Order,
                Data = data
            };
        }

        private static string NormalizeContractText(ContractSnapshotInfo info)
        {
            return new string(Encoding.UTF8.GetString(info.Data ?? new byte[0], 0, GetSafeNumBytes(info))
                .Where(c => !char.IsWhiteSpace(c))
                .ToArray());
        }

        private static int GetSafeNumBytes(ContractSnapshotInfo info)
        {
            if (info == null || info.Data == null || info.Data.Length <= 0)
            {
                return 0;
            }

            return info.Data.Length;
        }
    }

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
