using Contracts;
using Contracts.Templates;
using LmpClient;
using LmpClient.Base;
using LmpClient.Systems.Lock;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Locks;
using LmpCommon.PersistentSync;
using System;
using System.Linq;

namespace LmpClient.Systems.ShareContracts
{
    public class ShareContractsEvents : SubSystem<ShareContractsSystem>
    {
        /// <summary>
        /// If we get the contract lock then generate contracts
        /// </summary>
        public void LockAcquire(LockDefinition lockDefinition)
        {
            if (lockDefinition.Type == LockType.Contract && lockDefinition.PlayerName == SettingsSystem.CurrentSettings.PlayerName)
            {
                System.RequestControlledStockContractRefresh("ContractLockAcquire");
                System.ApplyStockContractMutationPolicy("ContractLockAcquire");
                System.ReplenishStockOffersAfterPersistentSnapshotApply("ContractLockAcquire");
                // Explicit producer-side authority handoff: once we own the contract lock, publish one full reconcile
                // after the controlled-refresh settle window so the server canonical row set reflects our truth.
                System.ScheduleProducerFullReconcileAfterLockHandoff("ContractLockAcquire");
            }
        }

        /// <summary>
        /// Try to get contract lock
        /// </summary>
        public void LockReleased(LockDefinition lockDefinition)
        {
            if (lockDefinition.Type == LockType.Contract)
            {
                System.ApplyStockContractMutationPolicy("ContractLockReleased");
                System.TryGetContractLock();
            }
        }

        /// <summary>
        /// Try to get contract lock when loading a level
        /// </summary>
        public void LevelLoaded(GameScenes data)
        {
            System.TryGetContractLock();
            System.RequestControlledStockContractRefresh("LevelLoaded");
            System.ApplyStockContractMutationPolicy("LevelLoaded");
        }

        #region EventHandlers

        public void ContractAccepted(Contract contract)
        {
            if (System.IgnoreEvents) return;

            ShareContractsSystem.LogMcUiContractInventory($"Contract.onAccepted:beforeWire guid={contract?.ContractGuid}");
            // Do not call SendContractCommand synchronously: stock can fire onAccepted before dateDeadline is
            // present in Contract.Save output; the server then stores Active+zero deadline and clients see instant expiry.
            System.EnqueueAcceptContractWireIntent(contract);
            ShareContractsSystem.LogMcUiContractInventory($"Contract.onAccepted:afterSchedule guid={contract.ContractGuid}");
        }

        public void ContractCancelled(Contract contract)
        {
            if (System.IgnoreEvents) return;

            System.MessageSender.SendContractCommand(
                ContractIntentPayloadKind.CancelContract,
                contract,
                $"ContractCommand:Cancel:{contract?.ContractGuid:N}");
            LunaLog.Log($"Contract cancelled: {contract.ContractGuid}");
        }

        public void ContractCompleted(Contract contract)
        {
            if (System.IgnoreEvents) return;

            // Do not gate on contract-lock ownership: the flying client completes Active work while the lock
            // holder is often another player (Mission Control / offer generation). Server ReduceObservedActive
            // only merges when the canonical row is Active.
            System.MessageSender.SendProducerProposal(
                ContractIntentPayloadKind.ContractCompletedObserved,
                contract,
                $"ContractProposal:Completed:{contract?.ContractGuid:N}");

            LunaLog.Log($"Contract completed: {contract.ContractGuid}");
        }

        public void ContractsListChanged()
        {
            if (System.IgnoreEvents) return;

            LunaLog.Log("Contract list changed.");
        }

        public void ContractsLoaded()
        {
            LunaLog.Log("Contracts loaded.");
        }

        public void ContractDeclined(Contract contract)
        {
            if (System.IgnoreEvents) return;

            System.MessageSender.SendContractCommand(
                ContractIntentPayloadKind.DeclineContract,
                contract,
                $"ContractCommand:Decline:{contract?.ContractGuid:N}");
            LunaLog.Log($"Contract declined: {contract.ContractGuid}");
        }

        public void ContractFailed(Contract contract)
        {
            if (System.IgnoreEvents) return;

            System.MessageSender.SendProducerProposal(
                ContractIntentPayloadKind.ContractFailedObserved,
                contract,
                $"ContractProposal:Failed:{contract?.ContractGuid:N}");

            LunaLog.Log($"Contract failed: {contract.ContractGuid}");
        }

        public void ContractFinished(Contract contract)
        {
            /*
            Doesn't need to be synchronized because there is no ContractFinished state.
            Also the contract will be finished on the contract complete / failed / cancelled / ...
            */
        }

