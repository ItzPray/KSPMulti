using KSP.UI.Screens;
using KSP.UI.Screens.Mapview;
using HarmonyLib;
using LmpClient;
using LmpClient.Systems.Lock;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;

using LmpCommon.Locks;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;


namespace LmpClient.Systems.Label

{

    /// <summary>

    /// Opt-in (Status window <b>D8</b>, <see cref="SettingStructure.Debug8"/>): loud console checks for

    /// control-lock vs vessel-label / map-caption / tracking-widget text (Harmony ordering, stale UI, other mods).

    /// <para>Grep KSP / Player.log for <see cref="LogTag"/> (single token, easy to filter).</para>

    /// </summary>

    public static class VesselLabelLockDiagnostics

    {

        /// <summary>Stable grep token for all D8 vessel-HUD label lines (errors, warnings, banner).</summary>

        public const string LogTag = "KSPMP_VESSEL_LABEL_HUD_DIAG";



        public static bool IsEnabled => SettingsSystem.CurrentSettings.Debug8;



        private const float DefaultThrottleSeconds = 2.5f;

        private static readonly Dictionary<string, float> LastLogTime = new Dictionary<string, float>();



        private struct AfterLmpProbe

        {

            public int Frame;

            public bool HadOwnerPrefix;

        }



        private static readonly Dictionary<Guid, AfterLmpProbe> AfterLmpPrefixByVessel = new Dictionary<Guid, AfterLmpProbe>();



        /// <summary>Stage string passed from <see cref="Harmony.VesselLabels_ProcessLabel"/> after KSPMP reapplies the owner line.</summary>
        public const string AfterLmpHarmonyDiagnosticStage = "HarmonyPostfixAfterLmpProcessLabel";



        public static void LogEnabledBanner()
        {
            if (!IsEnabled)
                return;

            LunaLog.LogError(

                "[KSPMP] " + LogTag +
                " | code=D8_SESSION_ENABLED | hint=Grep Player.log/KSP.log for token " + LogTag +
                " | turn_off=D8 in KSPMP Status when done");

            LogVesselLabelHarmonyPatchOrder();
        }


        public static void OnLabelSystemEnabled()
        {
            if (!IsEnabled)
                return;

            LunaLog.LogWarning(

                "[KSPMP] " + LogTag +
                " | code=LABEL_SYSTEM_ENABLED | scene=" + HighLogic.LoadedScene +
                " | network=" + (MainSystem.Singleton != null ? MainSystem.NetworkState.ToString() : "no_MainSystem"));

            LogVesselLabelHarmonyPatchOrder();
        }

        public static void LogVesselLabelHarmonyPatchOrder()
        {
            if (!IsEnabled || !ShouldLog("VESSEL_LABEL_HARMONY_POSTFIXES", 15f))
                return;

            var original = AccessTools.Method(
                typeof(VesselLabels),
                "ProcessLabel",
                new[] { typeof(BaseLabel), typeof(Vessel), typeof(ITargetable), typeof(Vector3) });

            var patches = original != null ? HarmonyLib.Harmony.GetPatchInfo(original) : null;
            if (patches == null)
            {
                LunaLog.LogWarning("[KSPMP] " + LogTag + " | code=VESSEL_LABEL_HARMONY_POSTFIXES | postfixes=<none>");
                return;
            }

            var postfixesField = typeof(Patches).GetField("Postfixes", BindingFlags.Public | BindingFlags.Instance);
            var postfixes = postfixesField?.GetValue(patches) as IEnumerable<Patch>;
            var sb = new StringBuilder(1024);
            sb.Append("[KSPMP] ").Append(LogTag).Append(" | code=VESSEL_LABEL_HARMONY_POSTFIXES | postfixes=");

            var first = true;
            if (postfixes != null)
            {
                foreach (var patch in postfixes)
                {
                    if (!first)
                        sb.Append(" ; ");

                    first = false;
                    sb.Append("owner=").Append(patch.owner)
                        .Append(",priority=").Append(patch.priority)
                        .Append(",index=").Append(patch.index)
                        .Append(",method=").Append(patch.PatchMethod?.DeclaringType?.FullName)
                        .Append(".").Append(patch.PatchMethod?.Name);
                }
            }

            if (first)
                sb.Append("<none>");

            LunaLog.LogWarning(sb.ToString());
        }


