using LmpCommon.Message.Base;
using LmpCommon.Message.Types;

namespace LmpCommon.Message.Data.PersistentSync
{
    /// <summary>
    /// Signal from server to the contract-lock holder: run stock offer replenishment (controlled RefreshContracts).
    /// Payload-free by design — canonical contracts state did not change (revision unchanged).
    /// </summary>
    public class PersistentSyncProducerOfferGenerationNudgeMsgData : PersistentSyncBaseMsgData
    {
        internal PersistentSyncProducerOfferGenerationNudgeMsgData() { }

        public override LmpCommon.Message.Types.PersistentSyncMessageType PersistentSyncMessageType =>
            LmpCommon.Message.Types.PersistentSyncMessageType.ProducerOfferGenerationNudge;

        public override string ClassName { get; } = nameof(PersistentSyncProducerOfferGenerationNudgeMsgData);
    }
}
