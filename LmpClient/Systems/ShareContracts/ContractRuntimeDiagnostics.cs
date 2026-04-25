using Contracts;

using FinePrint.Contracts.Parameters;

using FinePrint.Utilities;

using LmpClient.Systems.SettingsSys;

using System;

using System.Collections.Generic;

using System.Linq;

using System.Reflection;

using System.Text;

using UnityEngine;



namespace LmpClient.Systems.ShareContracts

{

    /// <summary>

    /// Opt-in diagnostics (Status window <b>D7</b> / <see cref="SettingStructure.Debug7"/>) to see which stock

    /// <c>PARAM</c> rows are still open when a mission refuses to complete.

    /// </summary>

    internal static class ContractRuntimeDiagnostics

    {

        private static readonly Dictionary<Guid, float> LastLogRealtimeByGuid = new Dictionary<Guid, float>();



        private static readonly FieldInfo VspSuccessCounterField =

            typeof(VesselSystemsParameter).GetField("successCounter", BindingFlags.Instance | BindingFlags.NonPublic);



        private static readonly FieldInfo VspValidVesselField =

            typeof(VesselSystemsParameter).GetField("validVessel", BindingFlags.Instance | BindingFlags.NonPublic);



        private static readonly FieldInfo VspDirtyVesselField =

            typeof(VesselSystemsParameter).GetField("dirtyVessel", BindingFlags.Instance | BindingFlags.NonPublic);



        /// <summary>True when the player enabled contract diagnostics in the LMP status bar (D7).</summary>

        public static bool IsEnabled => SettingsSystem.CurrentSettings.Debug7;



        /// <summary>

        /// Logs <see cref="Contract.Save"/> <c>PARAM</c> <c>name</c> + <c>state</c> lines plus stock completion hints.

        /// Throttled for noisy reasons; never throttles completion/failure paths.

        /// </summary>

        public static void MaybeLogParameterTree(Contract contract, string reason)

        {

            if (!IsEnabled || contract == null)

            {

                return;

            }



            var urgent = reason.IndexOf("onCompleted", StringComparison.OrdinalIgnoreCase) >= 0

                         || reason.IndexOf("onFailed", StringComparison.OrdinalIgnoreCase) >= 0

                         || reason.IndexOf("preSend:ContractCompletedObserved", StringComparison.OrdinalIgnoreCase) >= 0

                         || reason.IndexOf("preSend:ContractFailedObserved", StringComparison.OrdinalIgnoreCase) >= 0

                         || reason.IndexOf("postFilter", StringComparison.OrdinalIgnoreCase) >= 0;



            if (!urgent && !ShouldLogNow(contract.ContractGuid, minSeconds: 0.5f))

            {

                return;

            }



            var sb = new StringBuilder(512);

            sb.Append("[KSPMP][ContractDiag] ");

            sb.Append(reason);

            sb.Append(" guid=").Append(contract.ContractGuid.ToString("N"));

            sb.Append(" contractState=").Append(contract.ContractState);

            sb.Append(" type=").Append(contract.GetType().Name);

            sb.Append(" title=\"").Append((contract.Title ?? string.Empty).Replace("\r", " ").Replace("\n", " ")).Append('"');



            AppendStockCompletionHints(contract, sb);

            sb.AppendLine();



            ConfigNode root;

            try

            {

                root = new ConfigNode();

                contract.Save(root);

            }

            catch (Exception ex)

            {

                sb.AppendLine();

                sb.Append("  Contract.Save failed: ").Append(ex.Message);

                LunaLog.Log(sb.ToString());

                return;

            }



            AppendParamTreeFromSavedRoot(root, sb);

            AppendVesselSystemsParameterFieldDump(root, sb);

            AppendVesselSystemsParameterRuntimeEvaluation(contract, root, sb);



            LunaLog.Log(sb.ToString());

        }



        private static bool ShouldLogNow(Guid guid, float minSeconds)

