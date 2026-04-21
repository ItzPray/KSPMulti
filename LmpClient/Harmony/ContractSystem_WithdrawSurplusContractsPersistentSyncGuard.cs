using Contracts;
using HarmonyLib;
using LmpClient.Systems.ShareContracts;
using LmpCommon.Enums;
using UnityEngine;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// Stock <see cref="ContractSystem.WithdrawSurplusContracts"/> is invoked at the top of every
    /// <see cref="ContractSystem.RefreshContracts"/> call. It treats the local offer pool as a rotation cache and
    /// calls <c>Contract.Withdraw()</c> on every Offered contract beyond the tier caps derived from local reputation
    /// (<c>ContractDefs.AverageAvailableContracts</c>, typically ~4..15 total).
    /// <para/>
    /// In stock this is correct because the local offer pool IS the source of truth. With PersistentSync the server
    /// is authoritative for the offer pool across the entire session/subspace and legitimately holds far more offers
    /// than any single client's tier caps would allow. Without this guard, every stock refresh (update daemon,
    /// reputation change, achievement, mission control spawn, controlled replenish window, etc.) collapses the
    /// server-authoritative list back to ~4 offers within one frame.
    /// <para/>
    /// Offer-pool capping is performed server-side in <c>ContractsPersistentSyncDomainStore</c> once the server has
    /// the full picture. Clients must not prune the list based on their own local reputation.
    /// </summary>
    [HarmonyPatch(typeof(ContractSystem), "WithdrawSurplusContracts")]
    public class ContractSystem_WithdrawSurplusContractsPersistentSyncGuard
    {
        private static string _suppressLogSignature;

        private static float _suppressLogRealtime = -999f;

        private const float SuppressLogThrottleSeconds = 5f;

        [HarmonyPrefix]
        private static bool Prefix(Contract.ContractPrestige level, int maxAllowed, ref bool __result)
        {
            if (MainSystem.NetworkState < ClientState.Connected)
            {
                return true;
            }

            var share = ShareContractsSystem.Singleton;
            if (share == null || !share.Enabled)
            {
                return true;
            }

            // Activate as soon as the tracker has any server-known contracts. IsPersistentSyncAuthoritativeForContracts
            // reads Reconciler.State.HasInitialSnapshot, which only flips true after MarkApplied fires — but the
            // tracker is populated at the very start of ReplaceContractsFromSnapshot, and stock RefreshContracts
            // (which calls WithdrawSurplusContracts) can execute between those moments, pruning the freshly
            // materialized server-authoritative offer pool down to local tier caps in that window. Tracker presence
            // is strictly a subset of HasInitialSnapshot once both are true, so this only WIDENS the guard window.
            var sender = share.MessageSender;
            var trackerActive = sender != null && sender.HasAnyServerKnownContracts();
            if (!trackerActive && !share.IsPersistentSyncAuthoritativeForContracts())
            {
                return true;
            }

            __result = false;

            var cs = ContractSystem.Instance;
            var main = cs?.Contracts?.Count ?? 0;
            var sig = $"{level}:{maxAllowed}:{main}";
            var now = Time.realtimeSinceStartup;
            if (!string.Equals(sig, _suppressLogSignature, System.StringComparison.Ordinal) ||
                now - _suppressLogRealtime >= SuppressLogThrottleSeconds)
            {
                _suppressLogSignature = sig;
                _suppressLogRealtime = now;
                LunaLog.Log(
                    $"[PersistentSync] ContractSystem.WithdrawSurplusContracts suppressed (PersistentSync authoritative) " +
                    $"level={level} localMaxAllowed={maxAllowed} runtimeMainCount={main}");
            }

            return false;
        }
    }
}
