using System;
using System.Collections.Generic;
using Contracts;
using HarmonyLib;
using LmpClient.Systems.ShareContracts;
using LmpCommon.Enums;
using UnityEngine;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// Stock <c>Contract.SetState</c> is the single choke point that every stock state transition funnels
    /// through (<see cref="Contract.Accept"/>, <see cref="Contract.Update"/>'s deadline/requirements
    /// branches, cancellation paths, etc.). Two of its transitions silently retire server-authoritative
    /// contracts from the local lists without ever touching <see cref="Contract.Withdraw"/>:
    /// <list type="bullet">
    ///   <item><c>Offered → OfferExpired</c> — stock's per-frame <c>Contract.Update</c> fires this when UT
    ///         has passed <c>dateExpire</c> or when <c>MeetRequirements()</c> returns false. Covered
    ///         separately by <see cref="Contract_UpdatePersistentSyncGuard"/> (it pre-empts the Update
    ///         body, avoiding the log noise and cost of entering Update for every offered row every frame).</item>
    ///   <item><c>Active → OfferExpired</c> — after a user accepts a server-known offer, stock's next
    ///         <c>Contract.Update</c> tick re-runs <c>MeetRequirements()</c> on the now-Active contract
    ///         and, on failure, immediately demotes it to <c>OfferExpired</c>. The next
    ///         <c>ContractSystem.UpdateContracts</c> sweep then sees <see cref="Contract.IsFinished"/>=true
    ///         (OfferExpired satisfies IsFinished via <c>state-2 ≤ 2</c>) and removes the row without
    ///         adding it to <c>ContractsFinished</c>, so the contract disappears from Available AND never
    ///         arrives in Active or Archived. This is the exact "accept makes contracts vanish" symptom.</item>
    /// </list>
    /// <para/>
    /// With PersistentSync the server is authoritative for the contract lifecycle of every server-known
    /// row. <c>MeetRequirements</c> can legitimately fail client-side during the apply window — e.g. a
    /// <c>PartTest</c> contract whose prerequisite researched parts haven't yet been applied by the
    /// <c>Technology</c>/<c>PartPurchases</c> domains — so local pruning via OfferExpired would
    /// permanently collapse a temporarily-unverifiable server state. The server is the only actor allowed
    /// to retire these rows; clients propose transitions via explicit command intents
    /// (<c>DeclineContract</c>/<c>CancelContract</c>/<c>ContractCompletedObserved</c>) and the server
    /// publishes the canonical new state. Blocking <c>OfferExpired</c> transitions here therefore matches
    /// the authoritative contract model without affecting any real state change.
    /// <para/>
    /// All other <c>SetState</c> transitions (Active, Completed, DeadlineExpired, Failed, Cancelled,
    /// Declined, Withdrawn) keep flowing through the original method — they're either player-initiated
    /// or represent legitimate terminal outcomes the server accepts.
    /// </summary>
    [HarmonyPatch(typeof(Contract), "SetState")]
    public class Contract_SetStatePersistentSyncGuard
    {
        private const float SuppressLogWindowSeconds = 10f;

        private static float _windowStartRealtime = -1f;

        private static int _windowSuppressedCount;

        private static readonly HashSet<Guid> _windowSuppressedGuids = new HashSet<Guid>();

        [HarmonyPrefix]
        private static bool Prefix(Contract __instance, Contract.State newState)
        {
            if (__instance == null)
            {
                return true;
            }

            if (newState != Contract.State.OfferExpired)
            {
                return true;
            }

            if (MainSystem.NetworkState < ClientState.Connected)
            {
                return true;
            }

            var share = ShareContractsSystem.Singleton;
            if (share == null || !share.Enabled)
            {
                return true;
            }

            var sender = share.MessageSender;
            if (sender == null || !sender.IsServerKnownContract(__instance.ContractGuid))
            {
                return true;
            }

            LogSuppressionThrottled(__instance);
            return false;
        }

        private static void LogSuppressionThrottled(Contract contract)
        {
            var now = Time.realtimeSinceStartup;
            if (_windowStartRealtime < 0f)
            {
                _windowStartRealtime = now;
            }

            _windowSuppressedCount++;
            _windowSuppressedGuids.Add(contract.ContractGuid);

            if (now - _windowStartRealtime < SuppressLogWindowSeconds)
            {
                return;
            }

            LunaLog.Log(
                $"[PersistentSync] Contract.SetState(OfferExpired) suppressed for {_windowSuppressedGuids.Count} server-known contract(s) " +
                $"(stock would demote Active→OfferExpired or confirm Offered→OfferExpired and UpdateContracts would silently drop them): " +
                $"{_windowSuppressedCount} transitions in last {SuppressLogWindowSeconds:0.#}s, " +
                $"sample guid={contract.ContractGuid} title={contract.Title} currentState={contract.ContractState}");

            _windowStartRealtime = now;
            _windowSuppressedCount = 0;
            _windowSuppressedGuids.Clear();
        }
    }
}