        {

            var now = Time.realtimeSinceStartup;

            if (LastLogRealtimeByGuid.TryGetValue(guid, out var last) && now - last < minSeconds)

            {

                return false;

            }



            LastLogRealtimeByGuid[guid] = now;

            return true;

        }



        private static void AppendStockCompletionHints(Contract contract, StringBuilder sb)

        {

            try

            {

                sb.Append(" stockIsFinished=").Append(contract.IsFinished());

            }

            catch

            {

                sb.Append(" stockIsFinished=?");

            }

        }



        private static void AppendParamTreeFromSavedRoot(ConfigNode root, StringBuilder sb)

        {

            if (root == null)

            {

                sb.AppendLine();

                sb.Append("  PARAM rows: skipped (null save root).");

                return;

            }



            sb.Append("  PARAM rows (depth-first, name + state):");

            var count = 0;

            AppendParamNodesRecursive(root, sb, depth: 0, ref count);

            if (count == 0)

            {

                sb.AppendLine();

                sb.Append("  (no PARAM nodes found under saved root)");

            }

        }



        private static void AppendParamNodesRecursive(ConfigNode node, StringBuilder sb, int depth, ref int count)

        {

            if (node == null)

            {

                return;

            }



            if (string.Equals(node.name, "PARAM", StringComparison.OrdinalIgnoreCase))

            {

                count++;

                sb.AppendLine();

                sb.Append(' ', Math.Min(depth * 2, 40));

                var typeName = node.GetValue("name") ?? string.Empty;

                var state = node.GetValue("state") ?? string.Empty;

                sb.Append("- ").Append(typeName).Append(" state=").Append(state);

            }



            foreach (ConfigNode child in node.GetNodes())

            {

                AppendParamNodesRecursive(child, sb, depth + 1, ref count);

            }

        }



        private const string VesselSystemsParameterTypeName = "VesselSystemsParameter";



        /// <summary>

        /// Dumps every serialized value (and shallow nested nodes) under the stock

        /// <see cref="VesselSystemsParameter"/> <c>PARAM</c> block so players can see launch IDs, module checks, etc.

        /// </summary>

        private static void AppendVesselSystemsParameterFieldDump(ConfigNode root, StringBuilder sb)

        {

            if (root == null)

            {

                return;

            }



            var vs = FindParamNodeByTypeName(root, VesselSystemsParameterTypeName);

            sb.AppendLine();

            if (vs == null)

            {

                sb.Append("  VesselSystemsParameter: no PARAM block with name=").Append(VesselSystemsParameterTypeName)

                    .Append(" in Contract.Save output.");

                return;

            }



            sb.Append("  VesselSystemsParameter full dump (all values on this PARAM node):");

            sb.AppendLine();

            sb.Append("    (Stock: comma-separated \"values\" on any PARAM are base ContractParameter reward scalars — ");

            sb.Append("funds completion, funds failure, science completion, reputation completion, reputation failure; ");

            sb.Append("not per-module progress. VesselSystemsParameter uses requireNew, launchID, checkModuleTypes, ");

            sb.Append("and only evaluates FlightGlobals.ActiveVessel while the contract is Active.)");

            AppendConfigNodeScalarValuesIndented(vs, sb, indentSpaces: 4);



            var children = vs.GetNodes()?.ToList() ?? new List<ConfigNode>();

            if (children.Count == 0)

            {

                return;

            }



            sb.AppendLine();

            sb.Append("  VesselSystemsParameter child nodes (name + values):");

            foreach (ConfigNode child in children)

            {

                if (child == null)

                {

                    continue;

                }



                sb.AppendLine();

                sb.Append("    [").Append(child.name ?? string.Empty).Append(']');

                AppendConfigNodeScalarValuesIndented(child, sb, indentSpaces: 6);

            }

        }



        /// <summary>

