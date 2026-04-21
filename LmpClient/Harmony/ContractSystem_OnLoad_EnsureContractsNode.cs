using Contracts;
using HarmonyLib;
using LmpCommon.Enums;
using UnityEngine;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// Root-cause guard for the "contracts never sync on (re)connect" class of bugs.
    ///
    /// Stock <c>ContractSystem.OnLoadRoutine</c> (decompiled from Assembly-CSharp.dll) is an <c>IEnumerator</c> that:
    ///   1. Yields one frame.
    ///   2. Clears the live <c>contracts</c> list.
    ///   3. Reads <c>gameNode.GetNode("CONTRACTS")</c>.
    ///   4. If that node is <b>null</b>, it does <c>yield break</c> early — WITHOUT ever setting
    ///      <c>ContractSystem.loaded = true</c> and WITHOUT registering its <c>GameEvents</c> subscriptions
    ///      (onNodeReached, onFlightReady, onVesselChange, onMissionControlSpawned, etc.).
    ///   5. Only the non-null path falls through to <c>loaded = true; yield break;</c>.
    ///
    /// In multiplayer, a client joining a universe where the server owns the authoritative contracts state
    /// commonly has a freshly-constructed <c>ProtoScenarioModule</c> for <c>ContractSystem</c> with
    /// <c>moduleValues</c> empty (no <c>CONTRACTS</c> child). That drops stock straight into the early-exit
    /// branch, leaving <c>ContractSystem.loaded == false</c> permanently. Downstream consequences:
    ///   - <see cref="LmpClient.Systems.PersistentSync.ContractsPersistentSyncClientDomain.FlushPendingState"/>
    ///     defers the server's Contracts snapshot forever (the gate waits for <c>loaded == true</c>), so the
    ///     client never sees the server's accepted/completed/offered contracts on (re)connect.
    ///   - None of stock's contract <c>GameEvents</c> subscriptions exist, so stock contract progression
    ///     (vessel change, progress nodes, reputation) silently stops tracking locally — which historically
    ///     surfaced as Mission Control being "closed" and starter missions reappearing in Available after
    ///     reconnect.
    ///
    /// The fix: before the stock routine starts, guarantee the <c>CONTRACTS</c> child exists on
    /// <paramref name="gameNode"/>. An empty <c>CONTRACTS</c> node is semantically identical to "no contracts
    /// persisted" — stock's two inner loops over <c>CONTRACT</c> and <c>CONTRACT_FINISHED</c> find zero
    /// children, fall through, register event listeners, and set <c>loaded = true</c>. PersistentSync then
    /// applies the server snapshot cleanly. Without this guard, the snapshot apply gate has no way to
    /// distinguish "coroutine still running" from "coroutine bailed out early and will never complete".
    /// </summary>
    [HarmonyPatch(typeof(ContractSystem), "OnLoad")]
    public class ContractSystem_OnLoad_EnsureContractsNode
    {
        /// <summary>
        /// Last Unity frame on which stock <see cref="ContractSystem.OnLoad"/> was observed. Used by
        /// <see cref="LmpClient.Systems.PersistentSync.ContractsPersistentSyncClientDomain.FlushPendingState"/>
        /// as a deterministic, session-resistant replacement for the static
        /// <see cref="ContractSystem.loaded"/> gate.
        ///
        /// <see cref="ContractSystem.loaded"/> is unreliable as a gate: stock sets it to <c>false</c> in
        /// <c>OnAwake</c>, but it is static, so any scene transition or scenario-runner rebuild that awakens
        /// a new <see cref="ContractSystem"/> instance resets it even after a previous
        /// <c>OnLoadRoutine</c> had completed. If stock's coroutine yield-breaks early (when <c>gameNode</c>
        /// has no <c>CONTRACTS</c> child), <c>loaded</c> is never set to <c>true</c> at all. In both cases the
        /// PersistentSync gate would defer forever.
        ///
        /// Instead: anchor the gate to the Unity frame count observed at the most recent <c>OnLoad</c>. The
        /// stock coroutine yields exactly one frame, then runs to completion the next frame. After a small
        /// completion window (see <c>OnLoadCoroutineCompletionWindowFrames</c> on
        /// <see cref="ContractsPersistentSyncClientDomain"/>) we know stock is done mutating
        /// <see cref="ContractSystem.Instance"/>'s lists and it is safe to apply. If <c>OnLoad</c> has not
        /// been observed at all this session the marker stays at <see cref="int.MinValue"/>, so the frame
        /// delta is always large and apply proceeds immediately (there is no coroutine to race).
        /// </summary>
        public static int LastOnLoadFrame = int.MinValue / 2;

        /// <summary>
        /// True once any <c>OnLoad</c> on <see cref="ContractSystem"/> has been observed in this session.
        /// </summary>
        public static bool HasObservedOnLoad;

        /// <summary>
        /// Unity frames that stock <c>ContractSystem.OnLoadRoutine</c> needs to complete after <c>OnLoad</c>
        /// fires. The coroutine yields one frame, then runs to completion the next frame; we conservatively
        /// allow a small window (4 frames) before treating the live <see cref="ContractSystem.Instance"/>
        /// lists as stable. Kept here (rather than private to any single consumer) so every PersistentSync
        /// subsystem that races this coroutine uses the same invariant — see
        /// <see cref="LmpClient.Systems.PersistentSync.ContractsPersistentSyncClientDomain.FlushPendingState"/>
        /// and <see cref="LmpClient.Systems.ShareContracts.ShareContractsSystem.ReplenishStockOffersAfterPersistentSnapshotApply"/>.
        /// </summary>
        public const int OnLoadCoroutineCompletionWindowFrames = 4;

        /// <summary>
        /// True while stock's <c>ContractSystem.OnLoadRoutine</c> is (or may still be) mid-flight: we have
        /// observed an <c>OnLoad</c> call this session and the Unity frame count has not yet advanced past
        /// the completion window. During this window <see cref="ContractSystem.Instance"/>'s
        /// <c>Contracts</c> / <c>ContractsFinished</c> lists are either cleared (pre-repopulate) or in the
        /// middle of being rebuilt from the persisted <c>gameNode</c>, so any consumer that reads them and
        /// acts on "empty = needs regeneration" will produce false-positive contract generation that
        /// pollutes the authoritative server state. Callers must short-circuit while this returns true.
        /// </summary>
        public static bool IsOnLoadCoroutineWithinCompletionWindow()
        {
            if (!HasObservedOnLoad)
            {
                return false;
            }

            var framesSinceOnLoad = Time.frameCount - LastOnLoadFrame;
            return framesSinceOnLoad >= 0 && framesSinceOnLoad < OnLoadCoroutineCompletionWindowFrames;
        }

        [HarmonyPrefix]
        private static void Prefix(ContractSystem __instance, ConfigNode gameNode)
        {
            HasObservedOnLoad = true;
            LastOnLoadFrame = Time.frameCount;

            if (MainSystem.NetworkState < ClientState.Connected) return;

            if (gameNode == null)
            {
                LunaLog.Log(
                    $"[PersistentSync] ContractSystem.OnLoad fired instanceId={__instance?.GetInstanceID()} " +
                    $"frame={LastOnLoadFrame} gameNode=null (stock OnLoadRoutine will NRE or bail; cannot inject)");
                return;
            }

            var existing = gameNode.GetNode("CONTRACTS");
            var topLevelNodeCount = gameNode.GetNodes()?.Length ?? 0;
            var topLevelValueCount = gameNode.values?.Count ?? 0;
            if (existing != null)
            {
                var contractChildren = existing.GetNodes("CONTRACT")?.Length ?? 0;
                var finishedChildren = existing.GetNodes("CONTRACT_FINISHED")?.Length ?? 0;
                LunaLog.Log(
                    $"[PersistentSync] ContractSystem.OnLoad fired instanceId={__instance?.GetInstanceID()} " +
                    $"frame={LastOnLoadFrame} gameNode.CONTRACTS present (CONTRACT={contractChildren} " +
                    $"CONTRACT_FINISHED={finishedChildren} topLevelNodeCount={topLevelNodeCount} " +
                    $"topLevelValueCount={topLevelValueCount})");
                return;
            }

            gameNode.AddNode("CONTRACTS");
            LunaLog.Log(
                $"[PersistentSync] ContractSystem.OnLoad fired instanceId={__instance?.GetInstanceID()} " +
                $"frame={LastOnLoadFrame} gameNode missing CONTRACTS child " +
                $"(topLevelNodeCount={topLevelNodeCount} topLevelValueCount={topLevelValueCount}); " +
                "injected empty node so stock OnLoadRoutine completes and registers its GameEvents " +
                "subscriptions (would yield break early otherwise)");
        }
    }
}
