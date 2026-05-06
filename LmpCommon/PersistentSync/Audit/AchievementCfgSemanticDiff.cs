using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LmpCommon.PersistentSync.Payloads.Achievements;

namespace LmpCommon.PersistentSync.Audit
{
    /// <summary>
    /// Defensive cfg-text analysis for achievement snapshot bytes (KSP ProgressNode-style UTF-8).
    /// Walks nested <c>{ }</c> blocks to find milestone keys (e.g. under <c>PROGRESS</c>).
    /// </summary>
    internal static class AchievementCfgSemanticDiff
    {
        /// <summary>
        /// Keys that encode ProgressNode-style milestone gates (case-insensitive).
        /// Stock may omit false flags from cfg; include Unity/Persistent name variants.
        /// <c>completed</c> is usually UT float — not a milestone gate here.
        /// </summary>
        private static readonly HashSet<string> WatchKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "reached",
            "IsReached",
            "complete",
            "IsComplete",
            "finished",
            "unlocked",
            "completedOnce",
            "state"
        };

        private const int MaxPayloadBytesForKeyDump = 512;
        private const int MaxParseLineVisits = 4096;
        private const int MaxBraceDepth = 64;
        private const int MaxDeepKeySampleKeys = 14;

        /// <summary>
        /// UT seconds tolerance for <c>completed</c> when comparing local KSP cfg text vs server Luna round-trip text.
        /// </summary>
        internal const double DefaultCompletedUtToleranceSeconds = 120d;

        private static readonly string[] ReachedAliasKeys =
        {
            "reached",
            "IsReached"
        };

        private static readonly string[] CompleteAliasKeys =
        {
            "complete",
            "IsComplete",
            "finished",
            "unlocked",
            "completedOnce"
        };

        /// <summary>
        /// True when the two achievement cfg blobs are not byte-identical but carry the same milestone and data keys
        /// for Domain Analyzer purposes (KSP vs Luna formatting, omitted false flags, small completed-UT skew).
        /// </summary>
        internal static bool IsSemanticallyEquivalentForAudit(byte[] local, byte[] server)
        {
            if (local == null || server == null)
            {
                return false;
            }

            string localText;
            string serverText;
            try
            {
                localText = local.Length == 0 ? string.Empty : Encoding.UTF8.GetString(local, 0, local.Length);
                serverText = server.Length == 0 ? string.Empty : Encoding.UTF8.GetString(server, 0, server.Length);
            }
            catch
            {
                return false;
            }

            localText = StripUtf8Bom(localText);
            serverText = StripUtf8Bom(serverText);
            var localKv = CollectDeepKeyValues(localText);
            var serverKv = CollectDeepKeyValues(serverText);
            return MilestoneGatesEqual(localKv, serverKv) &&
                   NonMilestoneDataEqual(localKv, serverKv, DefaultCompletedUtToleranceSeconds);
        }

        private static bool MilestoneGatesEqual(Dictionary<string, string> local, Dictionary<string, string> server)
        {
            if (GetEffectiveBoolTrueIfAny(local, ReachedAliasKeys) != GetEffectiveBoolTrueIfAny(server, ReachedAliasKeys))
            {
                return false;
            }

            if (GetEffectiveBoolTrueIfAny(local, CompleteAliasKeys) != GetEffectiveBoolTrueIfAny(server, CompleteAliasKeys))
            {
                return false;
            }

            local.TryGetValue("state", out var ls);
            server.TryGetValue("state", out var rs);
            var ln = ls == null ? null : NormalizeValue(ls);
            var rn = rs == null ? null : NormalizeValue(rs);
            if (string.IsNullOrEmpty(ln) && string.IsNullOrEmpty(rn))
            {
                return true;
            }

            if (string.IsNullOrEmpty(ln) || string.IsNullOrEmpty(rn))
            {
                return false;
            }

            return string.Equals(ln, rn, StringComparison.OrdinalIgnoreCase);
        }

