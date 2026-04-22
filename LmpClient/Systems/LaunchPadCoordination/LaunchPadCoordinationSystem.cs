using System;
using System.Collections.Generic;
using LmpClient.Base;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using LmpCommon.Message.Data.LaunchPad;

namespace LmpClient.Systems.LaunchPadCoordination
{
    public class LaunchPadCoordinationSystem : MessageSystem<LaunchPadCoordinationSystem, LaunchPadMessageSender, LaunchPadMessageHandler>
    {
        private readonly object _padLock = new object();
        private readonly List<LaunchPadOccupancyEntry> _entries = new List<LaunchPadOccupancyEntry>();
        private bool _overflowBubbleActive;
        private float _effectiveSafetyBubbleDistance;

        public override string SystemName { get; } = nameof(LaunchPadCoordinationSystem);

        protected override ClientState EnableStage => ClientState.Connected;

        public bool OverflowBubbleActive
        {
            get
            {
                lock (_padLock)
                    return _overflowBubbleActive;
            }
        }

        public float EffectiveSafetyBubbleDistance
        {
            get
            {
                lock (_padLock)
                    return _effectiveSafetyBubbleDistance;
            }
        }

        public void ApplySnapshot(LaunchPadOccupancySnapshotMsgData snap)
        {
            lock (_padLock)
            {
                _overflowBubbleActive = snap.OverflowBubbleActive;
                _effectiveSafetyBubbleDistance = snap.EffectiveSafetyBubbleDistance;
                _entries.Clear();
                _entries.AddRange(snap.Entries);
            }
        }

        public void ApplyDelta(LaunchPadOccupancyDeltaMsgData delta)
        {
            lock (_padLock)
            {
                _overflowBubbleActive = delta.OverflowBubbleActive;
                _effectiveSafetyBubbleDistance = delta.EffectiveSafetyBubbleDistance;

                foreach (var op in delta.Operations)
                {
                    if (op.Kind == 0)
                    {
                        _entries.RemoveAll(e => e.VesselId == op.Entry.VesselId);
                        _entries.Add(op.Entry);
                    }
                    else if (op.Kind == 1)
                    {
                        _entries.RemoveAll(e => e.VesselId == op.RemoveVesselId);
                    }
                }
            }
        }

        public void ClearFromSettings()
        {
            lock (_padLock)
            {
                _entries.Clear();
                _overflowBubbleActive = false;
                _effectiveSafetyBubbleDistance = 0f;
            }
        }

        /// <summary>Distance used for safety-bubble geometry when overflow is active (server-authoritative).</summary>
        public float GetEffectiveSafetyBubbleForRendering()
        {
            if (SettingsSystem.ServerSettings.LaunchPadCoordMode != LaunchPadCoordinationMode.LockAndOverflowBubble)
                return SettingsSystem.ServerSettings.SafetyBubbleDistance;

            lock (_padLock)
            {
                if (!_overflowBubbleActive)
                    return SettingsSystem.ServerSettings.SafetyBubbleDistance;

                return _effectiveSafetyBubbleDistance > 0f
                    ? _effectiveSafetyBubbleDistance
                    : SettingsSystem.ServerSettings.SafetyBubbleDistance;
            }
        }

        public bool IsSiteBlockedForLocalPlayer(string siteKey)
        {
            if (string.IsNullOrEmpty(siteKey))
                return false;

            var mode = SettingsSystem.ServerSettings.LaunchPadCoordMode;
            if (mode == LaunchPadCoordinationMode.Off)
                return false;

            if (mode == LaunchPadCoordinationMode.LockAndOverflowBubble && OverflowBubbleActive)
                return false;

            var self = SettingsSystem.CurrentSettings.PlayerName;
            lock (_padLock)
            {
                foreach (var e in _entries)
                {
                    if (e.SiteKey != siteKey)
                        continue;
                    if (!string.Equals(e.PlayerName, self, System.StringComparison.Ordinal))
                        return true;
                }
            }

            return false;
        }

        /// <summary>When the pad is blocked for the local player, returns one occupying player name from server occupancy (vessel or reservation row).</summary>
        public bool TryGetBlockingOccupant(string siteKey, out string otherPlayerName)
        {
            otherPlayerName = null;
            if (string.IsNullOrEmpty(siteKey))
                return false;

            var mode = SettingsSystem.ServerSettings.LaunchPadCoordMode;
            if (mode == LaunchPadCoordinationMode.Off)
                return false;

            if (mode == LaunchPadCoordinationMode.LockAndOverflowBubble && OverflowBubbleActive)
                return false;

            var self = SettingsSystem.CurrentSettings.PlayerName;
            lock (_padLock)
            {
                foreach (var e in _entries)
                {
                    if (e.SiteKey != siteKey)
                        continue;
                    if (string.Equals(e.PlayerName, self, System.StringComparison.Ordinal))
                        continue;
                    otherPlayerName = string.IsNullOrEmpty(e.PlayerName) ? "Another player" : e.PlayerName;
                    return true;
                }
            }

            return false;
        }
    }
}
