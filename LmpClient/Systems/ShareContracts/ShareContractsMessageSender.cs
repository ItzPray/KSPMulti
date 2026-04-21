using Contracts;
using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Extensions;
using LmpClient.Network;
using LmpClient.Systems.PersistentSync;
using LmpCommon.Message.Client;
using LmpCommon.Message.Interface;
using LmpCommon.PersistentSync;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LmpClient.Systems.ShareContracts
{
    public class ShareContractsMessageSender : SubSystem<ShareContractsSystem>, IMessageSender
    {
        private const string LmpOfferTitleFieldName = "lmpOfferTitle";
        private readonly ContractSnapshotChangeTracker _snapshotChangeTracker = new ContractSnapshotChangeTracker();

        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<ShareProgressCliMsg>(msg)));
        }

        public void SendContractCommand(ContractIntentPayloadKind kind, Contract contract, string reason)
        {
            if (contract == null)
            {
                return;
            }

            if (!PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainId.Contracts))
            {
                LogPersistentSyncUnavailableSkip(nameof(SendContractCommand), reason);
                return;
            }

            var command = BuildContractCommand(kind, contract);
            if (command == null)
            {
                return;
            }

            PersistentSyncSystem.Singleton.MessageSender.SendContractsIntentPayload(command.Serialize(), reason);
        }

        public void SendRequestOfferGeneration(string reason)
        {
            if (!PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainId.Contracts))
            {
                LogPersistentSyncUnavailableSkip(nameof(SendRequestOfferGeneration), reason);
                return;
            }

            var command = ContractCommandIntent.RequestOfferGeneration();
            PersistentSyncSystem.Singleton.MessageSender.SendContractsIntentPayload(command.Serialize(), reason);
        }

        public void SendProducerProposal(ContractIntentPayloadKind kind, Contract contract, string reason)
        {
            var contractSnapshots = CreateCanonicalContractSnapshots(new[] { contract });
            var changedSnapshots = _snapshotChangeTracker.FilterChanged(contractSnapshots);
            if (changedSnapshots.Length == 0)
            {
                return;
            }

            if (!PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainId.Contracts))
            {
                LogPersistentSyncUnavailableSkip(nameof(SendProducerProposal), reason);
                return;
            }

            var proposal = BuildContractProposal(kind, changedSnapshots[0]);
            if (proposal == null)
            {
                return;
            }

            PersistentSyncSystem.Singleton.MessageSender.SendContractsIntentPayload(proposal.Serialize(), reason);
        }

        public void SendFullContractReconcile(string reason)
        {
            if (ContractSystem.Instance == null)
            {
                return;
            }

            if (!PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainId.Contracts))
            {
                LogPersistentSyncUnavailableSkip(nameof(SendFullContractReconcile), reason);
                return;
            }

            var canonicalContracts = CreateCanonicalContractSnapshots(
                ContractSystem.Instance.Contracts.Concat(ContractSystem.Instance.ContractsFinished));
            var knownContracts = canonicalContracts.ToArray();
            _snapshotChangeTracker.Reset(knownContracts);
            var proposal = ContractProducerProposal.FullReconcile(knownContracts);
            PersistentSyncSystem.Singleton.MessageSender.SendContractsIntentPayload(proposal.Serialize(), reason);
        }

        private static ContractCommandIntent BuildContractCommand(ContractIntentPayloadKind kind, Contract contract)
        {
            var contractGuid = contract.ContractGuid;
            switch (kind)
            {
                case ContractIntentPayloadKind.AcceptContract:
                    // Accept must carry the full post-Accept contract record. Stock Contract.Accept() populates
                    // runtime-only fields (dateAccepted, dateDeadline, subclass-specific targets like
                    // VesselRepairContract's target vessel) that are absent from the pre-Accept Offered row the
                    // server already has. If we only send the GUID the server rewrites state=Offered->Active on
                    // that stale row; when the server echoes the snapshot back, the accepting client's
                    // re-materialized Contract has dateDeadline=0 and stock Update() instantly flips it to
                    // DeadlineExpired, paying ContractPenalty and firing ContractFailedObserved back to the
                    // server. Passing the post-Accept snapshot keeps the canonical row internally consistent
                    // with the Active state.
                    return ContractCommandIntent.Accept(contractGuid, TryBuildContractSnapshot(contract));
                case ContractIntentPayloadKind.DeclineContract:
                    return ContractCommandIntent.Decline(contractGuid);
                case ContractIntentPayloadKind.CancelContract:
                    return ContractCommandIntent.Cancel(contractGuid);
                default:
                    LunaLog.LogError($"[PersistentSync] ShareContractsMessageSender.SendContractCommand: unsupported command kind {kind}");
                    return null;
            }
        }

        private static ContractSnapshotInfo TryBuildContractSnapshot(Contract contract)
        {
            if (contract == null)
            {
                return null;
            }

            var snapshots = CreateCanonicalContractSnapshots(new[] { contract });
            return snapshots.Count > 0 ? snapshots[0] : null;
        }

        private static ContractProducerProposal BuildContractProposal(ContractIntentPayloadKind kind, ContractSnapshotInfo contract)
        {
            switch (kind)
            {
                case ContractIntentPayloadKind.OfferObserved:
                    return ContractProducerProposal.OfferObserved(contract);
                case ContractIntentPayloadKind.ParameterProgressObserved:
                    return ContractProducerProposal.ParameterProgressObserved(contract);
                case ContractIntentPayloadKind.ContractCompletedObserved:
                    return ContractProducerProposal.CompletedObserved(contract);
                case ContractIntentPayloadKind.ContractFailedObserved:
                    return ContractProducerProposal.FailedObserved(contract);
                default:
                    LunaLog.LogError($"[PersistentSync] ShareContractsMessageSender.SendProducerProposal: unsupported proposal kind {kind}");
                    return null;
            }
        }

        public void SendFullContractSystemSnapshot(string reason)
        {
            SendFullContractReconcile(reason);
        }

        private static void LogPersistentSyncUnavailableSkip(string methodName, string reason)
        {
            // Plan: contracts domain has a single live path (PersistentSync typed intents). Before PS negotiates
            // ready with the server we must not emit legacy raw-relay contract messages; skip and rely on the next
            // canonical snapshot to converge once PS is live.
            LunaLog.LogWarning(
                $"[PersistentSync] ShareContractsMessageSender.{methodName} skipped (reason={reason}): PersistentSync is not live for contracts yet; legacy raw-relay contract path is disabled.");
        }

        public void ResetKnownContractSnapshots(IEnumerable<ContractSnapshotInfo> contracts)
        {
            _snapshotChangeTracker.Reset(contracts);
        }

        public void ClearKnownContractSnapshots()
        {
            _snapshotChangeTracker.Clear();
        }

        private static ConfigNode ConvertContractToConfigNode(Contract contract)
        {
            var configNode = new ConfigNode();
            try
            {
                contract.Save(configNode);
                WriteSyntheticOfferMetadata(configNode, contract);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: Error while saving contract: {e}");
                return null;
            }

            return configNode;
        }

        private static void WriteSyntheticOfferMetadata(ConfigNode configNode, Contract contract)
        {
            if (configNode == null || contract == null)
            {
                return;
            }

            var title = ShareContractsSystem.NormalizeOfferTitleForDedupe(contract.Title);
            if (string.IsNullOrEmpty(title))
            {
                return;
            }

            configNode.RemoveValues(LmpOfferTitleFieldName);
            configNode.AddValue(LmpOfferTitleFieldName, title);
        }

        private static List<ContractSnapshotInfo> CreateCanonicalContractSnapshots(IEnumerable<Contract> contracts)
        {
            var snapshots = new List<ContractSnapshotInfo>();
            foreach (var contract in contracts ?? Enumerable.Empty<Contract>())
            {
                if (contract == null)
                {
                    continue;
                }

                var configNode = ConvertContractToConfigNode(contract);
                if (configNode == null)
                {
                    continue;
                }

                var data = configNode.Serialize();
                snapshots.Add(new ContractSnapshotInfo
                {
                    ContractGuid = contract.ContractGuid,
                    ContractState = contract.ContractState.ToString(),
                    Placement = DeterminePlacement(contract),
                    Order = -1,
                    Data = data,
                    NumBytes = data.Length
                });
            }

            return snapshots;
        }

        private static ContractSnapshotPlacement DeterminePlacement(Contract contract)
        {
            if (contract == null)
            {
                return ContractSnapshotPlacement.Current;
            }

            if (contract.IsFinished())
            {
                return ContractSnapshotPlacement.Finished;
            }

            switch (contract.ContractState)
            {
                case Contract.State.Active:
                    return ContractSnapshotPlacement.Active;
                case Contract.State.Completed:
                case Contract.State.DeadlineExpired:
                case Contract.State.Failed:
                case Contract.State.Cancelled:
                case Contract.State.Declined:
                case Contract.State.Withdrawn:
                    return ContractSnapshotPlacement.Finished;
                default:
                    return ContractSnapshotPlacement.Current;
            }
        }
    }
}
