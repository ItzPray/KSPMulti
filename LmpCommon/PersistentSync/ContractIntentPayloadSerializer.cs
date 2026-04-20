using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LmpCommon.PersistentSync
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
        public ContractSnapshotInfo[] Contracts = Array.Empty<ContractSnapshotInfo>();
    }

    public static class ContractIntentPayloadSerializer
    {
        private const int Sentinel = unchecked((int)0x434E5452); // CNTR
        private const byte Version = 1;

        public static byte[] SerializeCommand(ContractIntentPayloadKind kind, Guid contractGuid)
        {
            return Serialize(new ContractIntentPayload
            {
                Kind = kind,
                ContractGuid = contractGuid
            });
        }

        public static byte[] SerializeRequestOfferGeneration()
        {
            return Serialize(new ContractIntentPayload
            {
                Kind = ContractIntentPayloadKind.RequestOfferGeneration
            });
        }

        public static byte[] SerializeProposal(ContractIntentPayloadKind kind, ContractSnapshotInfo contract)
        {
            return Serialize(new ContractIntentPayload
            {
                Kind = kind,
                ContractGuid = contract?.ContractGuid ?? Guid.Empty,
                Contract = ContractSnapshotInfoComparer.Clone(contract)
            });
        }

        public static byte[] SerializeFullReconcile(IEnumerable<ContractSnapshotInfo> contracts)
        {
            return Serialize(new ContractIntentPayload
            {
                Kind = ContractIntentPayloadKind.FullReconcile,
                Contracts = (contracts ?? Enumerable.Empty<ContractSnapshotInfo>())
                    .Select(ContractSnapshotInfoComparer.Clone)
                    .ToArray()
            });
        }

        public static byte[] Serialize(ContractIntentPayload payload)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                var safePayload = payload ?? new ContractIntentPayload();
                writer.Write(Sentinel);
                writer.Write(Version);
                writer.Write((byte)safePayload.Kind);
                writer.Write(safePayload.ContractGuid != Guid.Empty);
                if (safePayload.ContractGuid != Guid.Empty)
                {
                    writer.Write(safePayload.ContractGuid.ToByteArray());
                }

                writer.Write(safePayload.Contract != null);
                if (safePayload.Contract != null)
                {
                    WriteContractSnapshotInfo(writer, safePayload.Contract);
                }

                var contracts = safePayload.Contracts ?? Array.Empty<ContractSnapshotInfo>();
                writer.Write(contracts.Length);
                foreach (var contract in contracts)
                {
                    WriteContractSnapshotInfo(writer, contract);
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        public static ContractIntentPayload Deserialize(byte[] payload, int numBytes)
        {
            using (var stream = new MemoryStream(payload, 0, numBytes))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.ReadInt32() != Sentinel)
                {
                    throw new InvalidDataException("Contracts intent payload sentinel mismatch.");
                }

                var version = reader.ReadByte();
                if (version != Version)
                {
                    throw new InvalidDataException("Contracts intent payload version mismatch.");
                }

                var result = new ContractIntentPayload
                {
                    Kind = (ContractIntentPayloadKind)reader.ReadByte()
                };

                if (reader.ReadBoolean())
                {
                    result.ContractGuid = new Guid(reader.ReadBytes(16));
                }

                if (reader.ReadBoolean())
                {
                    result.Contract = ReadContractSnapshotInfo(reader);
                }

                var contractCount = reader.ReadInt32();
                if (contractCount > 0)
                {
                    result.Contracts = new ContractSnapshotInfo[contractCount];
                    for (var i = 0; i < contractCount; i++)
                    {
                        result.Contracts[i] = ReadContractSnapshotInfo(reader);
                    }
                }

                return result;
            }
        }

        private static void WriteContractSnapshotInfo(BinaryWriter writer, ContractSnapshotInfo contract)
        {
            var safeContract = ContractSnapshotInfoComparer.Clone(contract) ?? new ContractSnapshotInfo();
            var data = safeContract.Data ?? Array.Empty<byte>();
            var numBytes = safeContract.NumBytes;
            if (numBytes < 0)
            {
                numBytes = 0;
            }

            if (numBytes > data.Length)
            {
                numBytes = data.Length;
            }

            writer.Write(safeContract.ContractGuid.ToByteArray());
            writer.Write(safeContract.ContractState ?? string.Empty);
            writer.Write((byte)safeContract.Placement);
            writer.Write(safeContract.Order);
            writer.Write(numBytes);
            writer.Write(data, 0, numBytes);
        }

        private static ContractSnapshotInfo ReadContractSnapshotInfo(BinaryReader reader)
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
            info.Data = dataLength > 0 ? reader.ReadBytes(dataLength) : Array.Empty<byte>();
            return info;
        }
    }
}