        public static void OnLockEvent(string eventName, LockDefinition lockDefinition)

        {

            if (!IsEnabled || lockDefinition == null || lockDefinition.Type != LockType.Control)

                return;



            if (MainSystem.NetworkState < ClientState.Connected)

                return;



            var key = "LOCK_EVT:" + eventName;

            if (!ShouldLog(key, 2.5f))

                return;



            LunaLog.LogWarning(

                "[KSPMP] " + LogTag +

                " | code=LOCK_EVENT | name=" + eventName +

                " | vesselId=" + lockDefinition.VesselId.ToString("N") +

                " | player=" + (lockDefinition.PlayerName ?? "<null>"));

        }



        public static void OnLockListAppliedDiag()

        {

            if (!IsEnabled)

                return;



            if (MainSystem.NetworkState < ClientState.Connected)

                return;



            if (!ShouldLog("LOCK_LIST_APPLIED", 3f))

                return;



            LunaLog.LogWarning("[KSPMP] " + LogTag + " | code=LOCK_LIST_APPLIED | hint=flight labels refresh scheduled");

        }



        /// <summary>

        /// Runs in a Harmony postfix ordered <b>before</b> <see cref="Harmony.VesselLabels_ProcessLabel"/> (before KSPMP injects the owner line).

        /// </summary>

        public static void AfterStockBeforeLmpEvent(VesselLabel vesselLabel)

        {

            if (!IsEnabled || vesselLabel?.vessel == null || vesselLabel.text == null)

                return;



            if (MainSystem.NetworkState < ClientState.Connected)

                return;



            if (HighLogic.LoadedScene != GameScenes.FLIGHT)

                return;



            var owner = LockSystem.LockQuery.GetControlLockOwner(vesselLabel.vessel.id);

            if (string.IsNullOrEmpty(owner))

                return;



            var text = vesselLabel.text.text ?? string.Empty;

            if (!text.StartsWith(owner + "\n", StringComparison.Ordinal))

                return;



            var key = $"{vesselLabel.vessel.id:N}:PREFIX_BEFORE_LMP_UNEXPECTED";

            if (!ShouldLog(key, 15f))

                return;



            LunaLog.LogWarning(BuildMessage(

                "OWNER_PREFIX_PRESENT_BEFORE_LMP_EVENT",

                "after_stock_before_LmpPostfix",

                vesselLabel.vessel.vesselName,

                vesselLabel.vessel.id,

                owner,

                text));

        }



        public static void AfterFlightLabelPipeline(VesselLabel vesselLabel, string stage)

        {

            if (!IsEnabled || vesselLabel?.vessel == null || vesselLabel.text == null)

                return;



            if (MainSystem.NetworkState < ClientState.Connected)

                return;



            if (HighLogic.LoadedScene != GameScenes.FLIGHT)

                return;



            var vesselId = vesselLabel.vessel.id;

            var owner = LockSystem.LockQuery.GetControlLockOwner(vesselId);

            var text = vesselLabel.text.text ?? string.Empty;



            if (string.IsNullOrEmpty(owner))

            {

                AfterLmpPrefixByVessel.Remove(vesselId);

                return;

            }



            if (text.StartsWith(owner + "\n" + owner + "\n", StringComparison.Ordinal))

            {

                var dupKey = $"{vesselId:N}:FLIGHT_DUP_PREFIX:{stage}";

                if (ShouldLog(dupKey, 4f))

                {

                    LunaLog.LogError(BuildMessage(

                        "FLIGHT_LABEL_DUPLICATE_OWNER_PREFIX",

                        stage,

                        vesselLabel.vessel.vesselName,

                        vesselId,

                        owner,

                        text));

                }

            }



            if (text.StartsWith(owner + "\n", StringComparison.Ordinal))

            {

                if (stage == AfterLmpHarmonyDiagnosticStage)

                    RecordAfterLmpPrefixState(vesselId, hadOwnerPrefix: true);

                return;

            }



            var key = $"{vesselId:N}:FLIGHT:{stage}";

            if (ShouldLog(key, DefaultThrottleSeconds))

            {

                LunaLog.LogError(BuildMessage(

                    "FLIGHT_LABEL_MISMATCH",

                    stage,

                    vesselLabel.vessel.vesselName,

                    vesselId,

                    owner,

                    text));

            }



            if (stage == AfterLmpHarmonyDiagnosticStage)

                RecordAfterLmpPrefixState(vesselId, hadOwnerPrefix: false);

        }



