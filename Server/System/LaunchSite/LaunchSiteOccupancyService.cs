using System;
using System.Collections.Generic;
using System.Linq;
using LmpCommon.Enums;
using LmpCommon.Locks;
using LmpCommon.Message.Data.LaunchPad;
using LmpCommon.Message.Server;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Server;
using Server.Settings.Structures;
using Server.System.Vessel;
using Server.System.Vessel.Classes;
using VesselClass = Server.System.Vessel.Classes.Vessel;

namespace Server.System.LaunchSite
{
    /// <summary>
    /// Tracks PRELAUNCH vessels + optional pad reservations; broadcasts snapshot or delta.
    /// </summary>
    public static class LaunchSiteOccupancyService
    {
        private static readonly object SnapshotGate = new object();
        private static List<LaunchPadOccupancyEntry> _lastSentEntries = new List<LaunchPadOccupancyEntry>();
        private static bool _lastSentOverflow;
        private static float _lastSentBubble;

        private const int MaxDeltaOpsBeforeSnapshot = 12;

        public static bool IsSiteOccupiedByAnotherPlayerVesselOrReservation(string siteKey, string playerName)
        {
            var now = DateTime.UtcNow;
            if (LaunchPadReservationRegistry.IsBlockedByOtherReservation(siteKey, playerName, now))
                return true;

            foreach (var kv in VesselStoreSystem.CurrentVessels)
            {
                var sk = LaunchSiteKeyParser.TryBuildSiteKey(kv.Value);
                if (sk == null || !string.Equals(sk, siteKey, StringComparison.Ordinal)) continue;
                var owner = ResolveVesselOwner(kv.Key);
                if (!string.IsNullOrEmpty(owner) && !string.Equals(owner, playerName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public static bool TryAcceptIncomingProto(ClientStructure client, Guid vesselId, string vesselConfigText, out string denyReason)
        {
            denyReason = null;
            var mode = GeneralSettings.SettingsStore.LaunchPadCoordinationMode;
            if (mode == LaunchPadCoordinationMode.Off)
                return true;

            VesselClass parsed;
            try
            {
                parsed = new VesselClass(vesselConfigText);
            }
            catch (Exception ex)
            {
                LunaLog.Warning($"[LaunchPad] Could not parse vessel proto for pad check: {ex.Message}");
                return true;
            }

            var siteKey = LaunchSiteKeyParser.TryBuildSiteKey(parsed);
            if (siteKey == null)
                return true;

            LaunchPadVesselActivityTracker.Touch(vesselId);

            var owner = ResolveVesselOwner(vesselId);
            if (string.IsNullOrEmpty(owner))
                owner = client.PlayerName;

            var now = DateTime.UtcNow;
            if (LaunchPadReservationRegistry.IsBlockedByOtherReservation(siteKey, owner, now))
            {
                if (!(mode == LaunchPadCoordinationMode.LockAndOverflowBubble && IsOverflowActive(BuildOccupancyEntriesUnlocked(now))))
                {
                    denyReason = $"Launch site '{siteKey}' is reserved by another player.";
                    return false;
                }
            }

            var otherOccupant = FindOccupantForSite(siteKey, vesselId, now);
            if (otherOccupant == null)
            {
                LaunchPadReservationRegistry.OnProtoAcceptedForSite(owner, siteKey);
                return true;
            }

            if (string.Equals(otherOccupant.Value.player, owner, StringComparison.Ordinal))
            {
                LaunchPadReservationRegistry.OnProtoAcceptedForSite(owner, siteKey);
                return true;
            }

            if (mode == LaunchPadCoordinationMode.LockAndOverflowBubble &&
                IsOverflowActive(BuildOccupancyEntriesUnlocked(now)))
            {
                LaunchPadReservationRegistry.OnProtoAcceptedForSite(owner, siteKey);
                return true;
            }

            denyReason = $"Launch site '{siteKey}' is in use by {otherOccupant.Value.player}.";
            return false;
        }

        public static void BroadcastSnapshot()
        {
            BroadcastSmart(forceSnapshot: true);
        }

        public static void BroadcastSmart(bool forceSnapshot = false)
        {
            var mode = GeneralSettings.SettingsStore.LaunchPadCoordinationMode;
            if (mode == LaunchPadCoordinationMode.Off)
                return;

            var now = DateTime.UtcNow;
            LaunchPadReservationRegistry.ClearExpired();
            var entries = BuildOccupancyEntriesUnlocked(now);
            var overflow = mode == LaunchPadCoordinationMode.LockAndOverflowBubble && IsOverflowActive(entries);
            var effectiveBubble = GeneralSettings.SettingsStore.SafetyBubbleDistance;
            if (overflow)
                effectiveBubble = Math.Max(effectiveBubble, GeneralSettings.SettingsStore.LaunchPadOverflowBubbleDistance);

            lock (SnapshotGate)
            {
                if (!forceSnapshot && _lastSentEntries.Count > 0 && TryBuildDelta(_lastSentEntries, _lastSentOverflow, _lastSentBubble, entries, overflow, effectiveBubble, out var delta))
                {
                    MessageQueuer.SendToAllClients<LaunchPadSrvMsg>(delta);
                    _lastSentEntries = CloneEntries(entries);
                    _lastSentOverflow = overflow;
                    _lastSentBubble = effectiveBubble;
                    return;
                }

                var snap = ServerContext.ServerMessageFactory.CreateNewMessageData<LaunchPadOccupancySnapshotMsgData>();
                snap.OverflowBubbleActive = overflow;
                snap.EffectiveSafetyBubbleDistance = effectiveBubble;
                snap.Entries.Clear();
                snap.Entries.AddRange(entries);
                MessageQueuer.SendToAllClients<LaunchPadSrvMsg>(snap);
                _lastSentEntries = CloneEntries(entries);
                _lastSentOverflow = overflow;
                _lastSentBubble = effectiveBubble;
            }
        }

        public static void SendSnapshotToClient(ClientStructure client)
        {
            var mode = GeneralSettings.SettingsStore.LaunchPadCoordinationMode;
            if (mode == LaunchPadCoordinationMode.Off)
                return;

            var now = DateTime.UtcNow;
            LaunchPadReservationRegistry.ClearExpired();
            var entries = BuildOccupancyEntriesUnlocked(now);
            var overflow = mode == LaunchPadCoordinationMode.LockAndOverflowBubble && IsOverflowActive(entries);
            var effectiveBubble = GeneralSettings.SettingsStore.SafetyBubbleDistance;
            if (overflow)
                effectiveBubble = Math.Max(effectiveBubble, GeneralSettings.SettingsStore.LaunchPadOverflowBubbleDistance);

            var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<LaunchPadOccupancySnapshotMsgData>();
            msgData.OverflowBubbleActive = overflow;
            msgData.EffectiveSafetyBubbleDistance = effectiveBubble;
            msgData.Entries.Clear();
            msgData.Entries.AddRange(entries);
            MessageQueuer.SendToClient<LaunchPadSrvMsg>(client, msgData);

            lock (SnapshotGate)
            {
                _lastSentEntries = CloneEntries(entries);
                _lastSentOverflow = overflow;
                _lastSentBubble = effectiveBubble;
            }
        }

        public static void SendLaunchDenied(ClientStructure client, string reason)
        {
            var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<LaunchPadLaunchDeniedMsgData>();
            msgData.Reason = reason;
            MessageQueuer.SendToClient<LaunchPadSrvMsg>(client, msgData);
        }

        public static void OnVesselProtoStored(Guid vesselId)
        {
            LaunchPadVesselActivityTracker.Touch(vesselId);
        }

        public static void OnVesselRemoved(Guid vesselId)
        {
            LaunchPadVesselActivityTracker.Remove(vesselId);
        }

        public static void BroadcastIfLockCanChangeOccupancy(LockDefinition lockDefinition)
        {
            if (!CanLockChangeOccupancy(lockDefinition))
                return;

            BroadcastSmart();
        }

        internal static bool CanLockChangeOccupancy(LockDefinition lockDefinition)
        {
            if (lockDefinition == null || lockDefinition.VesselId == Guid.Empty)
                return false;

            return lockDefinition.Type == LockType.Control || lockDefinition.Type == LockType.Update;
        }

        private static bool TryBuildDelta(
            List<LaunchPadOccupancyEntry> oldEntries,
            bool oldOverflow,
            float oldBubble,
            List<LaunchPadOccupancyEntry> newEntries,
            bool newOverflow,
            float newBubble,
            out LaunchPadOccupancyDeltaMsgData delta)
        {
            delta = null;
            if (oldEntries.Count == 0)
                return false;

            if (oldEntries.Any(e => e.VesselId == Guid.Empty) || newEntries.Any(e => e.VesselId == Guid.Empty))
                return false;

            if (oldOverflow != newOverflow || Math.Abs(oldBubble - newBubble) > 0.01f)
                return false;

            var ops = new List<LaunchPadDeltaOperation>();
            var oldSet = oldEntries.ToDictionary(e => e.VesselId, e => e);
            var newSet = newEntries.ToDictionary(e => e.VesselId, e => e);

            foreach (var kv in newSet)
            {
                if (!oldSet.TryGetValue(kv.Key, out var prev) || !EntryEquals(prev, kv.Value))
                    ops.Add(new LaunchPadDeltaOperation { Kind = 0, Entry = kv.Value });
            }

            foreach (var kv in oldSet)
            {
                if (!newSet.ContainsKey(kv.Key))
                    ops.Add(new LaunchPadDeltaOperation { Kind = 1, RemoveVesselId = kv.Key });
            }

            if (ops.Count == 0 || ops.Count > MaxDeltaOpsBeforeSnapshot)
                return false;

            delta = ServerContext.ServerMessageFactory.CreateNewMessageData<LaunchPadOccupancyDeltaMsgData>();
            delta.OverflowBubbleActive = newOverflow;
            delta.EffectiveSafetyBubbleDistance = newBubble;
            delta.Operations.AddRange(ops);
            return true;
        }

        private static bool EntryEquals(LaunchPadOccupancyEntry a, LaunchPadOccupancyEntry b) =>
            a.SiteKey == b.SiteKey && a.PlayerName == b.PlayerName && a.VesselId == b.VesselId;

        private static List<LaunchPadOccupancyEntry> CloneEntries(List<LaunchPadOccupancyEntry> src)
        {
            var list = new List<LaunchPadOccupancyEntry>(src.Count);
            foreach (var e in src)
                list.Add(new LaunchPadOccupancyEntry { SiteKey = e.SiteKey, PlayerName = e.PlayerName, VesselId = e.VesselId });
            return list;
        }

        private static List<LaunchPadOccupancyEntry> BuildOccupancyEntriesUnlocked(DateTime nowUtc)
        {
            var list = new List<LaunchPadOccupancyEntry>();
            var leaseSec = GeneralSettings.SettingsStore.LaunchPadLeaseTimeoutSeconds;

            foreach (var kv in VesselStoreSystem.CurrentVessels)
            {
                var siteKey = LaunchSiteKeyParser.TryBuildSiteKey(kv.Value);
                if (siteKey == null) continue;

                if (LaunchPadVesselActivityTracker.IsStale(kv.Key, leaseSec, nowUtc))
                    continue;

                var owner = ResolveVesselOwner(kv.Key);
                if (string.IsNullOrEmpty(owner))
                    continue;

                list.Add(new LaunchPadOccupancyEntry
                {
                    SiteKey = siteKey,
                    PlayerName = owner,
                    VesselId = kv.Key
                });
            }

            LaunchPadReservationRegistry.AppendReservationEntries(list, nowUtc);
            return list;
        }

        private static bool IsOverflowActive(List<LaunchPadOccupancyEntry> entries)
        {
            var slots = GeneralSettings.SettingsStore.LaunchPadConcurrentSlots;
            if (slots <= 0) return false;
            return entries.Count >= slots;
        }

        private static (string player, Guid vesselId)? FindOccupantForSite(string siteKey, Guid excludingVesselId, DateTime nowUtc)
        {
            foreach (var kv in VesselStoreSystem.CurrentVessels)
            {
                if (kv.Key == excludingVesselId) continue;
                var sk = LaunchSiteKeyParser.TryBuildSiteKey(kv.Value);
                if (sk == null) continue;
                if (!string.Equals(sk, siteKey, StringComparison.Ordinal)) continue;

                if (LaunchPadVesselActivityTracker.IsStale(kv.Key, GeneralSettings.SettingsStore.LaunchPadLeaseTimeoutSeconds, nowUtc))
                    continue;

                var owner = ResolveVesselOwner(kv.Key);
                if (!string.IsNullOrEmpty(owner))
                    return (owner, kv.Key);
            }

            return null;
        }

        private static string ResolveVesselOwner(Guid vesselId)
        {
            var control = LockSystem.LockQuery.GetControlLockOwner(vesselId);
            if (!string.IsNullOrEmpty(control))
                return control;

            if (LockSystem.LockQuery.UpdateLockExists(vesselId))
                return LockSystem.LockQuery.GetUpdateLockOwner(vesselId);

            return null;
        }
    }
}