        /// Mirrors stock <see cref="VesselSystemsParameter"/> preconditions and <c>isVesselValid</c> so logs show which

        /// gate fails (active vessel, controllable, crew rule, launch IDs, contract objectives).

        /// </summary>

        private static void AppendVesselSystemsParameterRuntimeEvaluation(Contract contract, ConfigNode root, StringBuilder sb)

        {

            sb.AppendLine();

            sb.Append("  VesselSystemsParameter runtime gates (mirrors stock OnUpdate / isVesselValid):");



            try

            {

                var vsNode = FindParamNodeByTypeName(root, VesselSystemsParameterTypeName);

                if (vsNode == null)

                {

                    sb.AppendLine();

                    sb.Append("    (no saved PARAM node; skipped.)");

                    return;

                }



                var requireNew = ParseConfigBool(vsNode, "requireNew", defaultValue: false);

                var launchId = ParseConfigUInt(vsNode, "launchID", 0u);

                var mannedOrdinal = ParseConfigInt(vsNode, "mannedStatus", int.MinValue);

                var manned = mannedOrdinal == int.MinValue ? MannedStatus.ANY : (MannedStatus)mannedOrdinal;

                var checkTypes = ParsePipeList(vsNode.GetValue("checkModuleTypes"));



                // Qualify FinePrint type: LmpClient also defines namespace LmpClient.VesselUtilities.

                var flightReady = FinePrint.Utilities.SystemUtilities.FlightIsReady(

                    contract.ContractState,

                    Contract.State.Active,

                    checkVessel: true);

                sb.AppendLine();

                sb.Append("    FlightIsReady(ContractState, Active, checkVessel:true)=")

                    .Append(flightReady)

                    .Append(" sceneIsFlight=").Append(HighLogic.LoadedSceneIsFlight)

                    .Append(" flightGlobalsReady=").Append(FlightGlobals.ready)

                    .Append(" contractState=").Append(contract.ContractState);



                var av = FlightGlobals.ActiveVessel;

                sb.AppendLine();

                if (av == null)

                {

                    sb.Append("    ActiveVessel=null (stock never evaluates vessel checks).");

                }

                else

                {

                    var partCount = av.loaded

                        ? av.Parts.Count

                        : (av.protoVessel?.protoPartSnapshots?.Count ?? 0);

                    sb.Append("    ActiveVessel name=\"").Append(av.vesselName ?? string.Empty)

                        .Append("\" id=").Append(av.id)

                        .Append(" loaded=").Append(av.loaded)

                        .Append(" parts=").Append(partCount)

                        .Append(" IsControllable=").Append(av.IsControllable);

                }



                sb.AppendLine();

                sb.Append("    Saved PARAM: requireNew=").Append(requireNew)

                    .Append(" launchID=").Append(launchId)

                    .Append(" mannedStatus=").Append(mannedOrdinal == int.MinValue ? "?" : manned.ToString())

                    .Append(" checkModuleTypes=")

                    .Append(checkTypes.Count == 0 ? "(none)" : string.Join("|", checkTypes.ToArray()));



                if (av == null || !flightReady)

                {

                    sb.AppendLine();

                    sb.Append("    Recomputed isVesselValid-like=(n/a: FlightIsReady false or no active vessel).");

                }

                else

                {

                    var crew = av.loaded

                        ? av.GetCrewCount()

                        : (av.protoVessel != null ? av.protoVessel.GetVesselCrew().Count : 0);

                    var crewOk = EvaluateMannedGate(manned, crew, out var crewDetail);

                    sb.AppendLine();

                    sb.Append("    Crew gate: ").Append(crewDetail);



                    var controllable = av.IsControllable;

                    sb.AppendLine();

                    sb.Append("    IsControllable: ").Append(controllable);



                    var launchOk = true;

                    var launchDetail = "requireNew=false (launch age not checked).";

                    if (requireNew)

                    {

                        launchOk = FinePrint.Utilities.VesselUtilities.VesselLaunchedAfterID(launchId, av, "PotatoRoid");

                        launchDetail = "VesselLaunchedAfterID(launchID=" + launchId + ", ignore=PotatoRoid)=" + launchOk;

                    }



                    sb.AppendLine();

                    sb.Append("    Launch gate: ").Append(launchDetail);

                    if (requireNew && !launchOk && av.loaded)

                    {

                        AppendLaunchIdViolations(sb, av, launchId);

                    }



                    var objectivesOk = true;

                    var objectivesDetail = "checkModuleTypes empty (not required).";

                    if (checkTypes.Count > 0)

                    {

                        objectivesOk = av.HasValidContractObjectives(checkTypes);

                        objectivesDetail = "HasValidContractObjectives([" + string.Join(", ", checkTypes.ToArray()) + "])=" + objectivesOk;

                    }



                    sb.AppendLine();

                    sb.Append("    Objectives gate: ").Append(objectivesDetail);



                    var recomputed = controllable && crewOk && launchOk && objectivesOk;

                    sb.AppendLine();

                    sb.Append("    Recomputed isVesselValid-like=").Append(recomputed)

                        .Append(" (AND of controllable, crew gate, launch gate, objectives).");



                    sb.AppendLine();

                    sb.Append("    Stock debounce: parameter must see validVessel true for 5 consecutive Contract updates " +

                              "(successCounter); not persisted in save — see live instance line below.");

                }



                AppendLiveVspInstanceLine(contract, sb);

            }

            catch (Exception ex)

            {

                sb.AppendLine();

                sb.Append("    runtime evaluation error: ").Append(ex.Message);

            }

        }



