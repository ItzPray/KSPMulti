using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LmpCommon.PersistentSync
{
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

    public static class ContractSnapshotPayloadSerializer
    {
        public static byte[] Serialize(IEnumerable<ContractSnapshotInfo> contracts)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                var orderedContracts = (contracts ?? Enumerable.Empty<ContractSnapshotInfo>()).ToArray();
                writer.Write(orderedContracts.Length);
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
            using (var stream = new MemoryStream(payload, 0, numBytes))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                var contractCount = reader.ReadInt32();
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

                return contracts;
            }
        }
    }
}
