using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LmpCommon.PersistentSync
{
    internal static class PersistentSyncContractPayloadCodec
    {
        private const int ContractIntentSentinel = unchecked((int)0x434E5452); // CNTR
        private const byte ContractIntentVersion = 1;

        internal static ContractSnapshotPayload ReadContractSnapshotPayload(PersistentSyncPayloadReader reader)
        {
            var firstInt = reader.ReadInt32();
            return ReadContractSnapshotPayload(reader, firstInt);
        }

        internal static ContractSnapshotPayload ReadContractSnapshotPayload(PersistentSyncPayloadReader reader, int firstInt)
        {
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
                contracts.Add(ReadContractSnapshotRecord(reader));
            }

            return new ContractSnapshotPayload
            {
                Mode = mode,
                Contracts = contracts
            };
        }

        internal static void WriteContractSnapshotPayload(PersistentSyncPayloadWriter writer, ContractSnapshotPayload payload)
        {
            var safePayload = payload ?? new ContractSnapshotPayload();
            var contracts = safePayload.Contracts ?? new List<ContractSnapshotInfo>();
            if (safePayload.Mode == ContractSnapshotPayloadMode.Delta)
            {
                writer.WriteInt32(contracts.Count);
            }
            else
            {
                // Negative sentinel keeps older delta payloads readable while allowing envelope metadata.
                writer.WriteInt32(-1);
                writer.WriteByte((byte)safePayload.Mode);
                writer.WriteInt32(contracts.Count);
            }

            foreach (var contract in contracts)
            {
                WriteContractSnapshotRecord(writer, contract ?? new ContractSnapshotInfo());
            }
        }

        internal static ContractIntentPayload ReadContractIntentPayload(PersistentSyncPayloadReader reader)
        {
            if (reader.ReadInt32() != ContractIntentSentinel)
            {
                throw new InvalidDataException("Contracts intent payload sentinel mismatch.");
            }

            var version = reader.ReadByte();
            if (version != ContractIntentVersion)
            {
                throw new InvalidDataException("Contracts intent payload version mismatch.");
            }

            var result = new ContractIntentPayload
            {
                Kind = (ContractIntentPayloadKind)reader.ReadByte()
            };

            if (reader.ReadBoolean())
            {
                result.ContractGuid = reader.ReadGuid();
            }

            if (reader.ReadBoolean())
            {
                result.Contract = ReadContractSnapshotRecord(reader);
            }

            var contractCount = reader.ReadInt32();
            if (contractCount > 0)
            {
                result.Contracts = new ContractSnapshotInfo[contractCount];
                for (var i = 0; i < contractCount; i++)
                {
                    result.Contracts[i] = ReadContractSnapshotRecord(reader);
                }
            }

            return result;
        }

        internal static ContractsPayload ReadContractsPayload(PersistentSyncPayloadReader reader)
        {
            var firstInt = reader.ReadInt32();
            if (firstInt == ContractIntentSentinel)
            {
                return new ContractsPayload
                {
                    Intent = ReadContractIntentPayloadAfterSentinel(reader)
                };
            }

            return new ContractsPayload
            {
                Snapshot = ReadContractSnapshotPayload(reader, firstInt)
            };
        }

        internal static void WriteContractsPayload(PersistentSyncPayloadWriter writer, ContractsPayload payload)
        {
            if (payload?.Intent != null)
            {
                WriteContractIntentPayload(writer, payload.Intent);
                return;
            }

            WriteContractSnapshotPayload(writer, payload?.Snapshot ?? new ContractSnapshotPayload());
        }

        internal static void WriteContractIntentPayload(PersistentSyncPayloadWriter writer, ContractIntentPayload payload)
        {
            var safePayload = payload ?? new ContractIntentPayload();
            writer.WriteInt32(ContractIntentSentinel);
            writer.WriteByte(ContractIntentVersion);
            writer.WriteByte((byte)safePayload.Kind);
            writer.WriteBoolean(safePayload.ContractGuid != Guid.Empty);
            if (safePayload.ContractGuid != Guid.Empty)
            {
                writer.WriteGuid(safePayload.ContractGuid);
            }

            writer.WriteBoolean(safePayload.Contract != null);
            if (safePayload.Contract != null)
            {
                WriteContractSnapshotRecord(writer, safePayload.Contract);
            }

            var contracts = safePayload.Contracts ?? new ContractSnapshotInfo[0];
            writer.WriteInt32(contracts.Length);
            foreach (var contract in contracts)
            {
                WriteContractSnapshotRecord(writer, contract ?? new ContractSnapshotInfo());
            }
        }

        private static ContractIntentPayload ReadContractIntentPayloadAfterSentinel(PersistentSyncPayloadReader reader)
        {
            var version = reader.ReadByte();
            if (version != ContractIntentVersion)
            {
                throw new InvalidDataException("Contracts intent payload version mismatch.");
            }

            var result = new ContractIntentPayload
            {
                Kind = (ContractIntentPayloadKind)reader.ReadByte()
            };

            if (reader.ReadBoolean())
            {
                result.ContractGuid = reader.ReadGuid();
            }

            if (reader.ReadBoolean())
            {
                result.Contract = ReadContractSnapshotRecord(reader);
            }

            var contractCount = reader.ReadInt32();
            if (contractCount > 0)
            {
                result.Contracts = new ContractSnapshotInfo[contractCount];
                for (var i = 0; i < contractCount; i++)
                {
                    result.Contracts[i] = ReadContractSnapshotRecord(reader);
                }
            }

            return result;
        }

        private static ContractSnapshotInfo ReadContractSnapshotRecord(PersistentSyncPayloadReader reader)
        {
            return new ContractSnapshotInfo
            {
                ContractGuid = reader.ReadGuid(),
                ContractState = reader.ReadString(),
                Placement = (ContractSnapshotPlacement)reader.ReadByte(),
                Order = reader.ReadInt32(),
                Data = reader.ReadBytesWithLength(out _)
            };
        }

        private static void WriteContractSnapshotRecord(PersistentSyncPayloadWriter writer, ContractSnapshotInfo contract)
        {
            var safeContract = contract ?? new ContractSnapshotInfo();
            writer.WriteGuid(safeContract.ContractGuid);
            writer.WriteString(safeContract.ContractState);
            writer.WriteByte((byte)safeContract.Placement);
            writer.WriteInt32(safeContract.Order);
            writer.WriteBytes(safeContract.Data, safeContract.Data?.Length ?? 0);
        }
    }
}