        private static void AppendLaunchIdViolations(StringBuilder sb, Vessel av, uint contractLaunchId)

        {

            const string ignoreRaw = "PotatoRoid";

            var ignoreResolved = ignoreRaw.Replace('_', '.');

            var bad = new List<string>(12);

            foreach (var p in av.Parts)

            {

                if (p == null)

                {

                    continue;

                }



                var pn = p.partInfo != null ? p.partInfo.name : string.Empty;

                if (string.Equals(pn, ignoreResolved, StringComparison.Ordinal))

                {

                    continue;

                }



                if (p.launchID < contractLaunchId)

                {

                    bad.Add(pn + "[flightId=" + p.flightID + ",launchID=" + p.launchID + "]");

                    if (bad.Count >= 10)

                    {

                        break;

                    }

                }

            }



            if (bad.Count == 0)

            {

                return;

            }



            sb.AppendLine();

            sb.Append("    Parts with part.launchID < contract launchID (sample up to 10): ");

            sb.Append(string.Join("; ", bad.ToArray()));

        }



        private static void AppendLiveVspInstanceLine(Contract contract, StringBuilder sb)

        {

            VesselSystemsParameter first = null;

            var total = 0;

            foreach (var p in contract.AllParameters)

            {

                if (p is VesselSystemsParameter vsp)

                {

                    total++;

                    if (first == null)

                    {

                        first = vsp;

                    }

                }

            }



            sb.AppendLine();

            if (first == null)

            {

                sb.Append("    Live Contract tree: no VesselSystemsParameter instance (desync / not materialized?).");

                return;

            }



            if (total > 1)

            {

                sb.Append("    Live Contract tree: ").Append(total)

                    .Append(" VesselSystemsParameter instances; showing first only.");

                sb.AppendLine();

            }



            sb.Append("    Live VesselSystemsParameter: State=").Append(first.State)

                .Append(" requireNew=").Append(first.requireNew)

                .Append(" launchID=").Append(first.launchID);



            TryReadVspPrivateFields(first, out var successCounter, out var validVessel, out var dirtyVessel);

            sb.Append(" successCounter=").Append(successCounter?.ToString() ?? "?");

            sb.Append(" validVessel=").Append(validVessel?.ToString() ?? "?");

            sb.Append(" dirtyVessel=").Append(dirtyVessel?.ToString() ?? "?");

        }



