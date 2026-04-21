using System;

namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Public CLR facade for client-to-server contract commands (Accept / Decline / Cancel / RequestOfferGeneration).
    /// Commands are low-trust user intents: any client may submit them; the server validates target canonical state.
    /// Wire-level payload is produced via <see cref="ContractIntentPayloadSerializer"/>.
    /// <para>
    /// Accept additionally carries the post-Accept <see cref="ContractSnapshotInfo"/> because stock
    /// <c>Contract.Accept()</c> populates runtime-only fields (dateAccepted, dateDeadline, subclass-specific
    /// targets filled in by <c>OnAccepted</c> overrides like <c>VesselRepairContract</c>'s target vessel) that
    /// the server cannot reconstruct from the pre-Accept record alone. Without the full post-Accept node the
    /// server's stored row still has dateDeadline=0 from the Offered state, and the moment the snapshot echoes
    /// back to the accepting client, stock <c>Contract.Update()</c> sees an Active contract with
    /// <c>UT &gt; dateDeadline(0)</c> and flips it to <c>DeadlineExpired</c> in the same frame.
    /// </para>
    /// </summary>
    public sealed class ContractCommandIntent
    {
        public ContractCommandIntentKind Kind { get; }

        public Guid ContractGuid { get; }

        /// <summary>
        /// Post-Accept contract record produced from the client's live <c>Contract</c> via
        /// <c>Contract.Save</c> after stock <c>Accept()</c> finished. Only set for
        /// <see cref="ContractCommandIntentKind.Accept"/>. Servers receiving a null snapshot on Accept
        /// fall back to the legacy state-only rewrite path for backward compatibility.
        /// </summary>
        public ContractSnapshotInfo ContractSnapshot { get; }

        private ContractCommandIntent(ContractCommandIntentKind kind, Guid contractGuid, ContractSnapshotInfo contractSnapshot = null)
        {
            Kind = kind;
            ContractGuid = contractGuid;
            ContractSnapshot = contractSnapshot;
        }

        public static ContractCommandIntent Accept(Guid contractGuid, ContractSnapshotInfo postAcceptSnapshot = null) =>
            new ContractCommandIntent(ContractCommandIntentKind.Accept, contractGuid, postAcceptSnapshot);

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
                    return ContractSnapshot != null
                        ? ContractIntentPayloadSerializer.SerializeCommandWithContract(
                            ContractIntentPayloadKind.AcceptContract, ContractGuid, ContractSnapshot)
                        : ContractIntentPayloadSerializer.SerializeCommand(ContractIntentPayloadKind.AcceptContract, ContractGuid);
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
