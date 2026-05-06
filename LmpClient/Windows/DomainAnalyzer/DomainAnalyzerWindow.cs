#if DEBUG
using Contracts;
using LmpClient.Base;
using LmpClient.Systems.Lock;
using LmpClient.Utilities;
using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.ShareAchievements;
using LmpClient.Systems.ShareContracts;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Audit;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace LmpClient.Windows.DomainAnalyzer
{
    /// <summary>
    /// DEBUG-only PersistentSync domain audit UI (compare-only; does not apply snapshots).
    /// </summary>
    public sealed class DomainAnalyzerWindow : Window<DomainAnalyzerWindow>
    {
        private const float WindowWidth = 900f;
        private const float WindowHeight = 540f;
        private const int WindowControlId = 6726;
        private const float DomainListWidth = 292f;

        private Vector2 _listScroll;
        private Vector2 _detailScroll;

        private readonly List<string> _orderedDomainIds = new List<string>();

        private int _selectedIndex;

        private bool _autoRefresh = true;
        private float _autoRefreshSeconds = 8f;
        private double _lastAutoRefreshTime;

        private static readonly GUILayoutOption[] LayoutOptions = new GUILayoutOption[4];

        private enum BundleCopyMode
        {
            All,
            SelectedSemantic,
            MinimalRepro,
            RawSelected
        }

        public override bool Display
        {
            get =>
                base.Display &&
                MainSystem.NetworkState >= ClientState.Running &&
                HighLogic.LoadedScene >= GameScenes.SPACECENTER;
            set => base.Display = value;
        }

        public override void OnDisplay()
        {
            base.OnDisplay();
            PersistentSyncAuditCoordinator.Instance.RefreshCompleted += OnRefreshCompleted;
            TryRefreshAll();
        }

        public override void OnHide()
        {
            PersistentSyncAuditCoordinator.Instance.RefreshCompleted -= OnRefreshCompleted;
            base.OnHide();
        }

        private void OnRefreshCompleted()
        {
            RebuildOrderedIds();
        }

        public override void Update()
        {
            base.Update();
            if (!Display || !_autoRefresh)
            {
                return;
            }

            if (Time.realtimeSinceStartup - _lastAutoRefreshTime < _autoRefreshSeconds)
            {
                return;
            }

            _lastAutoRefreshTime = Time.realtimeSinceStartup;
            if (!PersistentSyncAuditCoordinator.Instance.RefreshInProgress)
            {
                PersistentSyncAuditCoordinator.Instance.TryBeginRefreshAll(out _);
            }
        }

        protected override void DrawGui()
        {
            GUI.skin = DefaultSkin;
            WindowRect = FixWindowPos(GUILayout.Window(
                WindowControlId + MainSystem.WindowOffset,
                WindowRect,
                DrawContent,
                "Domain Analyzer (PersistentSync)",
                LayoutOptions));
        }

        public override void SetStyles()
        {
            WindowRect = new Rect(40f, 80f, WindowWidth, WindowHeight);
            MoveRect = new Rect(0, 0, int.MaxValue, TitleHeight);

            LayoutOptions[0] = GUILayout.MinWidth(WindowWidth);
            LayoutOptions[1] = GUILayout.MaxWidth(WindowWidth);
            LayoutOptions[2] = GUILayout.MinHeight(WindowHeight);
            LayoutOptions[3] = GUILayout.MaxHeight(WindowHeight);
        }

        protected override void DrawWindowContent(int windowId)
        {
            GUI.DragWindow(MoveRect);

            DrawToolbar();
            DrawSummaryBar();

            GUILayout.BeginHorizontal();

            _listScroll = GUILayout.BeginScrollView(
                _listScroll,
                false,
                true,
                GUILayout.Width(DomainListWidth),
                GUILayout.Height(WindowHeight - 92f));
            DrawDomainList();
            GUILayout.EndScrollView();

            _detailScroll = GUILayout.BeginScrollView(
                _detailScroll,
                false,
                true,
                GUILayout.Width(WindowWidth - DomainListWidth - 20f),
                GUILayout.Height(WindowHeight - 92f));
            DrawDetails();
            GUILayout.EndScrollView();

            GUILayout.EndHorizontal();

            DrawFooter();
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal();

            GUI.enabled = !PersistentSyncAuditCoordinator.Instance.RefreshInProgress;
            if (GUILayout.Button("Refresh all", GUILayout.Width(78f)))
            {
                TryRefreshAll();
            }

            if (GUILayout.Button("Refresh selected", GUILayout.Width(116f)) && _orderedDomainIds.Count > 0 && _selectedIndex >= 0 &&
                _selectedIndex < _orderedDomainIds.Count)
            {
                var id = _orderedDomainIds[_selectedIndex];
                PersistentSyncAuditCoordinator.Instance.TryBeginRefresh(new[] { id }, out _);
            }

            _autoRefresh = GUILayout.Toggle(_autoRefresh, "Auto", GUILayout.Width(48f));
            GUILayout.Label("sec", GUILayout.Width(24f));
            var secStr = GUILayout.TextField(_autoRefreshSeconds.ToString("0.#"), GUILayout.Width(42f));
            if (float.TryParse(secStr, out var sec) && sec >= 2f && sec <= 120f)
            {
                _autoRefreshSeconds = sec;
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Copy all", GUILayout.Width(70f)))
            {
                // Includes catalog + Contract Progression Runtime + all domain rows.
                CopyToClipboard(BuildTextBundle(BundleCopyMode.All));
            }

            if (GUILayout.Button("Copy selected", GUILayout.Width(96f)))
            {
                CopyToClipboard(BuildTextBundle(BundleCopyMode.SelectedSemantic));
            }

            if (GUILayout.Button("Copy repro", GUILayout.Width(76f)))
            {
                CopyToClipboard(BuildTextBundle(BundleCopyMode.MinimalRepro));
            }

            if (GUILayout.Button("Copy raw", GUILayout.Width(72f)))
            {
                CopyToClipboard(BuildTextBundle(BundleCopyMode.RawSelected));
            }

            GUI.enabled = true;

            GUILayout.EndHorizontal();
        }

        private void DrawSummaryBar()
        {
            var coord = PersistentSyncAuditCoordinator.Instance;
            var rows = PersistentSyncAuditCoordinator.Instance.LastRows.Values.ToList();
            var ok = rows.Count(r => r.Comparison != null && r.Comparison.Severity == PersistentSyncAuditSeverity.Ok);
            var warn = rows.Count(r => r.Comparison != null && r.Comparison.Severity == PersistentSyncAuditSeverity.Warning);
            var bad = rows.Count(r => r.Comparison != null && r.Comparison.Severity >= PersistentSyncAuditSeverity.Error);
            var pending = PersistentSyncDomainCatalog.AllOrdered.Count() - rows.Count(r => r.Comparison != null);
            GUILayout.Label(
                $"Audit: OK {ok}  Warn {warn}  Error {bad}  Pending {Math.Max(0, pending)}  " +
                $"correlation={coord.LastCorrelationId}  scene={HighLogic.LoadedScene}  net={MainSystem.NetworkState}",
                BoldGreenLabelStyle);
        }

        private static void TryRefreshAll()
        {
            PersistentSyncAuditCoordinator.Instance.TryBeginRefreshAll(out _);
            Singleton.RebuildOrderedIds();
        }

        private void RebuildOrderedIds()
        {
            _orderedDomainIds.Clear();
            foreach (var def in PersistentSyncDomainCatalog.AllOrdered)
            {
                _orderedDomainIds.Add(def.DomainId);
            }

            if (_selectedIndex >= _orderedDomainIds.Count)
            {
                _selectedIndex = Math.Max(0, _orderedDomainIds.Count - 1);
            }
        }

        private void DrawDomainList()
        {
            var rows = PersistentSyncAuditCoordinator.Instance.LastRows;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Status", GUILayout.Width(42f));
            GUILayout.Label("Domain", GUILayout.Width(126f));
            GUILayout.Label("Revision", GUILayout.Width(72f));
            GUILayout.EndHorizontal();

            for (var i = 0; i < _orderedDomainIds.Count; i++)
            {
                var id = _orderedDomainIds[i];
                rows.TryGetValue(id, out var row);

                var selected = _selectedIndex == i;
                var old = GUI.backgroundColor;
                if (selected)
                {
                    GUI.backgroundColor = new Color(0.55f, 0.75f, 1f);
                }

                var status = FormatDomainStatus(row);
                var cRev = row != null ? row.ClientKnownRevision.ToString() : "—";
                var sRev = row != null ? row.ServerRevision.ToString() : "—";
                var text = string.Format("{0,-4} {1,-20} {2}/{3}", status, Truncate(id, 20), cRev, sRev);
                if (GUILayout.Button(text, GUILayout.Width(DomainListWidth - 22f), GUILayout.Height(26f)))
                {
                    _selectedIndex = i;
                }

                GUI.backgroundColor = old;
            }
        }

        private static string FormatDomainStatus(PersistentSyncDomainAuditRow row)
        {
            if (row == null)
            {
                return "…";
            }

            if (!string.IsNullOrEmpty(row.ServerError))
            {
                return "ERR";
            }

            if (!string.IsNullOrEmpty(row.LocalUnavailableReason))
            {
                return "N/A";
            }

            if (row.Comparison == null)
            {
                return "…";
            }

            return SeverityChip(row.Comparison.Severity);
        }

        private static string SeverityChip(PersistentSyncAuditSeverity severity)
        {
            switch (severity)
            {
                case PersistentSyncAuditSeverity.Ok:
                    return "OK";
                case PersistentSyncAuditSeverity.Warning:
                    return "WARN";
                default:
                    return "ERR";
            }
        }

        private void DrawDetails()
        {
            if (_orderedDomainIds.Count == 0)
            {
                GUILayout.Label("No catalog domains.");
                return;
            }

            if (_selectedIndex < 0 || _selectedIndex >= _orderedDomainIds.Count)
            {
                return;
            }

            var id = _orderedDomainIds[_selectedIndex];
            PersistentSyncAuditCoordinator.Instance.LastRows.TryGetValue(id, out var row);
            if (row == null)
            {
                GUILayout.Label($"No data for {id} yet — use Refresh.");
                return;
            }

            DrawDomainHeader(row);
            if (!string.IsNullOrEmpty(row.ServerError))
            {
                GUILayout.Label($"Server error: {row.ServerError}", BoldRedLabelStyle);
            }

            if (!string.IsNullOrEmpty(row.LocalUnavailableReason))
            {
                GUILayout.Label($"Local: {row.LocalUnavailableReason}", BoldRedLabelStyle);
            }

            GUILayout.Label($"Payload bytes: local={row.LocalPayloadBytes} server={row.ServerPayloadBytes}");
            GUILayout.Label($"SHA256-8: local={row.LocalHash8} server={row.ServerHash8}");
            if (!string.IsNullOrEmpty(row.LocalPreviewHex))
            {
                GUILayout.Label("Local payload (hex prefix):", BoldGreenLabelStyle);
                GUILayout.TextArea(row.LocalPreviewHex, GUILayout.Height(46f));
            }

            if (!string.IsNullOrEmpty(row.ServerPreviewHex))
            {
                GUILayout.Label("Server payload (hex prefix):", BoldGreenLabelStyle);
                GUILayout.TextArea(row.ServerPreviewHex, GUILayout.Height(46f));
            }

            if (row.Comparison != null)
            {
                GUILayout.Label("Compare", BoldGreenLabelStyle);
                GUILayout.Label($"{row.Comparison.PrimaryKind}: {row.Comparison.Summary}");
                if (!row.Comparison.KnownRevisionMatchesServer)
                {
                    GUILayout.Label("Note: client revision ≠ server revision (may be normal before snapshot apply).");
                }

                DrawRecordSections(row.Comparison);

                if (row.Comparison.SemanticDiagnostics.Count > 0)
                {
                    GUILayout.Label("=== Semantic (cfg) ===", BoldGreenLabelStyle);
                    for (var i = 0;
                        i < row.Comparison.SemanticDiagnostics.Count &&
                        i < PersistentSyncAuditSemanticLimits.MaxSemanticDiagnosticLines;
                        i++)
                    {
                        GUILayout.Label(row.Comparison.SemanticDiagnostics[i]);
                    }
                }

                for (var i = 0; i < row.Comparison.Details.Count && i < 32; i++)
                {
                    GUILayout.Label(row.Comparison.Details[i]);
                }
            }
        }

        private static void DrawDomainHeader(PersistentSyncDomainAuditRow row)
        {
            var status = FormatDomainStatus(row);
            GUILayout.Label($"{status}  {row.DomainId}  wire={row.WireId}", BoldGreenLabelStyle);
            GUILayout.Label(
                $"revision client={row.ClientKnownRevision} server={row.ServerRevision}  authority={row.AuthorityPolicy}");
        }

        private static void DrawRecordSections(PersistentSyncAuditComparisonResult cmp)
        {
            if (cmp.Records == null || cmp.Records.Count == 0)
            {
                return;
            }

            var onlyOnServer = cmp.Records.Where(x =>
                x.Kind == PersistentSyncAuditDifferenceKind.MissingOnClient).ToList();
            var onlyOnClient = cmp.Records.Where(x =>
                x.Kind == PersistentSyncAuditDifferenceKind.MissingOnServer).ToList();
            var other = cmp.Records.Where(x =>
                x.Kind != PersistentSyncAuditDifferenceKind.MissingOnClient &&
                x.Kind != PersistentSyncAuditDifferenceKind.MissingOnServer).ToList();

            if (onlyOnServer.Count > 0)
            {
                GUILayout.Label($"Server-only rows ({onlyOnServer.Count}):", BoldGreenLabelStyle);
                foreach (var rec in onlyOnServer.Take(24))
                {
                    GUILayout.Label($"  key={rec.Key} server={rec.Server}");
                }
            }

            if (onlyOnClient.Count > 0)
            {
                GUILayout.Label($"Client-only rows ({onlyOnClient.Count}):", BoldGreenLabelStyle);
                foreach (var rec in onlyOnClient.Take(24))
                {
                    GUILayout.Label($"  key={rec.Key} local={rec.Local}");
                }
            }

            if (other.Count > 0)
            {
                GUILayout.Label($"Other records ({other.Count}):", BoldGreenLabelStyle);
                foreach (var rec in other.Take(32))
                {
                    GUILayout.Label($"  kind={rec.Kind} key={rec.Key} local={rec.Local} server={rec.Server}");
                }
            }
        }

        private void DrawFooter()
        {
            var coord = PersistentSyncAuditCoordinator.Instance;
            GUILayout.Label(
                $"completedUtc={coord.LastCompletedUtc:O}  inProgress={coord.RefreshInProgress}  catalogWire(client)={PersistentSyncCatalogWire.CurrentVersion}");
        }

        private string BuildTextBundle(BundleCopyMode mode)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== KSPMP PersistentSync Domain Analyzer bundle ===");
            sb.AppendLine($"build=DEBUG scene={HighLogic.LoadedScene} net={MainSystem.NetworkState}");
            sb.AppendLine($"player={SettingsSystem.CurrentSettings?.PlayerName}");
            sb.AppendLine($"game={(HighLogic.CurrentGame == null ? "null" : HighLogic.CurrentGame.Mode.ToString())}");
            sb.AppendLine($"correlation={PersistentSyncAuditCoordinator.Instance.LastCorrelationId} utc={PersistentSyncAuditCoordinator.Instance.LastCompletedUtc:O}");
            sb.AppendLine($"catalogWireVersion(clientSupports)={PersistentSyncCatalogWire.CurrentVersion}");
            sb.AppendLine();

            AppendCatalogSection(sb);
            AppendContractProgressionDiagnostics(sb);

            var rows = PersistentSyncAuditCoordinator.Instance.LastRows;
            IEnumerable<string> ids;
            switch (mode)
            {
                case BundleCopyMode.All:
                    ids = PersistentSyncDomainCatalog.AllOrdered.Select(d => d.DomainId);
                    break;
                case BundleCopyMode.SelectedSemantic:
                    if (_selectedIndex >= 0 && _selectedIndex < _orderedDomainIds.Count)
                    {
                        ids = new[] { _orderedDomainIds[_selectedIndex] };
                    }
                    else
                    {
                        ids = Enumerable.Empty<string>();
                    }

                    break;
                case BundleCopyMode.MinimalRepro:
                    // Spec: always include non-OK domains; add Achievements, Contracts, Reputation, Technology, GameLaunchId
                    // only when contract-generation debugging is active (mapped to Debug7 — contract PARAM diagnostics).
                    var baseline = new HashSet<string>(
                        rows.Values
                            .Where(r => r.Comparison != null && r.Comparison.PrimaryKind != PersistentSyncAuditDifferenceKind.Ok)
                            .Select(r => r.DomainId),
                        StringComparer.Ordinal);
                    if (SettingsSystem.CurrentSettings.Debug7)
                    {
                        foreach (var x in new[]
                        {
                            PersistentSyncDomainNames.Achievements,
                            PersistentSyncDomainNames.Contracts,
                            PersistentSyncDomainNames.Reputation,
                            PersistentSyncDomainNames.Technology,
                            PersistentSyncDomainNames.GameLaunchId
                        })
                        {
                            baseline.Add(x);
                        }
                    }

                    ids = baseline;
                    break;
                case BundleCopyMode.RawSelected:
                    if (_selectedIndex >= 0 && _selectedIndex < _orderedDomainIds.Count)
                    {
                        ids = new[] { _orderedDomainIds[_selectedIndex] };
                    }
                    else
                    {
                        ids = Enumerable.Empty<string>();
                    }

                    break;
                default:
                    ids = Enumerable.Empty<string>();
                    break;
            }

            var rawHex = mode == BundleCopyMode.RawSelected;

            foreach (var domainId in ids)
            {
                if (!rows.TryGetValue(domainId, out var row))
                {
                    sb.AppendLine($"--- {domainId} ---");
                    sb.AppendLine("(no row)");
                    sb.AppendLine();
                    continue;
                }

                sb.AppendLine($"--- {row.DomainId} wire={row.WireId} ---");
                sb.AppendLine(
                    $"rev client={row.ClientKnownRevision} server={row.ServerRevision} match={row.Comparison?.KnownRevisionMatchesServer}");
                if (row.Comparison != null)
                {
                    sb.AppendLine($"primaryKind={row.Comparison.PrimaryKind}");
                }

                sb.AppendLine($"bytes local={row.LocalPayloadBytes} server={row.ServerPayloadBytes}");
                sb.AppendLine($"hash8 local={row.LocalHash8} server={row.ServerHash8}");
                if (!string.IsNullOrEmpty(row.LocalUnavailableReason))
                {
                    sb.AppendLine($"localUnavailable={row.LocalUnavailableReason}");
                }

                if (!string.IsNullOrEmpty(row.ServerError))
                {
                    sb.AppendLine($"serverError={row.ServerError}");
                }

                if (row.Comparison != null)
                {
                    sb.AppendLine($"summary={row.Comparison.Summary}");
                    foreach (var rec in row.Comparison.Records.Take(64))
                    {
                        sb.AppendLine($"record kind={rec.Kind} key={rec.Key} local={rec.Local} server={rec.Server}");
                    }

                    foreach (var line in row.Comparison.Details.Take(48))
                    {
                        sb.AppendLine($"detail={line}");
                    }

                    if (row.Comparison.SemanticDiagnostics.Count > 0)
                    {
                        sb.AppendLine("=== Semantic (cfg) ===");
                        foreach (var sem in row.Comparison.SemanticDiagnostics.Take(
                            PersistentSyncAuditSemanticLimits.MaxSemanticDiagnosticLines))
                        {
                            sb.AppendLine(sem);
                        }
                    }
                }

                if (rawHex && row.Comparison != null)
                {
                    sb.AppendLine("localPreviewHex=" + row.LocalPreviewHex);
                    sb.AppendLine("serverPreviewHex=" + row.ServerPreviewHex);
                }

                sb.AppendLine();
            }

            if (sb.Length > 120_000)
            {
                return sb.ToString(0, 120_000) + "\n...[truncated]";
            }

            return sb.ToString();
        }

        private static void AppendCatalogSection(StringBuilder sb)
        {
            sb.AppendLine("=== Catalog ===");
            foreach (var def in PersistentSyncDomainCatalog.AllOrdered)
            {
                sb.AppendLine(
                    $"domain={def.DomainId} wire={def.WireId} scenario={def.ScenarioName ?? ""} slot={def.MaterializationSlot} modes={def.InitialSyncGameModes} reqCaps={def.RequiredCapabilities}");
            }

            sb.AppendLine();
        }

        private static void AppendContractProgressionDiagnostics(StringBuilder sb)
        {
            foreach (var line in EnumerateContractProgressionDiagnosticLines())
            {
                sb.AppendLine(line);
            }

            sb.AppendLine();
        }

        private static IEnumerable<string> EnumerateContractProgressionDiagnosticLines()
        {
            yield return "=== Contract Progression Runtime ===";
            yield return $"contractLockOwner={LockSystem.LockQuery.ContractLockOwner() ?? "<none>"}";

            var cs = ContractSystem.Instance;
            if (cs == null)
            {
                yield return "contractSystem=null";
            }
            else
            {
                var main = cs.Contracts ?? new List<Contract>();
                var finished = cs.ContractsFinished ?? new List<Contract>();
                yield return
                    $"contracts main={main.Count} active={main.Count(c => c != null && c.ContractState == Contract.State.Active)} " +
                    $"offerPoolLike={main.Count(ShareContractsSystem.IsMissionControlOfferPoolContract)} " +
                    $"finished={finished.Count} finishedCompleted={finished.Count(c => c != null && c.ContractState == Contract.State.Completed)}";

                foreach (var c in main.Where(ShareContractsSystem.IsMissionControlOfferPoolContract).Take(20))
                {
                    yield return $"offer type={c.GetType().Name} state={c.ContractState} title={SanitizeBundleValue(c.Title)}";
                }

                foreach (var c in finished.Where(c => c != null && c.ContractState == Contract.State.Completed).Take(20))
                {
                    yield return
                        $"finished type={c.GetType().Name} title={SanitizeBundleValue(c.Title)} " +
                        $"identity={SanitizeBundleValue(ShareContractsSystem.BuildRuntimeContractIdentityKey(c))}";
                }

                foreach (var line in ShareAchievementsSystem.EnumerateFinishedContractProgressParameterDiagnosticLines(40))
                {
                    yield return line;
                }
            }

            var pt = ProgressTracking.Instance;
            if (pt == null)
            {
                yield return "progressTracking=null";
                yield break;
            }

            foreach (var id in new[] { "FirstLaunch", "ReachedSpace", "Orbit" })
            {
                var node = ShareAchievementsSystem.TryResolveStockTutorialGateNode(id);
                yield return
                    node == null
                        ? $"tutorialGate id={id} resolved=false"
                        : $"tutorialGate id={id} resolved=true reached={node.IsReached} complete={node.IsComplete}";
            }

            var reachedOrComplete = EnumerateProgressNodes(pt.achievementTree)
                .Where(x => x.Node != null && (x.Node.IsReached || x.Node.IsComplete))
                .Take(96)
                .ToList();
            yield return $"progressReachedOrCompleteShown={reachedOrComplete.Count}";
            foreach (var entry in reachedOrComplete)
            {
                yield return
                    $"progress path={SanitizeBundleValue(entry.Path)} id={entry.Node.Id} " +
                    $"reached={entry.Node.IsReached} complete={entry.Node.IsComplete}";
            }
        }

        private static string SanitizeBundleValue(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, Math.Max(0, max - 1)) + "…";
        }

        private static IEnumerable<ProgressNodeEntry> EnumerateProgressNodes(ProgressTree tree)
        {
            if (tree == null)
            {
                yield break;
            }

            for (var i = 0; i < tree.Count; i++)
            {
                foreach (var entry in EnumerateProgressNodes(tree[i], tree[i]?.Id ?? "<null>"))
                {
                    yield return entry;
                }
            }
        }

        private static IEnumerable<ProgressNodeEntry> EnumerateProgressNodes(ProgressNode node, string path)
        {
            if (node == null)
            {
                yield break;
            }

            yield return new ProgressNodeEntry { Node = node, Path = path };

            foreach (var child in EnumerateChildProgressNodes(node))
            {
                var childPath = string.Concat(path, "/", child.Id ?? "<null>");
                foreach (var entry in EnumerateProgressNodes(child, childPath))
                {
                    yield return entry;
                }
            }
        }

        private static IEnumerable<ProgressNode> EnumerateChildProgressNodes(ProgressNode parent)
        {
            if (parent == null)
            {
                yield break;
            }

            var subtree = parent.Subtree;
            if (subtree != null)
            {
                for (var i = 0; i < subtree.Count; i++)
                {
                    var child = subtree[i];
                    if (child != null)
                    {
                        yield return child;
                    }
                }
            }

            foreach (var fieldName in new[] { "children", "_children", "childNodes", "nodes" })
            {
                var field = parent.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null)
                {
                    continue;
                }

                var value = field.GetValue(parent);
                if (value is ProgressNode[] arr)
                {
                    foreach (var child in arr)
                    {
                        if (child != null)
                        {
                            yield return child;
                        }
                    }

                    yield break;
                }

                if (value is IList list)
                {
                    foreach (var item in list)
                    {
                        if (item is ProgressNode child)
                        {
                            yield return child;
                        }
                    }

                    yield break;
                }
            }
        }

        private sealed class ProgressNodeEntry
        {
            public ProgressNode Node { get; set; }

            public string Path { get; set; }
        }

        private static void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            GUIUtility.systemCopyBuffer = text;
        }

        public override void RemoveWindowLock()
        {
            if (IsWindowLocked)
            {
                IsWindowLocked = false;
                InputLockManager.RemoveControlLock("KSPMP_DomainAnalyzerLock");
            }
        }

        public override void CheckWindowLock()
        {
            if (Display)
            {
                if (MainSystem.NetworkState < ClientState.Running || HighLogic.LoadedSceneIsFlight)
                {
                    RemoveWindowLock();
                    return;
                }

                var mousePos = Input.mousePosition;
                mousePos.y = Screen.height - mousePos.y;
                var shouldLock = WindowRect.Contains(mousePos);
                if (shouldLock && !IsWindowLocked)
                {
                    InputLockManager.SetControlLock(LmpImguiInputLockMask.WindowMouseCapture, "KSPMP_DomainAnalyzerLock");
                    IsWindowLocked = true;
                }
                else if (!shouldLock && IsWindowLocked)
                {
                    RemoveWindowLock();
                }
            }
            else if (IsWindowLocked)
            {
                RemoveWindowLock();
            }
        }
    }
}
#endif
