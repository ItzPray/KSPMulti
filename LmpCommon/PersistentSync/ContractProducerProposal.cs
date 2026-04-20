using System;
using System.Collections.Generic;

namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Public CLR facade for high-trust producer-only proposals emitted by the current contract-lock owner.
    /// Proposals describe stock ContractSystem observations (new offer, parameter progress, completed/failed) that the
    /// server reduces into canonical state. A full-reconcile proposal republishes the producer's entire contract model
    /// on explicit authority handoff.
    /// </summary>
    public sealed class ContractProducerProposal
    {
        public ContractProducerProposalKind Kind { get; }

        public ContractSnapshotInfo Contract { get; }

        public IReadOnlyList<ContractSnapshotInfo> Contracts { get; }

        private ContractProducerProposal(
            ContractProducerProposalKind kind,
            ContractSnapshotInfo contract,
            IReadOnlyList<ContractSnapshotInfo> contracts)
        {
            Kind = kind;
            Contract = contract;
            Contracts = contracts ?? Array.Empty<ContractSnapshotInfo>();
        }

        public static ContractProducerProposal OfferObserved(ContractSnapshotInfo contract) =>
            new ContractProducerProposal(ContractProducerProposalKind.OfferObserved, contract, null);

        public static ContractProducerProposal ParameterProgressObserved(ContractSnapshotInfo contract) =>
            new ContractProducerProposal(ContractProducerProposalKind.ParameterProgressObserved, contract, null);

        public static ContractProducerProposal CompletedObserved(ContractSnapshotInfo contract) =>
            new ContractProducerProposal(ContractProducerProposalKind.CompletedObserved, contract, null);

        public static ContractProducerProposal FailedObserved(ContractSnapshotInfo contract) =>
            new ContractProducerProposal(ContractProducerProposalKind.FailedObserved, contract, null);

        public static ContractProducerProposal FullReconcile(IEnumerable<ContractSnapshotInfo> contracts)
        {
            IReadOnlyList<ContractSnapshotInfo> materialized =
                contracts == null
                    ? (IReadOnlyList<ContractSnapshotInfo>)Array.Empty<ContractSnapshotInfo>()
                    : new List<ContractSnapshotInfo>(contracts);
            return new ContractProducerProposal(ContractProducerProposalKind.FullReconcile, null, materialized);
        }

        public byte[] Serialize()
        {
            switch (Kind)
            {
                case ContractProducerProposalKind.OfferObserved:
                    return ContractIntentPayloadSerializer.SerializeProposal(ContractIntentPayloadKind.OfferObserved, Contract);
                case ContractProducerProposalKind.ParameterProgressObserved:
                    return ContractIntentPayloadSerializer.SerializeProposal(ContractIntentPayloadKind.ParameterProgressObserved, Contract);
                case ContractProducerProposalKind.CompletedObserved:
                    return ContractIntentPayloadSerializer.SerializeProposal(ContractIntentPayloadKind.ContractCompletedObserved, Contract);
                case ContractProducerProposalKind.FailedObserved:
                    return ContractIntentPayloadSerializer.SerializeProposal(ContractIntentPayloadKind.ContractFailedObserved, Contract);
                case ContractProducerProposalKind.FullReconcile:
                    return ContractIntentPayloadSerializer.SerializeFullReconcile(Contracts);
                default:
                    throw new InvalidOperationException($"Unknown ContractProducerProposalKind: {Kind}");
            }
        }
    }

    public enum ContractProducerProposalKind : byte
    {
        OfferObserved = 0,
        ParameterProgressObserved = 1,
        CompletedObserved = 2,
        FailedObserved = 3,
        FullReconcile = 4
    }
}