        public static void CheckVeryLateHarmonyStrip(VesselLabel vesselLabel)

        {

            if (!IsEnabled || vesselLabel?.vessel == null || vesselLabel.text == null)

                return;



            if (MainSystem.NetworkState < ClientState.Connected)

                return;



            if (HighLogic.LoadedScene != GameScenes.FLIGHT)

                return;



            var vesselId = vesselLabel.vessel.id;

            var owner = LockSystem.LockQuery.GetControlLockOwner(vesselId);

            if (string.IsNullOrEmpty(owner))

                return;



            var text = vesselLabel.text.text ?? string.Empty;

            if (text.StartsWith(owner + "\n", StringComparison.Ordinal))

                return;



            if (!AfterLmpPrefixByVessel.TryGetValue(vesselId, out var probe) || probe.Frame != Time.frameCount)

                return;



            if (!probe.HadOwnerPrefix)

                return;



            if (!ShouldLog($"{vesselId:N}:STRIPPED_AFTER_LMP", DefaultThrottleSeconds))

                return;



            LunaLog.LogError(BuildMessage(

                "FLIGHT_LABEL_OWNER_STRIPPED_AFTER_LMP_HARMONY",

                "HarmonyVeryLateAfterLmpChain",

                vesselLabel.vessel.vesselName,

                vesselId,

                owner,

                text));

        }



        private static void RecordAfterLmpPrefixState(Guid vesselId, bool hadOwnerPrefix)

        {

            AfterLmpPrefixByVessel[vesselId] = new AfterLmpProbe { Frame = Time.frameCount, HadOwnerPrefix = hadOwnerPrefix };

        }



        public static void AfterMapCaptionPipeline(Vessel vessel, MapNode.CaptionData data, string stage)

        {

            if (!IsEnabled || vessel == null || data == null)

                return;



            if (MainSystem.NetworkState < ClientState.Connected)

                return;



            var owner = LockSystem.LockQuery.GetControlLockOwner(vessel.id);

            if (string.IsNullOrEmpty(owner))

                return;



            var header = data.Header ?? string.Empty;

            var expectedPrefix = owner + "\n";

            if (header.StartsWith(expectedPrefix, StringComparison.Ordinal))

                return;



            var key = $"{vessel.id:N}:MAP_CAPTION:{stage}";

            if (!ShouldLog(key, DefaultThrottleSeconds))

                return;



            LunaLog.LogError(BuildMessage(

                "MAP_ORBIT_CAPTION_MISMATCH",

                stage,

                vessel.vesselName,

                vessel.id,

                owner,

                header));

        }



        public static void AfterTrackingWidgetPipeline(TrackingStationWidget widget, string stage)

        {

            if (!IsEnabled || widget?.vessel == null || widget.textName == null)

                return;



            if (MainSystem.NetworkState < ClientState.Connected)

                return;



            var owner = LockSystem.LockQuery.GetControlLockOwner(widget.vessel.id);

            if (string.IsNullOrEmpty(owner))

                return;



            var text = widget.textName.text ?? string.Empty;

            var expectedPrefix = $"({owner}) ";

            if (text.StartsWith(expectedPrefix, StringComparison.Ordinal))

                return;



            var key = $"{widget.vessel.id:N}:TRACK_WIDGET:{stage}";

            if (!ShouldLog(key, DefaultThrottleSeconds))

                return;



            LunaLog.LogError(BuildMessage(

                "TRACKING_STATION_WIDGET_MISMATCH",

                stage,

                widget.vessel.vesselName,

                widget.vessel.id,

                owner,

                text));

        }



