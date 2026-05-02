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
        // Aggregate throttle window (seconds). Stock ContractSystem.UpdateDaemon calls Contract.Update on every
        // Offered row every frame, so a naive "log on each suppression" produced ~1500 lines/sec with 25 server-
        // known offers (176k lines in under two minutes in production). Flood that via LunaLog → disk IO → the
        // Unity main thread stalls during scene transitions. Log at most once per window with aggregate stats.
        private const float SuppressLogWindowSeconds = 10f;

        private static float _windowStartRealtime = -1f;

        private const int SuppressLogSampleEveryCalls = 1024;

        private static int _windowSuppressedCount;

        private static Guid _windowSampleGuid;

        private static string _windowSampleTitle;

        [HarmonyPrefix]
        private static bool Prefix(Contract __instance)
        {
            if (!ShouldSuppressServerKnownOfferedUpdate(__instance))
            {
                return true;
            }

            LogSuppressionThrottled(__instance);
            return false;
        }

        public static bool ShouldSuppressServerKnownOfferedUpdate(Contract contract)
        {
            if (contract == null)
            {
                return false;
            }

            if (MainSystem.NetworkState < ClientState.Connected)
            {
                return false;
            }

            // Only guard offered rows. Active contracts must still run stock Update so parameter handlers,
            // deadline checks, and OnUpdate fire — that's how mission progress is actually tracked client-side.
            if (contract.ContractState != Contract.State.Offered)
            {
                return false;
            }

            var share = ShareContractsSystem.Singleton;
            if (share == null || !share.Enabled)
            {
                return false;
            }

            // Tracker presence is the authority signal (populated only by ResetKnownContractSnapshots during
            // PersistentSync apply + authoritative FullReconcile publishes). A tracked GUID implies a canonical
            // server row exists for it; letting stock silently SetState(OfferExpired) on such a row collapses
            // the offer pool and then the next producer reconcile overwrites server truth with the collapsed view.
            // Untracked offers (locally stock-generated between snapshots — e.g. rescue contract subclass validators
            // during a controlled refresh window) still run through the original so stock's rejection path stays
            // intact for contracts the server hasn't seen.
            var sender = share.MessageSender;
            return sender != null && sender.IsServerKnownContract(contract.ContractGuid);
        }

        private static void LogSuppressionThrottled(Contract contract)
        {
            if (_windowSuppressedCount == 0)
            {
                _windowStartRealtime = Time.realtimeSinceStartup;
                _windowSampleGuid = contract.ContractGuid;
                _windowSampleTitle = contract.Title;
            }

            _windowSuppressedCount++;

            // This prefix can run thousands of times per ten-second window. Keep the hot path cheap:
            // count every suppressed call, but only sample Unity realtime/log metadata periodically.
            if ((_windowSuppressedCount & (SuppressLogSampleEveryCalls - 1)) != 0)
            {
                return;
            }

            var now = Time.realtimeSinceStartup;

            if (now - _windowStartRealtime < SuppressLogWindowSeconds)
            {
                return;
            }

            LunaLog.Log(
                $"[PersistentSync] Contract.Update suppressed server-known offered row update(s) " +
                $"(stock would silently SetState(OfferExpired) and UpdateContracts would drop them without logging): " +
                $"{_windowSuppressedCount} calls in last {SuppressLogWindowSeconds:0.#}s, " +
                $"sample guid={_windowSampleGuid} title={_windowSampleTitle}");

            _windowStartRealtime = now;
            _windowSuppressedCount = 0;
            _windowSampleGuid = Guid.Empty;
            _windowSampleTitle = null;
        }
    }

    /// <summary>
    /// Stock <c>ContractSystem.UpdateContracts</c> walks every current contract once per frame and calls
    /// <see cref="Contract.Update"/> on every non-finished row. A prefix on <see cref="Contract.Update"/> can stop
    /// stock from expiring server-known offered rows, but it still pays the per-row Harmony dispatch cost forever.
    /// Replacing the owning loop lets PersistentSync keep offered rows visible while avoiding the hot path entirely.
    /// Active rows still call stock <see cref="Contract.Update"/> so parameter progress and deadlines keep working.
    /// </summary>
    [HarmonyPatch(typeof(ContractSystem), "UpdateContracts")]
    public class ContractSystem_UpdateContractsPersistentSyncGuard
    {
        private static readonly System.Reflection.FieldInfo RequireRefreshField =
            AccessTools.Field(typeof(ContractSystem), "requireRefresh");

        [HarmonyPrefix]
        private static bool Prefix(ContractSystem __instance)
        {
            if (__instance == null || !ShouldUsePersistentSyncContractUpdateLoop())
            {
                return true;
            }

            var contracts = __instance.Contracts;
            var contractsFinished = __instance.ContractsFinished;
            if (contracts == null || contractsFinished == null)
            {
                return true;
            }

            var requireRefresh = false;
            var count = contracts.Count;
            while (count-- > 0)
            {
                var contract = contracts[count];
                if (contract == null)
                {
                    contracts.RemoveAt(count);
                    requireRefresh = true;
                    continue;
                }

                if (contract.IsFinished())
                {
                    contract.Unregister();
                    if (contract.ContractState == Contract.State.Completed ||
                        contract.ContractState == Contract.State.DeadlineExpired ||
                        contract.ContractState == Contract.State.Failed ||
                        contract.ContractState == Contract.State.Cancelled)
                    {
                        contractsFinished.Add(contract);
                    }

                    contracts.RemoveAt(count);
                    requireRefresh = true;
                    continue;
                }

                if (Contract_UpdatePersistentSyncGuard.ShouldSuppressServerKnownOfferedUpdate(contract))
                {
                    continue;
                }

                contract.Update();
            }

            if (contracts.Count == 0)
            {
                requireRefresh = true;
            }

            if (requireRefresh)
            {
                RequireRefreshField?.SetValue(__instance, true);
            }

            return false;
        }

        private static bool ShouldUsePersistentSyncContractUpdateLoop()
        {
            if (MainSystem.NetworkState < ClientState.Connected)
            {
                return false;
            }

            var share = ShareContractsSystem.Singleton;
            return share != null &&
                   share.Enabled &&
                   share.ShouldBypassStockContractSystemRefreshForPersistentContracts();
        }
    }
}
