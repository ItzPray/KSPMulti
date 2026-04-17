using Contracts;
using KSP.UI.Screens;
using LmpClient.Events;
using LmpClient.Systems.Lock;
using LmpClient.Systems.ShareProgress;
using LmpCommon.Enums;
using System;
using System.Reflection;

namespace LmpClient.Systems.ShareContracts
{
    public class ShareContractsSystem : ShareProgressBaseSystem<ShareContractsSystem, ShareContractsMessageSender, ShareContractsMessageHandler>
    {
        public override string SystemName { get; } = nameof(ShareContractsSystem);

        private ShareContractsEvents ShareContractsEvents { get; } = new ShareContractsEvents();

        public int DefaultContractGenerateIterations;

        //This queue system is not used because we use one big queue in ShareCareerSystem for this system.
        protected override bool ShareSystemReady => true;

        protected override GameMode RelevantGameModes => GameMode.Career;

        protected override void OnEnabled()
        {
            base.OnEnabled();

            if (!CurrentGameModeIsRelevant) return;

            ContractSystem.generateContractIterations = 0;

            LockEvent.onLockAcquire.Add(ShareContractsEvents.LockAcquire);
            LockEvent.onLockRelease.Add(ShareContractsEvents.LockReleased);
            GameEvents.onLevelWasLoadedGUIReady.Add(ShareContractsEvents.LevelLoaded);

            GameEvents.Contract.onAccepted.Add(ShareContractsEvents.ContractAccepted);
            GameEvents.Contract.onCancelled.Add(ShareContractsEvents.ContractCancelled);
            GameEvents.Contract.onCompleted.Add(ShareContractsEvents.ContractCompleted);
            GameEvents.Contract.onContractsListChanged.Add(ShareContractsEvents.ContractsListChanged);
            GameEvents.Contract.onContractsLoaded.Add(ShareContractsEvents.ContractsLoaded);
            GameEvents.Contract.onDeclined.Add(ShareContractsEvents.ContractDeclined);
            GameEvents.Contract.onFailed.Add(ShareContractsEvents.ContractFailed);
            GameEvents.Contract.onFinished.Add(ShareContractsEvents.ContractFinished);
            GameEvents.Contract.onOffered.Add(ShareContractsEvents.ContractOffered);
            GameEvents.Contract.onParameterChange.Add(ShareContractsEvents.ContractParameterChanged);
            GameEvents.Contract.onRead.Add(ShareContractsEvents.ContractRead);
            GameEvents.Contract.onSeen.Add(ShareContractsEvents.ContractSeen);
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            ContractSystem.generateContractIterations = DefaultContractGenerateIterations;

            LockEvent.onLockAcquire.Remove(ShareContractsEvents.LockAcquire);
            LockEvent.onLockRelease.Remove(ShareContractsEvents.LockReleased);
            GameEvents.onLevelWasLoadedGUIReady.Remove(ShareContractsEvents.LevelLoaded);

            //Always try to remove the event, as when we disconnect from a server the server settings will get the default values
            GameEvents.Contract.onAccepted.Remove(ShareContractsEvents.ContractAccepted);
            GameEvents.Contract.onCancelled.Remove(ShareContractsEvents.ContractCancelled);
            GameEvents.Contract.onCompleted.Remove(ShareContractsEvents.ContractCompleted);
            GameEvents.Contract.onContractsListChanged.Remove(ShareContractsEvents.ContractsListChanged);
            GameEvents.Contract.onContractsLoaded.Remove(ShareContractsEvents.ContractsLoaded);
            GameEvents.Contract.onDeclined.Remove(ShareContractsEvents.ContractDeclined);
            GameEvents.Contract.onFailed.Remove(ShareContractsEvents.ContractFailed);
            GameEvents.Contract.onFinished.Remove(ShareContractsEvents.ContractFinished);
            GameEvents.Contract.onOffered.Remove(ShareContractsEvents.ContractOffered);
            GameEvents.Contract.onParameterChange.Remove(ShareContractsEvents.ContractParameterChanged);
            GameEvents.Contract.onRead.Remove(ShareContractsEvents.ContractRead);
            GameEvents.Contract.onSeen.Remove(ShareContractsEvents.ContractSeen);
        }

        /// <summary>
        /// Try to acquire the contract lock
        /// </summary>
        public void TryGetContractLock()
        {
            if (!LockSystem.LockQuery.ContractLockExists())
            {
                LockSystem.Singleton.AcquireContractLock();
            }
        }

        /// <summary>
        /// Refreshes derived contract UI after local contract truth was already updated.
        /// Keep this separate from snapshot correctness so UI refreshes do not become the data model.
        /// </summary>
        public void RefreshContractUiAdapters(string source)
        {
            RefreshContractLists(source);
            RefreshContractsApp(source);
            RefreshMissionControl(source);
        }

        private static void RefreshContractLists(string source)
        {
            if (ContractSystem.Instance == null)
            {
                return;
            }

            try
            {
                InvokeOptionalMethod(ContractSystem.Instance, "RefreshContracts");
                GameEvents.Contract.onContractsListChanged.Fire();
                GameEvents.Contract.onContractsLoaded.Fire();
                LunaLog.Log($"[PersistentSync] contract UI refresh source={source} adapter=contract-lists offered={ContractSystem.Instance.Contracts.Count} finished={ContractSystem.Instance.ContractsFinished.Count}");
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[PersistentSync] contract UI refresh failed source={source} adapter=contract-lists error={e}");
            }
        }

        private static void RefreshContractsApp(string source)
        {
            if (ContractsApp.Instance == null)
            {
                return;
            }

            try
            {
                InvokeOptionalMethod(ContractsApp.Instance, "OnContractsLoaded");
                InvokeOptionalMethod(ContractsApp.Instance, "CreateContractsList");
                LunaLog.Log($"[PersistentSync] contract UI refresh source={source} adapter=contracts-app");
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[PersistentSync] contract UI refresh failed source={source} adapter=contracts-app error={e}");
            }
        }

        private static void RefreshMissionControl(string source)
        {
            if (MissionControl.Instance == null)
            {
                return;
            }

            try
            {
                InvokeOptionalMethod(MissionControl.Instance, "RefreshContracts");
                InvokeOptionalMethod(MissionControl.Instance, "RebuildContractList");
                InvokeOptionalMethod(MissionControl.Instance, "RefreshUIControls");
                LunaLog.Log($"[PersistentSync] contract UI refresh source={source} adapter=mission-control");
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[PersistentSync] contract UI refresh failed source={source} adapter=mission-control error={e}");
            }
        }

        private static void InvokeOptionalMethod(object target, string methodName)
        {
            if (target == null)
            {
                return;
            }

            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                return;
            }

            method.Invoke(target, null);
        }
    }
}
