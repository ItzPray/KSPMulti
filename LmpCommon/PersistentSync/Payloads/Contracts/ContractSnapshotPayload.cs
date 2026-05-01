using System.Collections.Generic;

namespace LmpCommon.PersistentSync.Payloads.Contracts
{
    public sealed class ContractSnapshotPayload
    {
        public ContractSnapshotPayloadMode Mode = ContractSnapshotPayloadMode.Delta;
        public List<ContractSnapshotInfo> Contracts = new List<ContractSnapshotInfo>();
    }
}