        public static void OnFlightLabelLockTextMismatchBeforeLockRefresh(VesselLabel vesselLabel, string context)
        {
            if (!IsEnabled || vesselLabel?.vessel == null || vesselLabel.text == null)
                return;


            if (HighLogic.LoadedScene != GameScenes.FLIGHT)

                return;



            var vesselId = vesselLabel.vessel.id;

            var owner = LockSystem.LockQuery.GetControlLockOwner(vesselId);

            var text = vesselLabel.text.text ?? string.Empty;



            if (string.IsNullOrEmpty(owner))

                return;



            var expectedPrefix = owner + "\n";

            if (text.StartsWith(expectedPrefix, StringComparison.Ordinal))

                return;



            var key = $"{vesselId:N}:STALE_BEFORE_REFRESH:{context}";

            if (!ShouldLog(key, DefaultThrottleSeconds))

                return;



            LunaLog.LogError(BuildMessage(

                "STALE_FLIGHT_LABEL_BEFORE_LOCK_REFRESH",

                context,

                vesselLabel.vessel.vesselName,

                vesselId,

                owner,

                text));
        }

        public static void OnLateUpdateFlightLabelScan(string context, int labelCount, HashSet<Guid> seenVesselIds)
        {
            if (!IsEnabled || seenVesselIds == null)
                return;

            if (HighLogic.LoadedScene != GameScenes.FLIGHT || MainSystem.NetworkState < ClientState.Connected)
                return;

            var controlLocks = LockSystem.LockQuery.GetAllControlLocks();
            var controlLockCount = 0;
            var controlledLabelsSeen = 0;

            foreach (var controlLock in controlLocks)
            {
                controlLockCount++;
                if (seenVesselIds.Contains(controlLock.VesselId))
                {
                    controlledLabelsSeen++;
                    continue;
                }

                var vessel = FlightGlobals.FindVessel(controlLock.VesselId);
                if (vessel == null)
                    continue;

                var key = $"{controlLock.VesselId:N}:CONTROL_LOCK_WITHOUT_FLIGHT_LABEL";
                if (ShouldLog(key, 5f))
                {
                    LunaLog.LogError(
                        "[KSPMP] " + LogTag +
                        " | code=CONTROL_LOCK_WITHOUT_FLIGHT_LABEL" +
                        " | stage=" + context +
                        " | vessel=" + vessel.vesselName +
                        " | id=" + controlLock.VesselId.ToString("N") +
                        " | controlLockOwner=" + (controlLock.PlayerName ?? "<null>") +
                        " | flightLabelCount=" + labelCount +
                        " | hint=Lock store has a control owner and the vessel exists locally, but VesselLabels has no flight label object for it. Check stock label visibility/culling and UI-label mods.");
                }
            }

            if (ShouldLog("LATE_SCAN_SUMMARY", 10f))
            {
                LunaLog.LogWarning(
                    "[KSPMP] " + LogTag +
                    " | code=LATE_SCAN_SUMMARY" +
                    " | stage=" + context +
                    " | flightLabelCount=" + labelCount +
                    " | controlLockCount=" + controlLockCount +
                    " | controlledLabelsSeen=" + controlledLabelsSeen +
                    " | scene=" + HighLogic.LoadedScene);
            }
        }


