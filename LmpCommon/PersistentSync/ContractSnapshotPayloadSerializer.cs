using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LmpCommon.PersistentSync
{
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
        public int NumBytes;
        public byte[] Data = new byte[0];
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
                NumBytes = safeNumBytes,
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
            if (info == null || info.Data == null || info.NumBytes <= 0)
            {
                return 0;
            }

            return Math.Min(info.NumBytes, info.Data.Length);
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

        /// <summary>
        /// True if the given <paramref name="contractGuid"/> was in the most recently-applied authoritative snapshot
        /// from the server. Lets client-side stock guards (e.g. <c>Contract.Withdraw</c> Harmony prefix) distinguish
        /// server-known offers (which must not be unilaterally dropped locally) from locally-generated offers stock
        /// just minted that the server has not yet seen.
        /// </summary>
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

    public static class ContractSnapshotPayloadSerializer
    {
        public static byte[] Serialize(IEnumerable<ContractSnapshotInfo> contracts)
        {
            return Serialize(ContractSnapshotPayloadMode.Delta, contracts);
        }

        public static byte[] Serialize(ContractSnapshotPayloadMode mode, IEnumerable<ContractSnapshotInfo> contracts)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                var orderedContracts = (contracts ?? Enumerable.Empty<ContractSnapshotInfo>()).ToArray();
                if (mode == ContractSnapshotPayloadMode.Delta)
                {
                    writer.Write(orderedContracts.Length);
                }
                else
                {
                    // Negative sentinel keeps older delta payloads readable while allowing new envelope metadata.
                    writer.Write(-1);
                    writer.Write((byte)mode);
                    writer.Write(orderedContracts.Length);
                }

                foreach (var contract in orderedContracts)
                {
                    writer.Write(contract.ContractGuid.ToByteArray());
                    writer.Write(contract.ContractState ?? string.Empty);
                    writer.Write((byte)contract.Placement);
                    writer.Write(contract.Order);
                    writer.Write(contract.NumBytes);
                    writer.Write(contract.Data ?? new byte[0], 0, contract.NumBytes);
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        public static List<ContractSnapshotInfo> Deserialize(byte[] payload, int numBytes)
        {
            return DeserializeEnvelope(payload, numBytes).Contracts;
        }

        public static ContractSnapshotPayload DeserializeEnvelope(byte[] payload, int numBytes)
        {
            using (var stream = new MemoryStream(payload, 0, numBytes))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                var firstInt = reader.ReadInt32();
                var mode = ContractSnapshotPayloadMode.Delta;
                var contractCount = firstInt;
                if (firstInt < 0)
                {
                    mode = (ContractSnapshotPayloadMode)reader.ReadByte();
                    contractCount = reader.ReadInt32();
                }

                var contracts = new List<ContractSnapshotInfo>(contractCount);
                for (var i = 0; i < contractCount; i++)
                {
                    var dataLength = 0;
                    var info = new ContractSnapshotInfo
                    {
                        ContractGuid = new Guid(reader.ReadBytes(16)),
                        ContractState = reader.ReadString(),
                        Placement = (ContractSnapshotPlacement)reader.ReadByte(),
                        Order = reader.ReadInt32()
                    };

                    dataLength = reader.ReadInt32();
                    info.NumBytes = dataLength;
                    info.Data = dataLength > 0 ? reader.ReadBytes(dataLength) : new byte[0];
                    contracts.Add(info);
                }

                return new ContractSnapshotPayload
                {
                    Mode = mode,
                    Contracts = contracts
                };
            }
        }
    }

    public sealed class ContractSnapshotPayload
    {
        public ContractSnapshotPayloadMode Mode = ContractSnapshotPayloadMode.Delta;
        public List<ContractSnapshotInfo> Contracts = new List<ContractSnapshotInfo>();
    }
}
