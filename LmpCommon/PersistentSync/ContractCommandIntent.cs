using System;

namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Public CLR facade for client-to-server contract commands (Accept / Decline / Cancel / RequestOfferGeneration).
    /// Commands are low-trust user intents: any client may submit them; the server validates target canonical state.
    /// Wire-level payload is produced via <see cref="ContractIntentPayloadSerializer"/>.
    /// </summary>
    public sealed class ContractCommandIntent
    {
        public ContractCommandIntentKind Kind { get; }

        public Guid ContractGuid { get; }

        private ContractCommandIntent(ContractCommandIntentKind kind, Guid contractGuid)
        {
            Kind = kind;
            ContractGuid = contractGuid;
        }

        public static ContractCommandIntent Accept(Guid contractGuid) =>
            new ContractCommandIntent(ContractCommandIntentKind.Accept, contractGuid);

        public static ContractCommandIntent Decline(Guid contractGuid) =>
            new ContractCommandIntent(ContractCommandIntentKind.Decline, contractGuid);

        public static ContractCommandIntent Cancel(Guid contractGuid) =>
            new ContractCommandIntent(ContractCommandIntentKind.Cancel, contractGuid);

        public static ContractCommandIntent RequestOfferGeneration() =>
            new ContractCommandIntent(ContractCommandIntentKind.RequestOfferGeneration, Guid.Empty);

        public byte[] Serialize()
        {
            switch (Kind)
            {
                case ContractCommandIntentKind.Accept:
                    return ContractIntentPayloadSerializer.SerializeCommand(ContractIntentPayloadKind.AcceptContract, ContractGuid);
                case ContractCommandIntentKind.Decline:
                    return ContractIntentPayloadSerializer.SerializeCommand(ContractIntentPayloadKind.DeclineContract, ContractGuid);
                case ContractCommandIntentKind.Cancel:
                    return ContractIntentPayloadSerializer.SerializeCommand(ContractIntentPayloadKind.CancelContract, ContractGuid);
                case ContractCommandIntentKind.RequestOfferGeneration:
                    return ContractIntentPayloadSerializer.SerializeRequestOfferGeneration();
                default:
                    throw new InvalidOperationException($"Unknown ContractCommandIntentKind: {Kind}");
            }
        }
    }

    public enum ContractCommandIntentKind : byte
    {
        Accept = 0,
        Decline = 1,
        Cancel = 2,
        RequestOfferGeneration = 3
    }
}
