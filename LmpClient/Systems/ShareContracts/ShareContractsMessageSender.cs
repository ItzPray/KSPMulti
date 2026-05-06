using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
using Contracts;
using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Extensions;
using LmpClient.Network;
using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Message.Client;
using LmpCommon.Message.Interface;
using LmpCommon.PersistentSync;
using System;
using System.Collections.Generic;
using System.Globalization;
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

            if (!PersistentSyncSystem.IsLiveFor<ContractsPersistentSyncClientDomain>())
            {
                LogPersistentSyncUnavailableSkip(nameof(SendContractCommand), reason);
                return;
            }

            var command = BuildContractCommand(kind, contract);
            if (command == null)
            {
                return;
            }

            PersistentSyncSystem.SendIntent<ContractsPersistentSyncClientDomain, ContractsPayload>(new ContractsPayload { Intent = command.ToPayload() }, reason);
        }

        public void SendRequestOfferGeneration(string reason)
        {
            if (!PersistentSyncSystem.IsLiveFor<ContractsPersistentSyncClientDomain>())
            {
                LogPersistentSyncUnavailableSkip(nameof(SendRequestOfferGeneration), reason);
                return;
            }

            var command = ContractCommandIntent.RequestOfferGeneration();
            PersistentSyncSystem.SendIntent<ContractsPersistentSyncClientDomain, ContractsPayload>(new ContractsPayload { Intent = command.ToPayload() }, reason);
        }

        public void SendProducerProposal(ContractIntentPayloadKind kind, Contract contract, string reason)
        {
            if (ContractRuntimeDiagnostics.IsEnabled &&
                (kind == ContractIntentPayloadKind.ContractCompletedObserved ||
                 kind == ContractIntentPayloadKind.ContractFailedObserved))
            {
                ContractRuntimeDiagnostics.MaybeLogParameterTree(
                    contract,
                    kind == ContractIntentPayloadKind.ContractCompletedObserved
                        ? "preSend:ContractCompletedObserved"
                        : "preSend:ContractFailedObserved");
            }

            var contractSnapshots = CreateCanonicalContractSnapshots(new[] { contract });
            var changedSnapshots = _snapshotChangeTracker.FilterChanged(contractSnapshots);
            if (changedSnapshots.Length == 0)
            {
                if (ContractRuntimeDiagnostics.IsEnabled)
                {
                    LunaLog.LogWarning(
                        $"[KSPMP][ContractDiag] SendProducerProposal dropped (snapshot unchanged vs tracker) kind={kind} " +
                        $"reason={reason} guid={contract?.ContractGuid:N} title={contract?.Title}");
                    if (contract != null)
                    {
                        ContractRuntimeDiagnostics.MaybeLogParameterTree(contract, "postFilter:noDelta");
                    }
                }

                return;
            }

            if (!PersistentSyncSystem.IsLiveFor<ContractsPersistentSyncClientDomain>())
            {
                LogPersistentSyncUnavailableSkip(nameof(SendProducerProposal), reason);
                return;
            }

            var proposal = BuildContractProposal(kind, changedSnapshots[0]);
            if (proposal == null)
            {
                return;
            }

            PersistentSyncSystem.SendIntent<ContractsPersistentSyncClientDomain, ContractsPayload>(new ContractsPayload { Intent = proposal.ToPayload() }, reason);
        }

        public void SendFullContractReconcile(string reason)
        {
            if (ContractSystem.Instance == null)
            {
                return;
            }

            if (!PersistentSyncSystem.IsLiveFor<ContractsPersistentSyncClientDomain>())
            {
                LogPersistentSyncUnavailableSkip(nameof(SendFullContractReconcile), reason);
                return;
            }

            var canonicalContracts = CreateCanonicalContractSnapshots(
                ContractSystem.Instance.Contracts.Concat(ContractSystem.Instance.ContractsFinished));
            var knownContracts = canonicalContracts.ToArray();
            _snapshotChangeTracker.Reset(knownContracts);
            var proposal = ContractProducerProposal.FullReconcile(knownContracts);
            PersistentSyncSystem.SendIntent<ContractsPersistentSyncClientDomain, ContractsPayload>(new ContractsPayload { Intent = proposal.ToPayload() }, reason);
        }

        /// <summary>
        /// Stock sometimes fires <c>Contract.onAccepted</c> before every post-Accept field is written to the
        /// contract's serialization view (deadline / repair targets). If we snapshot too early, the wire record
        /// carries <c>dateDeadline=0</c>; the server echoes it and stock treats the mission as already expired.
        /// </summary>
        public static bool SerializationShowsActiveFutureDeadline(Contract contract)
        {
            if (contract == null || contract.ContractState != Contract.State.Active)
            {
                return false;
            }

            var node = new ConfigNode();
            try
            {
                contract.Save(node);
            }
            catch (Exception e)
            {
                LunaLog.LogWarning($"[PersistentSync] Accept snapshot probe: Contract.Save failed guid={contract.ContractGuid}: {e.Message}");
                return false;
            }

            if (!node.HasValue("dateDeadline"))
            {
                return false;
            }

            if (!double.TryParse(node.GetValue("dateDeadline"), NumberStyles.Float, CultureInfo.InvariantCulture, out var deadlineUt))
            {
                return false;
            }

            return deadlineUt > Planetarium.GetUniversalTime() + 5.0;
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
                    var snapshot = TryBuildContractSnapshot(contract);
                    if (snapshot == null)
                    {
                        LunaLog.LogError(
                            $"[PersistentSync] Accept command has no serializable contract snapshot (guid={contractGuid}). " +
                            "Server will fall back to state-only Active rewrite — deadlines may be wrong. Title=" +
                            contract.Title);
                    }

                    return ContractCommandIntent.Accept(contractGuid, snapshot);
                case ContractIntentPayloadKind.DeclineContract:
                    return ContractCommandIntent.Decline(contractGuid);
                case ContractIntentPayloadKind.CancelContract:
                    return ContractCommandIntent.Cancel(contractGuid);
                default:
                    LunaLog.LogError($"[PersistentSync] ShareContractsMessageSender.SendContractCommand: unsupported command kind {kind}");
                    return null;
            }
        }

        /// <summary>
        /// Builds the same wire-shaped snapshot used for PersistentSync proposals so client-side snapshot apply
        /// can compare live stock state to an incoming server row (see
        /// <see cref="ContractsPersistentSyncClientDomain"/> reuse path).
        /// </summary>
        internal static ContractSnapshotInfo TryBuildContractSnapshot(Contract contract)
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

        /// <summary>
        /// True if the given contract GUID was included in the most recently-applied server snapshot. Used by
        /// client-side stock guards (e.g. <see cref="Harmony.Contract_WithdrawPersistentSyncGuard"/>) to tell
        /// server-known offers apart from locally stock-generated offers the server has not seen yet.
        /// </summary>
        public bool IsServerKnownContract(Guid contractGuid)
        {
            return _snapshotChangeTracker.IsKnown(contractGuid);
        }

        /// <summary>
        /// True once any server snapshot has populated the known-contract tracker, even if the global
        /// <c>PersistentSyncReconciler.MarkApplied</c> flip hasn't fired yet. The tracker is seeded at the
        /// very start of <c>ContractsPersistentSyncClientDomain.ReplaceContractsFromSnapshot</c> (before any
        /// stock mutation can run), so it is the earliest reliable authority signal for client-side guards
        /// that need to suppress stock list mutations during the apply window itself.
        /// </summary>
        public bool HasAnyServerKnownContracts()
        {
            return _snapshotChangeTracker.KnownCount > 0;
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
                LunaLog.LogError($"[KSPMP]: Error while saving contract: {e}");
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
                    Data = data
                });
            }

            return snapshots;
        }

        /// <summary>Local wire-shaped contracts snapshot for Domain Analyzer (read-only, no network).</summary>
        internal static LmpCommon.PersistentSync.Payloads.Contracts.ContractsPayload TryBuildLocalAuditContractsPayload()
        {
            if (ContractSystem.Instance == null)
            {
                return null;
            }

            var snapshots = CreateCanonicalContractSnapshots(
                ContractSystem.Instance.Contracts.Concat(ContractSystem.Instance.ContractsFinished));
            snapshots.Sort((a, b) => a.ContractGuid.CompareTo(b.ContractGuid));
            return new LmpCommon.PersistentSync.Payloads.Contracts.ContractsPayload
            {
                Snapshot = new LmpCommon.PersistentSync.Payloads.Contracts.ContractSnapshotPayload
                {
                    Mode = LmpCommon.PersistentSync.Payloads.Contracts.ContractSnapshotPayloadMode.FullReplace,
                    Contracts = snapshots
                }
            };
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

