using Contracts;
using HarmonyLib;
using LmpClient.Systems.ShareContracts;
using LmpCommon.Enums;
using UnityEngine;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// Stock <see cref="ContractSystem.RefreshContracts"/> rebuilds/regenerates the offer pool from local progression.
    /// After PersistentSync materializes server contracts, that pass can clear the main list while
    /// <see cref="ContractSystem.ContractsFinished"/> still matches the archive (empty Mission Control "Available").
    /// When we already applied the authoritative contracts snapshot, skip the stock body and only rebind UI from
    /// the current <see cref="ContractSystem.Instance"/> lists (same contract as ps-safe refresh paths).
    /// </summary>
    [HarmonyPatch(typeof(ContractSystem), "RefreshContracts")]
    public class ContractSystem_RefreshContractsPersistentSyncGuard
    {
        private static string _bypassLogSignature;

        private static float _bypassLogRealtime = -999f;

        private const float BypassLogThrottleSeconds = 2f;

        [HarmonyPrefix]
        private static bool Prefix(ContractSystem __instance)
        {
            if (MainSystem.NetworkState < ClientState.Connected)
            {
                return true;
            }

            if (__instance == null || !ReferenceEquals(__instance, ContractSystem.Instance))
            {
                return true;
            }

            var share = ShareContractsSystem.Singleton;
            if (share == null || !share.ShouldBypassStockContractSystemRefreshForPersistentContracts())
            {
                return true;
            }

            var main = ContractSystem.Instance.Contracts?.Count ?? 0;
            var finished = ContractSystem.Instance.ContractsFinished?.Count ?? 0;
            var sig = $"{main}:{finished}";
            var now = Time.realtimeSinceStartup;
            if (!string.Equals(sig, _bypassLogSignature, System.StringComparison.Ordinal) ||
                now - _bypassLogRealtime >= BypassLogThrottleSeconds)
            {
                _bypassLogSignature = sig;
                _bypassLogRealtime = now;
                LunaLog.Log(
                    $"[PersistentSync] ContractSystem.RefreshContracts bypassed (PersistentSync contracts snapshot applied; " +
                    $"stock refresh would rebuild from local progression). Runtime lists before UI rebind: main={main} finished={finished}");
            }

            share.RefreshContractUiAdapters(ShareContractsSystem.ContractSystemRefreshPersistentSyncBypassSource);
            return false;
        }
    }
}