        public void ContractOffered(Contract contract)
        {
            if (!LockSystem.LockQuery.ContractLockBelongsToPlayer(SettingsSystem.CurrentSettings.PlayerName))
            {
                //We don't have the contract lock so remove the contract that we spawned.
                //The idea is that ONLY THE PLAYER with the contract lock spawn contracts to the other players
                contract.Withdraw();
                contract.Kill();
                return;
            }

            if (contract.GetType() == typeof(RecoverAsset))
            {
                //We don't support rescue contracts. See: https://github.com/LunaMultiplayer/LunaMultiplayer/issues/226#issuecomment-431831526
                contract.Withdraw();
                contract.Kill();
                return;
            }

            // Title-based dedup MUST run even while IgnoreEvents is true. Background: the Contracts
            // PersistentSync client domain wraps its entire snapshot apply in StartIgnoringEvents to
            // stop server-applied state from echoing back as fresh client intents. That scope ALSO
            // contains our controlled ReplenishStockOffersAfterPersistentSnapshotApply → stock
            // RefreshContracts call, whose explicit purpose is to mint NEW progression-unlocked
            // offers on the client. Any contract stock generates during that call fires
            // Contract.onOffered while IgnoreEvents is true. If we early-return on IgnoreEvents
            // before this check, fresh-GUID duplicates of Finished rows (canonical repro:
            // "Launch our first vessel!" regenerated after the starter completion, because the
            // order in which PersistentSync reapplies Achievements vs Contracts vs when stock's
            // generator re-checks ProgressTracking.FirstLaunch.IsComplete is not tight enough to
            // always block the regen) slip into ContractSystem.Instance.Contracts in Offered state
            // and are then harvested by the next ProducerFullReconcile — the user sees the just-
            // completed mission appear back in the Available list after reconnect. Running the
            // dedup always keeps local state consistent with the server's canonical set and is
            // independent of whether the offer will be published this frame or later.
            if (TrySuppressDuplicateOfferByTitle(contract))
            {
                return;
            }

            // IgnoreEvents suppresses "server-applied state echoing back as client intent" events.
            // Offers minted by our own controlled RefreshContracts call (gated by
            // ShareContractsSystem.IsInsideControlledStockContractRefresh) are NOT echoes: the proto
            // mirror and ReplaceContractsFromSnapshot paths assign state directly without firing
            // onOffered, so an onOffered observed while IsInsideControlledStockContractRefresh is
            // true must have come from stock generating a brand-new contract. Those need to reach
            // the server — otherwise they stay local-only, get wiped whenever the next snapshot
            // lands, and the user observes "new progression missions I saw after completing the
            // first mission all disappear after reconnect" because the client never told the
            // server they existed.
            if (System.IgnoreEvents && !System.IsInsideControlledStockContractRefresh)
            {
                return;
            }

            LunaLog.Log($"Contract offered: {contract.ContractGuid} - {contract.Title}");

            //This should be only called on the client with the contract lock, because it has the generationCount != 0.
            if (System.TryDeferContractOfferIfTimeWarping(contract))
            {
                return;
            }

            System.MessageSender.SendProducerProposal(
                ContractIntentPayloadKind.OfferObserved,
                contract,
                $"ContractProposal:Offer:{contract?.ContractGuid:N}");
        }

        public void ContractParameterChanged(Contract contract, ContractParameter contractParameter)
        {
            if (System.IgnoreEvents) return;

            if (contract == null || ShareContractsSystem.IsMissionControlOfferPoolContract(contract))
            {
                return;
            }

            System.MessageSender.SendProducerProposal(
                ContractIntentPayloadKind.ParameterProgressObserved,
                contract,
                $"ContractProposal:Parameter:{contract.ContractGuid:N}");

            System.InvalidatePersistentSyncBypassUiRefreshCoalesce();
        }

        public void ContractRead(Contract contract)
        {
        }

        public void ContractSeen(Contract contract)
        {
        }

        /// <summary>
        /// Stock sometimes offers the same mission title again (new GUID). Keep the first offered row and drop the new one
        /// so we never sync duplicate titles to the server. Also matches the archive list so completed tutorials are not
        /// re-offered after reconnect when ProgressTracking has not caught up yet.
        /// </summary>
        private static bool TrySuppressDuplicateOfferByTitle(Contract contract)
        {
            if (contract == null || ContractSystem.Instance == null || !ShareContractsSystem.IsMissionControlOfferPoolContract(contract))
            {
                return false;
            }

            var incomingKey = ShareContractsSystem.BuildRuntimeContractIdentityKey(contract);
            if (string.IsNullOrEmpty(incomingKey))
            {
                return false;
            }

            foreach (var other in ContractSystem.Instance.ContractsFinished.ToArray())
            {
                if (other == null || ReferenceEquals(other, contract))
                {
                    continue;
                }

                if (!string.Equals(
                        ShareContractsSystem.BuildRuntimeContractIdentityKey(other),
                        incomingKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                contract.Withdraw();
                contract.Kill();
                LunaLog.Log($"[PersistentSync] suppressed duplicate offer (matches finished archive contract {other.ContractGuid}): {contract.Title}");
                return true;
            }

            foreach (var other in ContractSystem.Instance.Contracts.ToArray())
            {
                if (other == null || ReferenceEquals(other, contract))
                {
                    continue;
                }

                var blocksOfferedDuplicate =
                    other.ContractState == Contract.State.Active ||
                    ShareContractsSystem.IsMissionControlOfferPoolContract(other);

                if (!blocksOfferedDuplicate)
                {
                    continue;
                }

                if (!string.Equals(
                        ShareContractsSystem.BuildRuntimeContractIdentityKey(other),
                        incomingKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                contract.Withdraw();
                contract.Kill();
                LunaLog.Log($"[PersistentSync] suppressed duplicate contract offer (same identity as {other.ContractGuid}, state={other.ContractState}): {contract.Title}");
                return true;
            }

            return false;
        }

        #endregion
    }
}
