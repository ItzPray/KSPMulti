namespace LmpCommon.PersistentSync.Payloads.Contracts
{
    public sealed class ContractsPayload
    {
        public ContractIntentPayload Intent;
        public ContractSnapshotPayload Snapshot;

        public bool IsIntent => Intent != null;
    }
}
