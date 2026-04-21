using System;
using Contracts;
using HarmonyLib;
using LmpClient.Systems.ShareContracts;
using LmpCommon.Enums;
using UnityEngine;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// Stock <see cref="Contract.Update"/> for <see cref="Contract.State.Offered"/> rows runs each tick of
    /// the stock <c>ContractSystem.UpdateDaemon</c> coroutine. On that per-frame tick, for an offered
    /// contract stock directly transitions the state to <see cref="Contract.State.OfferExpired"/> whenever
    /// <c>Planetarium.GetUniversalTime() >= dateExpire</c> or when <see cref="Contract.MeetRequirements"/>
    /// returns false. <b>This path does not go through <see cref="Contract.Withdraw"/></b> (it calls
    /// <c>SetState</c> directly), so the Withdraw Harmony guard never sees it. The next
    /// <c>ContractSystem.UpdateContracts</c> sweep then observes <see cref="Contract.IsFinished"/>=true on
    /// the row (OfferExpired satisfies IsFinished via <c>state-2 &lt;= 2</c>), calls
    /// <see cref="Contract.Unregister"/>, and silently <c>contracts.RemoveAt(count)</c>s the row without
    /// moving it to <c>ContractsFinished</c>. No KSP log message is emitted — the Offer simply disappears.
    /// <para/>
    /// This is the exact client-side pruning we observe after a server snapshot materializes on reconnect:
    /// <list type="bullet">
    ///   <item>UT jumps forward on reconnect, so most snapshot rows have <c>dateExpire</c> behind the new UT,
    ///         and/or transient <c>MeetRequirements</c> checks fail because <c>Technology</c>/<c>PartPurchases</c>
    ///         domains apply in sibling PersistentSync passes that don't strictly precede the Contracts apply.</item>
    ///   <item>Within ~1 Unity frame after apply, the stock daemon's <c>Contract.Update</c> marks dozens of the
    ///         freshly-materialized offers as <c>OfferExpired</c>, and <c>UpdateContracts</c> silently removes
    ///         them. The Contracts list collapses from the authoritative snapshot count back to whatever
    ///         happens to survive — typically just the starter offers whose expiry matches local progression.</item>
    ///   <item>The subsequent producer full reconcile publishes that pruned view back to the server, permanently
    ///         overwriting the authoritative offer pool.</item>
    /// </list>
    /// <para/>
    /// This guard suppresses the stock offered-state update for contracts whose GUID is tracked in the last
    /// server snapshot (tracker presence is the authority signal — see
    /// <see cref="Contract_WithdrawPersistentSyncGuard"/> for why we key off the tracker rather than
    /// <see cref="ShareContractsSystem.IsPersistentSyncAuthoritativeForContracts"/>). Active/Completed/etc.
    /// rows are unaffected — stock's Active-state handling (parameter updates, <c>OnUpdate</c>, deadline
    /// enforcement) still runs so local mission progress is tracked and submitted to the server.
    /// Explicit retirement intents (Decline/Cancel) continue to flow through the normal command pipeline.
    /// </summary>
    [HarmonyPatch(typeof(Contract), nameof(Contract.Update))]
    public class Contract_UpdatePersistentSyncGuard
    {
        private const float SuppressLogThrottleSeconds = 5f;

        private static Guid _lastSuppressedGuid;

        private static float _lastSuppressedRealtime = -999f;

        [HarmonyPrefix]
        private static bool Prefix(Contract __instance)
        {
            if (__instance == null)
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

            // Only guard offered rows. Active contracts must still run stock Update so parameter handlers,
            // deadline checks, and OnUpdate fire — that's how mission progress is actually tracked client-side.
            if (__instance.ContractState != Contract.State.Offered)
            {
                return true;
            }

            // Tracker presence is the authority signal (populated only by ResetKnownContractSnapshots during
            // PersistentSync apply + authoritative FullReconcile publishes). A tracked GUID implies a canonical
            // server row exists for it; letting stock silently SetState(OfferExpired) on such a row collapses
            // the offer pool and then the next producer reconcile overwrites server truth with the collapsed view.
            // Untracked offers (locally stock-generated between snapshots — e.g. rescue contract subclass validators
            // during a controlled refresh window) still run through the original so stock's rejection path stays
            // intact for contracts the server hasn't seen.
            var sender = share.MessageSender;
            if (sender == null || !sender.IsServerKnownContract(__instance.ContractGuid))
            {
                return true;
            }

            var now = Time.realtimeSinceStartup;
            if (__instance.ContractGuid != _lastSuppressedGuid || now - _lastSuppressedRealtime >= SuppressLogThrottleSeconds)
            {
                _lastSuppressedGuid = __instance.ContractGuid;
                _lastSuppressedRealtime = now;
                LunaLog.Log(
                    $"[PersistentSync] Contract.Update suppressed for offered server-known row " +
                    $"(would silently SetState(OfferExpired) and UpdateContracts would drop the row without logging) " +
                    $"guid={__instance.ContractGuid} title={__instance.Title}");
            }

            return false;
        }
    }
}