        public static void OnLateUpdateFlightLabelVisibleState(VesselLabel vesselLabel, string context)
        {
            if (!IsEnabled || vesselLabel?.vessel == null)
                return;

            if (HighLogic.LoadedScene != GameScenes.FLIGHT || MainSystem.NetworkState < ClientState.Connected)
                return;

            var vesselId = vesselLabel.vessel.id;
            var owner = LockSystem.LockQuery.GetControlLockOwner(vesselId);
            if (string.IsNullOrEmpty(owner))
                return;

            var key = $"{vesselId:N}:FLIGHT_LABEL_VISIBLE_STATE";
            if (!ShouldLog(key, 2.5f))
                return;

            var textComponent = vesselLabel.text;
            var labelGameObject = vesselLabel.gameObject;
            var textGameObject = textComponent != null ? textComponent.gameObject : null;
            var labelTransform = vesselLabel.transform;
            var textTransform = textComponent != null ? textComponent.transform : null;
            var rectTransform = textComponent != null ? textComponent.rectTransform : null;
            var text = textComponent != null ? textComponent.text ?? string.Empty : "<null_text_component>";
            var hasOwnerPrefix = textComponent != null && text.StartsWith(owner + "\n", StringComparison.Ordinal);

            var sb = new StringBuilder(1400);
            sb.Append("[KSPMP] ").Append(LogTag)
                .Append(" | code=FLIGHT_LABEL_VISIBLE_STATE")
                .Append(" | stage=").Append(context)
                .Append(" | vessel=").Append(vesselLabel.vessel.vesselName)
                .Append(" | id=").Append(vesselId.ToString("N"))
                .Append(" | controlLockOwner=").Append(owner)
                .Append(" | textHasOwnerPrefix=").Append(hasOwnerPrefix)
                .Append(" | textSnippet=").Append(EscapeOneLine(text, 260))
                .Append(" | labelEnabled=").Append(vesselLabel.enabled)
                .Append(" | labelIsActiveAndEnabled=").Append(vesselLabel.isActiveAndEnabled)
                .Append(" | labelActiveSelf=").Append(labelGameObject != null && labelGameObject.activeSelf)
                .Append(" | labelActiveInHierarchy=").Append(labelGameObject != null && labelGameObject.activeInHierarchy)
                .Append(" | labelLayer=").Append(labelGameObject != null ? labelGameObject.layer.ToString() : "<null>")
                .Append(" | labelPath=").Append(EscapeOneLine(BuildTransformPath(labelTransform), 180))
                .Append(" | labelLocalScale=").Append(labelTransform != null ? labelTransform.localScale.ToString("F3") : "<null>")
                .Append(" | labelPosition=").Append(labelTransform != null ? labelTransform.position.ToString("F1") : "<null>")
                .Append(" | textEnabled=").Append(textComponent != null && textComponent.enabled)
                .Append(" | textIsActiveAndEnabled=").Append(textComponent != null && textComponent.isActiveAndEnabled)
                .Append(" | textActiveSelf=").Append(textGameObject != null && textGameObject.activeSelf)
                .Append(" | textActiveInHierarchy=").Append(textGameObject != null && textGameObject.activeInHierarchy)
                .Append(" | textColor=").Append(textComponent != null ? textComponent.color.ToString() : "<null>")
                .Append(" | textAlpha=").Append(textComponent != null ? textComponent.color.a.ToString("F3") : "<null>")
                .Append(" | textFontSize=").Append(textComponent != null ? textComponent.fontSize.ToString("F2") : "<null>")
                .Append(" | textRect=").Append(rectTransform != null ? rectTransform.rect.ToString() : "<null>")
                .Append(" | textAnchoredPosition=").Append(rectTransform != null ? rectTransform.anchoredPosition.ToString("F1") : "<null>")
                .Append(" | labelCanvasGroups=").Append(DescribeCanvasGroups(labelTransform))
                .Append(" | textCanvasGroups=").Append(DescribeCanvasGroups(textTransform))
                .Append(" | hint=If textHasOwnerPrefix=true but the player cannot see the label, inspect active/enabled flags, alpha, parent CanvasGroups, scale/position, and whether the player is looking at the flight HUD label.");

            LunaLog.LogWarning(sb.ToString());
        }

        public static void OnApplyStillWrong(VesselLabel vesselLabel)
        {

            if (!IsEnabled || vesselLabel?.vessel == null || vesselLabel.text == null)

                return;



            var vesselId = vesselLabel.vessel.id;

            var owner = LockSystem.LockQuery.GetControlLockOwner(vesselId);

            var text = vesselLabel.text.text ?? string.Empty;



            if (string.IsNullOrEmpty(owner))

                return;



            if (text.StartsWith(owner + "\n", StringComparison.Ordinal))

                return;



            var key = $"{vesselId:N}:APPLY_FAILED";

            if (!ShouldLog(key, 5f))

                return;



            LunaLog.LogError(BuildMessage(

                "APPLY_CONTROL_OWNER_FAILED",

                "ApplyControlOwnerToFlightLabel returned without matching prefix",

                vesselLabel.vessel.vesselName,

                vesselId,

                owner,

                text));

        }



        private static bool ShouldLog(string key, float intervalSeconds)

        {

            var now = Time.realtimeSinceStartup;

            if (LastLogTime.TryGetValue(key, out var last) && now - last < intervalSeconds)

                return false;



            LastLogTime[key] = now;

            return true;

        }



        private static string BuildMessage(

            string code,

            string stage,

            string vesselName,

            Guid vesselId,

            string controlOwner,

            string actualText)