        private static void TryReadVspPrivateFields(

            VesselSystemsParameter vsp,

            out int? successCounter,

            out bool? validVessel,

            out bool? dirtyVessel)

        {

            successCounter = null;

            validVessel = null;

            dirtyVessel = null;

            try

            {

                if (VspSuccessCounterField != null)

                {

                    successCounter = (int)VspSuccessCounterField.GetValue(vsp);

                }



                if (VspValidVesselField != null)

                {

                    validVessel = (bool)VspValidVesselField.GetValue(vsp);

                }



                if (VspDirtyVesselField != null)

                {

                    dirtyVessel = (bool)VspDirtyVesselField.GetValue(vsp);

                }

            }

            catch

            {

                // leave nulls

            }

        }



        private static bool EvaluateMannedGate(MannedStatus manned, int crewCount, out string detail)

        {

            switch (manned)

            {

                case MannedStatus.UNMANNED:

                    detail = "mannedStatus=UNMANNED requires crewCount==0; crewCount=" + crewCount;

                    return crewCount == 0;

                case MannedStatus.MANNED:

                    detail = "mannedStatus=MANNED requires crewCount>=1; crewCount=" + crewCount;

                    return crewCount >= 1;

                default:

                    detail = "mannedStatus=ANY; crewCount=" + crewCount;

                    return true;

            }

        }



        private static List<string> ParsePipeList(string raw)

        {

            var list = new List<string>();

            if (string.IsNullOrWhiteSpace(raw))

            {

                return list;

            }



            foreach (var piece in raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))

            {

                var t = piece.Trim();

                if (t.Length > 0)

                {

                    list.Add(t);

                }

            }



            return list;

        }



        private static bool ParseConfigBool(ConfigNode node, string key, bool defaultValue)

        {

            var s = node?.GetValue(key);

            if (string.IsNullOrWhiteSpace(s))

            {

                return defaultValue;

            }



            return bool.TryParse(s.Trim(), out var b) ? b : defaultValue;

        }



        private static uint ParseConfigUInt(ConfigNode node, string key, uint defaultValue)

        {

            var s = node?.GetValue(key);

            if (string.IsNullOrWhiteSpace(s))

            {

                return defaultValue;

            }



            return uint.TryParse(s.Trim(), out var u) ? u : defaultValue;

        }



        private static int ParseConfigInt(ConfigNode node, string key, int defaultValue)

        {

            var s = node?.GetValue(key);

            if (string.IsNullOrWhiteSpace(s))

            {

                return defaultValue;

            }



            return int.TryParse(s.Trim(), out var i) ? i : defaultValue;

        }



        private static ConfigNode FindParamNodeByTypeName(ConfigNode node, string paramTypeName)

        {

            if (node == null || string.IsNullOrEmpty(paramTypeName))

            {

                return null;

            }



            if (string.Equals(node.name, "PARAM", StringComparison.OrdinalIgnoreCase))

            {

                var n = node.GetValue("name");

                if (string.Equals(n?.Trim(), paramTypeName, StringComparison.Ordinal))

                {

                    return node;

                }

            }



            foreach (ConfigNode child in node.GetNodes())

            {

                var hit = FindParamNodeByTypeName(child, paramTypeName);

                if (hit != null)

                {

                    return hit;

                }

            }



            return null;

        }



        private static void AppendConfigNodeScalarValuesIndented(ConfigNode node, StringBuilder sb, int indentSpaces)

        {

            if (node?.values == null)

            {

                return;

            }



            var pad = new string(' ', indentSpaces);

            foreach (var key in node.values.DistinctNames())

            {

                var v = node.GetValue(key);

                sb.AppendLine();

                sb.Append(pad).Append(key).Append('=').Append(v ?? string.Empty);

            }

        }

    }

}


