using System;

namespace LmpCommon.PersistentSync.Payloads.Contracts
{
    public sealed class ContractIntentPayload
    {
        public ContractIntentPayloadKind Kind;
        public Guid ContractGuid = Guid.Empty;
        public ContractSnapshotInfo Contract;
        public ContractSnapshotInfo[] Contracts = new ContractSnapshotInfo[0];
    }
}