        {

            var sb = new StringBuilder(512);

            sb.Append("[KSPMP] ").Append(LogTag).Append(" | code=").Append(code)

                .Append(" | stage=").Append(stage)

                .Append(" | vessel=").Append(vesselName)

                .Append(" | id=").Append(vesselId.ToString("N"))

                .Append(" | controlLockOwner=").Append(controlOwner)

                .Append(" | textSnippet=").Append(EscapeOneLine(actualText, 220));



            sb.Append(" | hint=");

            switch (code)

            {

                case "FLIGHT_LABEL_MISMATCH":

                    sb.Append("Mismatch right after KSPMP ProcessLabel postfix; another mod may rewrite label.text later in the same frame (see STRIPPED_AFTER_LMP if that fires) or stock skipped owner line.");

                    break;

                case "FLIGHT_LABEL_OWNER_STRIPPED_AFTER_LMP_HARMONY":

                    sb.Append("Owner line was present after KSPMP postfix this frame but missing at very-late Harmony postfix — strong evidence another mod runs after KSPMP on VesselLabels.ProcessLabel.");

                    break;

                case "OWNER_PREFIX_PRESENT_BEFORE_LMP_EVENT":

                    sb.Append("Label already had owner prefix before KSPMP postfix ran; unexpected ordering or double pipeline.");

                    break;

                case "FLIGHT_LABEL_DUPLICATE_OWNER_PREFIX":

                    sb.Append("Duplicate owner+newline prefix on flight label.");

                    break;

                case "MAP_ORBIT_CAPTION_MISMATCH":

                    sb.Append("Map orbit caption out of sync with lock store; check map UI mods / caption patches.");

                    break;

                case "TRACKING_STATION_WIDGET_MISMATCH":

                    sb.Append("Tracking station name widget out of sync with lock store.");

                    break;

                case "STALE_FLIGHT_LABEL_BEFORE_LOCK_REFRESH":

                    sb.Append("Lock store already had owner but flight label text did not before KSPMP re-apply (late lock / missed refresh).");

                    break;

                case "APPLY_CONTROL_OWNER_FAILED":

                    sb.Append("ApplyControlOwnerToFlightLabel could not leave expected prefix (null text component?).");

                    break;

                default:

                    sb.Append("See LabelEvents / Harmony VesselLabels patches.");

                    break;

            }



            return sb.ToString();

        }



        private static string BuildTransformPath(Transform transform)
        {
            if (transform == null)
                return "<null>";

            var names = new List<string>();
            var current = transform;
            while (current != null && names.Count < 12)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static string DescribeCanvasGroups(Transform transform)
        {
            if (transform == null)
                return "<null_transform>";

            var groups = transform.GetComponentsInParent<Component>(true);
            if (groups == null || groups.Length == 0)
                return "none";

            var alphaProduct = 1f;
            var sb = new StringBuilder(256);
            for (var i = 0; i < groups.Length; i++)
            {
                var group = groups[i];
                if (group == null || group.GetType().FullName != "UnityEngine.CanvasGroup")
                    continue;

                var alpha = ReadFloatProperty(group, "alpha", 1f);
                alphaProduct *= alpha;
                if (sb.Length > 0)
                    sb.Append(";");

                sb.Append(group.name)
                    .Append("(alpha=").Append(alpha.ToString("F3"))
                    .Append(",interactable=").Append(ReadBoolProperty(group, "interactable"))
                    .Append(",blocksRaycasts=").Append(ReadBoolProperty(group, "blocksRaycasts"))
                    .Append(",ignoreParentGroups=").Append(ReadBoolProperty(group, "ignoreParentGroups"))
                    .Append(")");
            }

            if (sb.Length == 0)
                return "none";

            sb.Insert(0, "effectiveAlpha=" + alphaProduct.ToString("F3") + ",groups=");
            return sb.ToString();
        }

        private static float ReadFloatProperty(Component component, string propertyName, float fallback)
        {
            var property = component.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null)
                return fallback;

            var value = property.GetValue(component, null);
            return value is float f ? f : fallback;
        }

        private static string ReadBoolProperty(Component component, string propertyName)
        {
            var property = component.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null)
                return "<missing>";

            var value = property.GetValue(component, null);
            return value is bool b ? b.ToString() : "<unknown>";
        }

        private static string EscapeOneLine(string s, int maxLen)
        {

            if (string.IsNullOrEmpty(s))

                return "<empty>";



            var one = s.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

            if (one.Length <= maxLen)

                return one;



            return one.Substring(0, maxLen) + "…";

        }

    }

}