        private static bool GetEffectiveBoolTrueIfAny(Dictionary<string, string> deep, string[] keys)
        {
            foreach (var k in keys)
            {
                if (deep.TryGetValue(k, out var v) && TryParseBoolLoose(v, out var b) && b)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseBoolLoose(string v, out bool b)
        {
            b = false;
            if (string.IsNullOrWhiteSpace(v))
            {
                return false;
            }

            var t = v.Trim();
            if (bool.TryParse(t, out b))
            {
                return true;
            }

            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            {
                b = i != 0;
                return true;
            }

            return false;
        }

        private static bool NonMilestoneDataEqual(
            Dictionary<string, string> local,
            Dictionary<string, string> server,
            double completedUtToleranceSeconds)
        {
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in ReachedAliasKeys)
            {
                skip.Add(k);
            }

            foreach (var k in CompleteAliasKeys)
            {
                skip.Add(k);
            }

            skip.Add("state");

            foreach (var k in local.Keys.Union(server.Keys, StringComparer.OrdinalIgnoreCase))
            {
                if (skip.Contains(k))
                {
                    continue;
                }

                if (k.Equals("completed", StringComparison.OrdinalIgnoreCase))
                {
                    if (!CompletedUtCompatible(local, server, completedUtToleranceSeconds))
                    {
                        return false;
                    }

                    continue;
                }

                local.TryGetValue(k, out var lv);
                server.TryGetValue(k, out var sv);
                if (!string.Equals(
                        NormalizeValue(lv ?? string.Empty),
                        NormalizeValue(sv ?? string.Empty),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CompletedUtCompatible(
            Dictionary<string, string> local,
            Dictionary<string, string> server,
            double toleranceSeconds)
        {
            var lh = local.TryGetValue("completed", out var l);
            var rh = server.TryGetValue("completed", out var r);
            var ld = 0d;
            var rd = 0d;
            var lp = lh && double.TryParse((l ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out ld);
            var rp = rh && double.TryParse((r ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out rd);
            if (!lp && !rp)
            {
                return true;
            }

            if (lp != rp)
            {
                return false;
            }

            return Math.Abs(ld - rd) <= toleranceSeconds;
        }

        internal static void AppendDiagnosticsForByteMismatch(
            PersistentSyncAuditComparisonResult r,
            string achievementId,
            byte[] local,
            byte[] server)
        {
            if (r == null || string.IsNullOrEmpty(achievementId))
            {
                return;
            }

            void Add(string line)
            {
                if (r.SemanticDiagnostics.Count >= PersistentSyncAuditSemanticLimits.MaxSemanticDiagnosticLines)
                {
                    return;
                }

                r.SemanticDiagnostics.Add(line);
            }

            Add($"semantic id={achievementId} rawCfgTextDiffers=yes");

            string localText;
            string serverText;
            try
            {
                localText = local == null || local.Length == 0 ? string.Empty : Encoding.UTF8.GetString(local, 0, local.Length);
                serverText = server == null || server.Length == 0 ? string.Empty : Encoding.UTF8.GetString(server, 0, server.Length);
            }
            catch (Exception ex)
            {
                Add($"semantic id={achievementId} utf8DecodeError={ex.GetType().Name}");
                return;
            }

            localText = StripUtf8Bom(localText);
            serverText = StripUtf8Bom(serverText);

            var localKv = CollectDeepKeyValues(localText);
            var serverKv = CollectDeepKeyValues(serverText);

            var localWatch = FilterWatch(localKv);
            var serverWatch = FilterWatch(serverKv);

            if (localWatch.Count == 0 && serverWatch.Count == 0)
            {
                Add($"semantic id={achievementId} watchKeys=noMilestoneKeysFoundInDeepParse");
                Add($"semantic id={achievementId} deepKeysLocal={FormatKeySample(localKv)}");
                Add($"semantic id={achievementId} deepKeysServer={FormatKeySample(serverKv)}");
                if (IsOnlyCompletedUtcKey(localKv) && IsOnlyCompletedUtcKey(serverKv) &&
                    TryGetCompletedUtcValue(localKv, out var localUt) &&
                    TryGetCompletedUtcValue(serverKv, out var serverUt))
                {
                    var diff = Math.Abs(localUt - serverUt);
                    Add($"semantic id={achievementId} completedUt local={FormatUt(localUt)} server={FormatUt(serverUt)} absDiff={diff.ToString("R", CultureInfo.InvariantCulture)}");
                    if (diff <= 0.5d)
                    {
                        Add($"semantic id={achievementId} note=completedUtSkewNegligibleLikelyFloatFormatting");
                    }
                    else if (diff <= 120d)
                    {
                        Add($"semantic id={achievementId} note=completedUtSkewSmallOftenSnapshotApplyOrder");
                    }
                    else
                    {
                        Add($"semantic id={achievementId} note=completedUtDiffersMilestoneGatesStillAbsentFromCfgText");
                    }
                }

                Add($"semantic id={achievementId} note=stock often omits false milestone fields from cfg; small byte deltas may still be benign");
            }
            else if (DictionariesEqual(localWatch, serverWatch))
            {
                Add($"semantic id={achievementId} watchKeysMatch=yes (equivalent under {string.Join(",", WatchKeys.OrderBy(x => x, StringComparer.Ordinal))})");
            }
            else
            {
                Add($"semantic id={achievementId} watchKeysMatch=no");
                foreach (var key in localWatch.Keys.Union(serverWatch.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal))
                {
                    localWatch.TryGetValue(key, out var lv);
                    serverWatch.TryGetValue(key, out var sv);
                    if (!string.Equals(NormalizeValue(lv), NormalizeValue(sv), StringComparison.OrdinalIgnoreCase))
                    {
                        Add($"semantic id={achievementId} delta {key} local={FormatVal(lv)} server={FormatVal(sv)}");
                    }
                }
            }

            if (local != null && server != null &&
                local.Length <= MaxPayloadBytesForKeyDump && server.Length <= MaxPayloadBytesForKeyDump)
            {
                var lk = string.Join(",", localKv.Keys.OrderBy(x => x, StringComparer.Ordinal));
                var sk = string.Join(",", serverKv.Keys.OrderBy(x => x, StringComparer.Ordinal));
                if (!string.Equals(lk, sk, StringComparison.Ordinal))
                {
                    Add($"semantic id={achievementId} deepKeySetDiffers localKeys=[{lk}] serverKeys=[{sk}]");
                }
            }
        }

        /// <summary>Resolves row id like runtime merge: root cfg node name, then inner <c>name =</c>, then wire id.</summary>
        internal static string ResolveAchievementRowKey(AchievementSnapshotInfo item)
        {
            if (item?.Data == null || item.Data.Length == 0)
            {
                return null;
            }

            string text;
            try
            {
                text = Encoding.UTF8.GetString(item.Data, 0, item.Data.Length);
            }
            catch
            {
                return string.IsNullOrEmpty(item.Id) ? null : item.Id;
            }

            text = StripUtf8Bom(text);
            if (TryGetOuterCfgNodeHeaderName(text, out var header) && !string.IsNullOrEmpty(header))
            {
                return header;
            }

            var deep = CollectDeepKeyValues(text);
            if (deep.TryGetValue("name", out var n) && !string.IsNullOrWhiteSpace(n))
            {
                return n.Trim();
            }

            return string.IsNullOrEmpty(item.Id) ? null : item.Id;
        }

        internal static bool TryGetCfgName(string cfgText, out string name)
        {
            name = null;
            if (string.IsNullOrEmpty(cfgText))
            {
                return false;
            }

            var deep = CollectDeepKeyValues(cfgText);
            if (deep.TryGetValue("name", out var n) && !string.IsNullOrWhiteSpace(n))
            {
                name = n.Trim();
                return true;
            }

            return false;
        }

        /// <summary>First line is a node header when followed by a lone <c>{</c> line (KSP root achievement node name).</summary>
        internal static bool TryGetOuterCfgNodeHeaderName(string cfgText, out string header)
        {
            header = null;
            var lines = NormalizeLines(cfgText);
            var i = 0;
            SkipEmptyAndComments(lines, ref i);
            if (i + 1 >= lines.Length)
            {
                return false;
            }

            var a = lines[i].Trim();
            if (a.Length == 0 || a.StartsWith("//", StringComparison.Ordinal) || a.Contains("=") || a == "{")
            {
                return false;
            }

            if (lines[i + 1].Trim() != "{")
            {
                return false;
            }

            header = a;
            return true;
        }

        /// <summary>All <c>key = value</c> pairs at any brace depth; later lines override earlier duplicate keys.</summary>
        internal static Dictionary<string, string> CollectDeepKeyValues(string cfgText)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(cfgText))
            {
                return map;
            }

            var lines = NormalizeLines(cfgText);
            var i = 0;
            var visited = 0;
            SkipEmptyAndComments(lines, ref i);
            if (i >= lines.Length)
            {
                return map;
            }

            if (IsHeaderBeforeOpenBrace(lines, i))
            {
                i += 2;
                ParseBlock(lines, ref i, 0, map, ref visited);
                return map;
            }

            if (lines[i].Trim() == "{")
            {
                i++;
                ParseBlock(lines, ref i, 0, map, ref visited);
                return map;
            }

            ParseBlock(lines, ref i, 0, map, ref visited);
            return map;
        }

        private static string StripUtf8Bom(string s)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '\uFEFF')
            {
                return s;
            }

            return s.Substring(1);
        }

        private static string FormatKeySample(Dictionary<string, string> map)
        {
            if (map == null || map.Count == 0)
            {
                return "[]";
            }

            var ordered = map.Keys.OrderBy(x => x, StringComparer.Ordinal).Take(MaxDeepKeySampleKeys).ToList();
            var inner = string.Join(",", ordered);
            if (map.Count > MaxDeepKeySampleKeys)
            {
                inner += ",...";
            }

            return "[" + inner + "]";
        }

        private static bool IsOnlyCompletedUtcKey(Dictionary<string, string> map)
        {
            if (map == null || map.Count != 1)
            {
                return false;
            }

            foreach (var k in map.Keys)
            {
                return k.Equals("completed", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static bool TryGetCompletedUtcValue(Dictionary<string, string> map, out double ut)
        {
            ut = 0d;
            if (map == null || !map.TryGetValue("completed", out var s) || string.IsNullOrWhiteSpace(s))
            {
                return false;
            }

            return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out ut);
        }

        private static string FormatUt(double ut) => ut.ToString("R", CultureInfo.InvariantCulture);

        private static string[] NormalizeLines(string cfgText)
        {
            return cfgText.Replace("\r\n", "\n").Replace('\r', '\n').Split(new[] { '\n' }, StringSplitOptions.None);
        }

        private static void SkipEmptyAndComments(string[] lines, ref int i)
        {
            while (i < lines.Length)
            {
                var t = lines[i].Trim();
                if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal))
                {
                    i++;
                    continue;
                }

                break;
            }
        }

        private static bool IsHeaderBeforeOpenBrace(string[] lines, int i)
        {
            var a = lines[i].Trim();
            if (a.Length == 0 || a.StartsWith("//", StringComparison.Ordinal) || a.Contains("=") || a == "{")
            {
                return false;
            }

            return i + 1 < lines.Length && lines[i + 1].Trim() == "{";
        }

        private static bool IsHeaderOpenCombined(string line, out string headerBeforeBrace)
        {
            headerBeforeBrace = null;
            var t = line.Trim();
            if (t.Length < 2 || !t.EndsWith("{", StringComparison.Ordinal))
            {
                return false;
            }

            var before = t.Substring(0, t.Length - 1).Trim();
            if (before.Length == 0 || before.Contains("="))
            {
                return false;
            }

            headerBeforeBrace = before;
            return true;
        }

        private static void ParseBlock(string[] lines, ref int i, int depth, Dictionary<string, string> map, ref int visited)
        {
            while (i < lines.Length)
            {
                if (visited++ > MaxParseLineVisits)
                {
                    return;
                }

                var t = lines[i].Trim();
                if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal))
                {
                    i++;
                    continue;
                }

                if (t == "}")
                {
                    i++;
                    return;
                }

                if (depth > MaxBraceDepth)
                {
                    i++;
                    continue;
                }

                var eq = t.IndexOf('=');
                if (eq > 0)
                {
                    var key = t.Substring(0, eq).Trim();
                    var val = t.Substring(eq + 1).Trim();
                    if (key.Length > 0)
                    {
                        map[key] = val;
                    }

                    i++;
                    continue;
                }

                if (IsHeaderOpenCombined(t, out _))
                {
                    i++;
                    ParseBlock(lines, ref i, depth + 1, map, ref visited);
                    continue;
                }

                if (i + 1 < lines.Length && lines[i + 1].Trim() == "{")
                {
                    i += 2;
                    ParseBlock(lines, ref i, depth + 1, map, ref visited);
                    continue;
                }

                if (t == "{")
                {
                    i++;
                    ParseBlock(lines, ref i, depth + 1, map, ref visited);
                    continue;
                }

                i++;
            }
        }

        private static string FormatVal(string v) => v == null ? "<null>" : (v.Length > 64 ? v.Substring(0, 61) + "..." : v);

        private static string NormalizeValue(string v)
        {
            if (string.IsNullOrWhiteSpace(v))
            {
                return string.Empty;
            }

            var t = v.Trim();
            if (bool.TryParse(t, out var b))
            {
                return b ? bool.TrueString : bool.FalseString;
            }

            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            {
                return i.ToString(CultureInfo.InvariantCulture);
            }

            if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
            {
                return f.ToString("R", CultureInfo.InvariantCulture);
            }

            return t;
        }

        private static Dictionary<string, string> FilterWatch(Dictionary<string, string> all)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in all)
            {
                if (WatchKeys.Contains(kv.Key))
                {
                    d[kv.Key] = kv.Value;
                }
            }

            return d;
        }

        private static bool DictionariesEqual(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            foreach (var kv in a)
            {
                if (!b.TryGetValue(kv.Key, out var bv))
                {
                    return false;
                }

                if (!string.Equals(NormalizeValue(kv.Value), NormalizeValue(bv), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Legacy shallow extract (flat lines only). Prefer <see cref="CollectDeepKeyValues"/>.</summary>
        internal static Dictionary<string, string> ParseShallowKeyValues(string cfgText)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(cfgText))
            {
                return map;
            }

            using (var reader = new System.IO.StringReader(cfgText))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (trimmed == "{" || trimmed == "}")
                    {
                        continue;
                    }

                    var eq = trimmed.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }

                    var key = trimmed.Substring(0, eq).Trim();
                    var val = trimmed.Substring(eq + 1).Trim();
                    if (key.Length == 0)
                    {
                        continue;
                    }

                    map[key] = val;
                }
            }

            return map;
        }
    }
}
