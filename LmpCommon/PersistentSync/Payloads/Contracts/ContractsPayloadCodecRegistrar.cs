using LmpCommon.PersistentSync;

namespace LmpCommon.PersistentSync.Payloads.Contracts
{
    /// <summary>
    /// Registers contract envelope codecs for <see cref="PersistentSyncPayloadSerializer"/>.
    /// </summary>
    public sealed class ContractsPayloadCodecRegistrar : IPersistentSyncPayloadCodecRegistrar
    {
        public void Register(PersistentSyncPayloadCodecRegistry registry)
        {
            registry.RegisterCustom<ContractIntentPayload>(
                PersistentSyncContractPayloadCodec.ReadContractIntentPayload,
                PersistentSyncContractPayloadCodec.WriteContractIntentPayload);
            registry.RegisterCustom<ContractSnapshotPayload>(
                PersistentSyncContractPayloadCodec.ReadContractSnapshotPayload,
                PersistentSyncContractPayloadCodec.WriteContractSnapshotPayload);
            registry.RegisterCustom<ContractsPayload>(
                PersistentSyncContractPayloadCodec.ReadContractsPayload,
                PersistentSyncContractPayloadCodec.WriteContractsPayload);
        }
    }
}
