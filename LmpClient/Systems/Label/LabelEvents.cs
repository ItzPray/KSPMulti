using KSP.UI.Screens;
using KSP.UI.Screens.Mapview;
using HarmonyLib;
using LmpClient;
using LmpClient.Base;
using LmpClient.Systems.Lock;
using LmpCommon.Enums;
using LmpCommon.Locks;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LmpClient.Systems.Label
{
    public class LabelEvents : SubSystem<LabelSystem>
    {
        private static readonly FieldInfo VesselLabelsField = AccessTools.Field(typeof(VesselLabels), "labels");
        private static readonly Dictionary<Guid, string> AppliedFlightLabelOwnersByVessel = new Dictionary<Guid, string>();

        private static bool ShouldApplyFlightLabels =>
            MainSystem.Singleton != null && MainSystem.NetworkState >= ClientState.Connected;

        /// <summary>
        /// <c>Resources.FindObjectsOfTypeAll&lt;VesselLabel&gt;</c> can return stale instances outside flight;
        /// only touch labels while the flight scene is active.
        /// </summary>
        private static bool ShouldScanFlightVesselLabels =>
            ShouldApplyFlightLabels && HighLogic.LoadedScene == GameScenes.FLIGHT;

        public void OnLabelProcessed(BaseLabel label)
        {
            if (label is VesselLabel vesselLabel)
                ApplyControlOwnerToFlightLabel(vesselLabel);
        }

        /// <summary>
        /// Control locks can arrive after <see cref="VesselLabels.ProcessLabel"/> last ran; stock does not
        /// automatically rebuild labels when only the lock table changes, so re-apply when locks change.
        /// </summary>
        public void OnLockAcquire(LockDefinition lockDefinition)
        {
            VesselLabelLockDiagnostics.OnLockEvent("OnLockAcquire", lockDefinition);

            if (!ShouldScanFlightVesselLabels || lockDefinition.Type != LockType.Control)
                return;

            RefreshFlightVesselLabelsForVessel(lockDefinition.VesselId, stripReleasedOwner: null);
        }

        public void OnLockRelease(LockDefinition lockDefinition)
        {
            VesselLabelLockDiagnostics.OnLockEvent("OnLockRelease", lockDefinition);

            if (!ShouldScanFlightVesselLabels || lockDefinition.Type != LockType.Control)
                return;

            RefreshFlightVesselLabelsForVessel(lockDefinition.VesselId, lockDefinition.PlayerName);
        }

        /// <summary>
        /// Full lock snapshot applied (no per-lock acquire events); refresh all flight labels once.
        /// </summary>
        public void OnLockListApplied()
        {
            VesselLabelLockDiagnostics.OnLockListAppliedDiag();

            if (!ShouldScanFlightVesselLabels)
                return;

            RefreshAllFlightVesselLabels();
        }

        /// <summary>
        /// When entering <see cref="ClientState.Running"/>, labels may have been built before lock snapshot
        /// handlers ran; align once with the current lock store.
        /// </summary>
        public void RefreshFlightLabelsAfterLabelSystemEnabled()
        {
            if (!ShouldScanFlightVesselLabels)
                return;

            RefreshAllFlightVesselLabels();
        }

        /// <summary>
        /// Final per-frame convergence point for flight HUD labels. This runs after normal Update/Harmony label
        /// writers, so the visible text is reconciled to the current lock store even if another mod rebuilt it.
        /// </summary>
        public void RefreshFlightLabelsLateUpdate()
        {
            if (!ShouldScanFlightVesselLabels)
                return;

            var labels = GetFlightVesselLabels();
            var seenVesselIds = VesselLabelLockDiagnostics.IsEnabled ? new HashSet<Guid>() : null;

            for (var i = 0; i < labels.Count; i++)
            {
                var vl = labels[i];
                if (vl?.vessel == null)
                    continue;

                seenVesselIds?.Add(vl.vessel.id);
                VesselLabelLockDiagnostics.OnFlightLabelLockTextMismatchBeforeLockRefresh(vl, "LateUpdateReconcile");
                ApplyControlOwnerToFlightLabel(vl);
                VesselLabelLockDiagnostics.OnLateUpdateFlightLabelVisibleState(vl, "LateUpdateReconcile");
            }

            VesselLabelLockDiagnostics.OnLateUpdateFlightLabelScan("LateUpdateReconcile", labels.Count, seenVesselIds);
        }

        private static void RefreshAllFlightVesselLabels()
        {
            var labels = GetFlightVesselLabels();
            for (var i = 0; i < labels.Count; i++)
            {
                var vl = labels[i];
                if (vl?.vessel == null)
                    continue;

                VesselLabelLockDiagnostics.OnFlightLabelLockTextMismatchBeforeLockRefresh(vl, "OnLockListAppliedOrLabelSystemEnabled");
                ApplyControlOwnerToFlightLabel(vl);
            }
        }

        private static void RefreshFlightVesselLabelsForVessel(Guid vesselId, string stripReleasedOwner)
        {
            var labels = GetFlightVesselLabels();
            for (var i = 0; i < labels.Count; i++)
            {
                var vl = labels[i];
                if (vl?.vessel == null || vl.vessel.id != vesselId)
                    continue;

                if (!string.IsNullOrEmpty(stripReleasedOwner))
                    StripLeadingOwnerLine(vl, stripReleasedOwner);

                VesselLabelLockDiagnostics.OnFlightLabelLockTextMismatchBeforeLockRefresh(
                    vl,
                    stripReleasedOwner == null ? "OnLockAcquire" : "OnLockRelease");

                ApplyControlOwnerToFlightLabel(vl);
            }
        }

        private static void StripLeadingOwnerLine(VesselLabel vesselLabel, string formerOwner)
        {
            if (vesselLabel?.vessel == null || vesselLabel.text == null || string.IsNullOrEmpty(formerOwner))
                return;

            var text = vesselLabel.text.text ?? string.Empty;
            var prefix = formerOwner + "\n";
            if (text.StartsWith(prefix, StringComparison.Ordinal))
                vesselLabel.text.text = text.Length > prefix.Length ? text.Substring(prefix.Length) : string.Empty;

            AppliedFlightLabelOwnersByVessel.Remove(vesselLabel.vessel.id);
        }

        private static void ApplyControlOwnerToFlightLabel(VesselLabel vesselLabel)
        {
            if (vesselLabel?.vessel == null || vesselLabel.text == null)
                return;

            var vesselId = vesselLabel.vessel.id;
            var owner = LockSystem.LockQuery.GetControlLockOwner(vesselId);
            var text = vesselLabel.text.text ?? string.Empty;

            AppliedFlightLabelOwnersByVessel.TryGetValue(vesselId, out var appliedOwner);
            if (!string.IsNullOrEmpty(appliedOwner) && appliedOwner != owner)
            {
                text = StripPrefix(text, appliedOwner);
                vesselLabel.text.text = text;
                AppliedFlightLabelOwnersByVessel.Remove(vesselId);
            }

            if (string.IsNullOrEmpty(owner))
                return;

            var ownerPrefix = owner + "\n";
            while (text.StartsWith(ownerPrefix + ownerPrefix, StringComparison.Ordinal))
            {
                text = text.Substring(ownerPrefix.Length);
                vesselLabel.text.text = text;
            }

            if (text.StartsWith(ownerPrefix, StringComparison.Ordinal))
            {
                AppliedFlightLabelOwnersByVessel[vesselId] = owner;
                return;
            }

            vesselLabel.text.text = ownerPrefix + text;
            AppliedFlightLabelOwnersByVessel[vesselId] = owner;
            VesselLabelLockDiagnostics.OnApplyStillWrong(vesselLabel);
        }

        private static string StripPrefix(string text, string owner)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(owner))
                return text ?? string.Empty;

            var prefix = owner + "\n";
            return text.StartsWith(prefix, StringComparison.Ordinal)
                ? text.Substring(prefix.Length)
                : text;
        }

        private static List<VesselLabel> GetFlightVesselLabels()
        {
            var result = new List<VesselLabel>();
            var vesselLabels = VesselLabels.Instance;
            if (vesselLabels != null)
            {
                var labels = VesselLabelsField?.GetValue(vesselLabels) as List<BaseLabel>;
                if (labels != null)
                {
                    for (var i = 0; i < labels.Count; i++)
                    {
                        if (labels[i] is VesselLabel vesselLabel)
                            result.Add(vesselLabel);
                    }

                    return result;
                }
            }

            var resourceLabels = Resources.FindObjectsOfTypeAll<VesselLabel>();
            for (var i = 0; i < resourceLabels.Length; i++)
                result.Add(resourceLabels[i]);

            return result;
        }

        public void OnMapLabelProcessed(Vessel vessel, MapNode.CaptionData label)
        {
            if (vessel == null) return;

            var owner = LockSystem.LockQuery.GetControlLockOwner(vessel.id);
            if (!string.IsNullOrEmpty(owner))
            {
                label.Header = $"{owner}\n{label.Header}";
            }
        }

        public void OnMapWidgetTextProcessed(TrackingStationWidget widget)
        {
            if (widget.vessel == null) return;

            var owner = LockSystem.LockQuery.GetControlLockOwner(widget.vessel.id);
            if (!string.IsNullOrEmpty(owner))
            {
                widget.textName.text = $"({owner}) {widget.textName.text}";
            }
        }
    }
}
