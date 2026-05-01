using System;
using System.Linq;
using System.Text;

namespace LmpCommon.PersistentSync.Payloads.Contracts
{
    public sealed class ContractSnapshotInfo
    {
        public Guid ContractGuid;
        public string ContractState = string.Empty;
        public ContractSnapshotPlacement Placement;
        public int Order;
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
}
