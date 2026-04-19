using KSP.UI.Screens;
using LmpClient;
using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.ShareProgress;
using LmpClient.Systems.SettingsSys;
using LmpClient.Utilities;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace LmpClient.Systems.ShareTechnology
{
    public class ShareTechnologySystem : ShareProgressBaseSystem<ShareTechnologySystem, ShareTechnologyMessageSender, ShareTechnologyMessageHandler>
    {
        public override string SystemName { get; } = nameof(ShareTechnologySystem);

        private const string RdControllerUpdatePanelRetryRoutine = nameof(ShareTechnologySystem) + ".RdControllerUpdatePanelRetry";

        private const string PsRnDCoalescedRefreshRoutine = nameof(ShareTechnologySystem) + ".PsRnDCoalescedRefresh";

        private const string RnDRecoverAfterStockTreeRoutine = nameof(ShareTechnologySystem) + ".RnDRecoverAfterStockTree";

        /// <summary>
        /// Stock <see cref="RDController.UpdatePanel"/> can NRE for many frames after RnDComplex spawn; two frames was not enough in practice.
        /// </summary>
        private const int RdControllerUpdatePanelMaxAttempts = 24;

        /// <summary>
        /// After this many frames, run one coalesced PersistentSync R&amp;D UI refresh so stock can finish wiring
        /// <see cref="RDController"/> after <see cref="ResearchAndDevelopment.RefreshTechTreeUI"/>.
        /// </summary>
        private const int PersistentSyncRnDUiCoalescedFramesDelay = 8;

        private static bool _persistentSyncRnDUiRefreshScheduled;

        private static bool _persistentSyncRnDUiRefreshWantsTechTreeReload;

        /// <summary>
        /// After a local research completes, stock often clears the graph selection; the next full tree refresh should
        /// re-select this tech so the side panel binds fresh <c>partsPurchased</c> state.
        /// </summary>
        private static string _lastLocalResearchTechIdForSidePanelReselect;

        /// <summary>
        /// Verbose R&amp;D side-panel diagnostics (grep <c>[RnDUiDiag]</c>). Remove or gate once the stock UI mismatch is fixed.
        /// </summary>
        private const string RnDUiDiagPrefix = "[PersistentSync][RnDUiDiag]";

        private ShareTechnologyEvents ShareTechnologyEvents { get; } = new ShareTechnologyEvents();

        private static void RnDUiDiag(string message)
        {
            LunaLog.Log($"{RnDUiDiagPrefix} {message}");
        }

        protected override bool ShareSystemReady => ResearchAndDevelopment.Instance != null;

        protected override GameMode RelevantGameModes => GameMode.Career | GameMode.Science;

        protected override bool UseSessionApplicabilityInsteadOfGameModeMask => true;

        protected override bool IsShareSystemApplicableForSession()
        {
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            return PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainId.Technology,
                SettingsSystem.ServerSettings.GameMode,
                in caps);
        }

        protected override void OnEnabled()
        {
            base.OnEnabled();

            GameEvents.OnTechnologyResearched.Add(ShareTechnologyEvents.TechnologyResearched);
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            _persistentSyncRnDUiRefreshScheduled = false;
            _persistentSyncRnDUiRefreshWantsTechTreeReload = false;
            _lastLocalResearchTechIdForSidePanelReselect = null;

            //Always try to remove the event, as when we disconnect from a server the server settings will get the default values
            GameEvents.OnTechnologyResearched.Remove(ShareTechnologyEvents.TechnologyResearched);
        }

        /// <summary>
        /// PersistentSync often applies <see cref="PersistentSyncDomainId.Technology"/> and <see cref="PersistentSyncDomainId.PartPurchases"/>
        /// in the same <see cref="PersistentSync.PersistentSyncReconciler.FlushPendingState"/> pass. Each domain used to call
        /// into <see cref="RDController"/> immediately while the R&amp;D complex UI is still being built, which doubled
        /// <see cref="RDController.UpdatePanel"/> traffic and could NRE for the entire retry window. Merge into one deferred refresh.
        /// </summary>
        public void SchedulePersistentSyncRnDUiCoalescedRefresh(bool wantsTechTreeReload)
        {
            if (!Enabled)
            {
                return;
            }

            _persistentSyncRnDUiRefreshWantsTechTreeReload |= wantsTechTreeReload;
            if (_persistentSyncRnDUiRefreshScheduled)
            {
                return;
            }

            _persistentSyncRnDUiRefreshScheduled = true;
            CoroutineUtil.StartFrameDelayedRoutine(PsRnDCoalescedRefreshRoutine, RunPersistentSyncRnDUiCoalescedRefresh, PersistentSyncRnDUiCoalescedFramesDelay);
        }

        private static void RunPersistentSyncRnDUiCoalescedRefresh()
        {
            // Allow Schedule(...) to queue another pass while this run executes (same-thread re-entrancy safe).
            _persistentSyncRnDUiRefreshScheduled = false;

            var wantsTree = _persistentSyncRnDUiRefreshWantsTechTreeReload;
            _persistentSyncRnDUiRefreshWantsTechTreeReload = false;

            var sys = Singleton;
            if (sys == null || !sys.Enabled)
            {
                return;
            }

            if (RDController.Instance != null)
            {
                if (wantsTree)
                {
                    sys.RefreshResearchAndDevelopmentUiAdapters("PersistentSyncSnapshotApply");
                }
                else
                {
                    sys.RefreshResearchAndDevelopmentPurchasesOnly("PersistentSyncSnapshotApply");
                }
            }
            else
            {
                sys.RefreshEditorAfterTechSnapshot("PersistentSyncSnapshotApply");
            }
        }

        /// <summary>
        /// Refreshes derived R&amp;D/editor UI after local tech truth was already corrected.
        /// Reloads the tech tree only when the R&amp;D complex is open; otherwise stock builds the tree from
        /// <see cref="ResearchAndDevelopment.Instance"/> when the player opens the facility. Calling
        /// <see cref="ResearchAndDevelopment.RefreshTechTreeUI"/> repeatedly while the UI is absent or
        /// mid-build stacks duplicate <see cref="RDNode"/> instances (PersistentSync flush + part list refresh).
        /// </summary>
        public void RefreshResearchAndDevelopmentUiAdapters(string source)
        {
            if (RDController.Instance != null)
            {
                RefreshTechTree(source);
                // Selection / purchase panel recovery is handled in Harmony postfix on ResearchAndDevelopment.RefreshTechTreeUI
                // (runs for this call and any other stock tree rebuild) so we always wait until after stock finishes wiring.
            }
            else
            {
                _lastLocalResearchTechIdForSidePanelReselect = null;
            }

            TryRefreshResearchAndDevelopmentControllerPartUi(source);
            RefreshEditorPartsList(source);
        }

        /// <summary>
        /// Harmony postfix on <see cref="ResearchAndDevelopment.RefreshTechTreeUI"/>: schedule a short delayed recovery
        /// when the player just researched a tech locally (see <see cref="RegisterLocalResearchTechForSidePanelReselect"/>).
        /// </summary>
        public static void NotifyStockRefreshTechTreeUiCompleted()
        {
            if (MainSystem.Singleton == null)
            {
                RnDUiDiag("NotifyStockRefreshTechTreeUiCompleted: skip MainSystem.Singleton=null");
                return;
            }

            var techId = _lastLocalResearchTechIdForSidePanelReselect;
            if (string.IsNullOrEmpty(techId))
            {
                RnDUiDiag("NotifyStockRefreshTechTreeUiCompleted: skip pendingTechId empty");
                return;
            }

            if (Singleton == null || !Singleton.Enabled)
            {
                RnDUiDiag($"NotifyStockRefreshTechTreeUiCompleted: skip ShareTechnologySystem disabled singleton={(Singleton == null ? "null" : "ok")}");
                return;
            }

            RnDUiDiag($"NotifyStockRefreshTechTreeUiCompleted: schedule RecoverRnDUiAfterStockTreeFlush in 0.12s techId={techId}");
            CoroutineUtil.StartDelayedRoutine(
                RnDRecoverAfterStockTreeRoutine,
                () => RecoverRnDUiAfterStockTreeFlush(techId),
                0.12f);
        }

        private static void RecoverRnDUiAfterStockTreeFlush(string techId)
        {
            RnDUiDiag($"RecoverRnDUiAfterStockTreeFlush: begin techId={techId} RnDTechTree={(AssetBase.RnDTechTree == null ? "null" : "ok")} RDController={(RDController.Instance ? "ok" : "null")}");
            if (string.IsNullOrEmpty(techId) || AssetBase.RnDTechTree == null)
            {
                RnDUiDiag("RecoverRnDUiAfterStockTreeFlush: abort (no techId or RnDTechTree)");
                return;
            }

            var controller = RDController.Instance;
            if (!controller)
            {
                RnDUiDiag("RecoverRnDUiAfterStockTreeFlush: abort RDController.Instance=null");
                return;
            }

            var treeTech = TryResolveRdtForTechId(techId);
            if (treeTech == null)
            {
                RnDUiDiag($"RecoverRnDUiAfterStockTreeFlush: TryResolveRdtForTechId returned null techId={techId}");
            }
            else
            {
                LogRdtTechSidePanelTruth("Recover pre-voidSweep", treeTech);
            }

            LogRdControllerAndPartListReflection("Recover before navigation", controller);

            if (treeTech != null)
            {
                TryInvokeRdControllerVoidMethodsTakingRdtTech(controller, treeTech, "Recover void(RDTech) sweep");
            }

            TryReselectRnDTechOnControllerForSidePanel(techId, "Recover");
            LogRdControllerAndPartListReflection("Recover after TryReselect", controller);
            TryRefreshResearchAndDevelopmentControllerPartUi($"StockRefreshTechTreeUi:{techId}");
            Singleton?.RefreshResearchAndDevelopmentPurchasesOnly($"StockRefreshTechTreeUi:{techId}");
            LogRdControllerAndPartListReflection("Recover after RefreshPurchasesOnly", controller);
            RnDUiDiag("RecoverRnDUiAfterStockTreeFlush: end");
        }

        /// <summary>
        /// Stock <see cref="RDController"/> selection entry points vary by KSP build; try every instance void(RDTech) that
        /// looks like navigation before falling back to <see cref="TryReselectRnDTechOnControllerForSidePanel"/>.
        /// </summary>
        private static void TryInvokeRdControllerVoidMethodsTakingRdtTech(RDController controller, RDTech treeTech, string tag)
        {
            if (!controller || treeTech == null)
            {
                return;
            }

            var candidates = new List<string>();
            var invokedOk = new List<string>();
            var invokeFail = new List<string>();

            try
            {
                foreach (var method in typeof(RDController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.IsAbstract || method.IsGenericMethodDefinition || method.ReturnType != typeof(void))
                    {
                        continue;
                    }

                    var ps = method.GetParameters();
                    if (ps.Length != 1 || !ps[0].ParameterType.IsAssignableFrom(typeof(RDTech)))
                    {
                        continue;
                    }

                    if (!RdControllerMethodNameLooksLikeUiNavigation(method.Name))
                    {
                        continue;
                    }

                    candidates.Add(method.Name);
                    try
                    {
                        method.Invoke(controller, new object[] { treeTech });
                        invokedOk.Add(method.Name);
                    }
                    catch (Exception e)
                    {
                        invokeFail.Add($"{method.Name}:{e.GetType().Name}");
                    }
                }
            }
            catch (Exception e)
            {
                RnDUiDiag($"{tag}: void(RDTech) sweep outer catch {e.GetType().Name}: {e.Message}");
                return;
            }

            RnDUiDiag(
                $"{tag}: void(RDTech) nav candidates={candidates.Count} [{string.Join(",", candidates)}] ok={invokedOk.Count} [{string.Join(",", invokedOk)}] " +
                $"throws={invokeFail.Count} [{string.Join(",", invokeFail)}]");
        }

        private static bool RdControllerMethodNameLooksLikeUiNavigation(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            foreach (var hint in new[]
                     {
                         "Select", "Show", "Focus", "Set", "Push", "Open", "Queue", "Display", "Switch", "Highlight",
                         "Zoom", "Navigate", "Pick", "Activate"
                     })
            {
                if (name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Updates the R&amp;D side panel part list and VAB/SPH part browser without reloading the entire tech tree.
        /// Used after part-purchase or experimental-part snapshots so one reconciler pass does not invoke
        /// <see cref="ResearchAndDevelopment.RefreshTechTreeUI"/> twice (Technology domain then PartPurchases domain).
        /// </summary>
        public void RefreshResearchAndDevelopmentPurchasesOnly(string source)
        {
            // Part-purchase snapshots can arrive after a local unlock; re-apply selection without consuming the
            // pending id so a later full tree refresh can still re-select once after RefreshTechTreeUI.
            TryReselectPendingLocalResearchTechForSidePanel();
            TryRefreshResearchAndDevelopmentControllerPartUi(source);
            RefreshEditorPartsList(source);
        }

        /// <summary>
        /// When the R&amp;D building is closed, tech state is already in <see cref="ResearchAndDevelopment.Instance"/>;
        /// refresh editor part categories only.
        /// </summary>
        public void RefreshEditorAfterTechSnapshot(string source)
        {
            RefreshEditorPartsList(source);
        }

        private static void RefreshTechTree(string source)
        {
            try
            {
                ResearchAndDevelopment.RefreshTechTreeUI();
                LunaLog.Log($"[PersistentSync] technology UI refresh source={source} adapter=tech-tree");
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[PersistentSync] technology UI refresh failed source={source} adapter=tech-tree error={e}");
            }
        }

        /// <summary>
        /// Refreshes the R&amp;D side panel. <see cref="RDPartList.Refresh"/> can throw when stock has no selected node yet;
        /// failures are swallowed so <see cref="RDController.UpdatePanel"/> still runs.
        /// </summary>
        public static void TryRefreshResearchAndDevelopmentControllerPartUi(string source)
        {
            var controller = RDController.Instance;
            if (!controller || !controller.partList)
            {
                // RDController exists before partList is wired during RnDComplex spawn; UpdatePanel() NREs in that window.
                RnDUiDiag(
                    $"TryRefreshPartUi source={source}: skip !(controller&&partList) controller={(controller ? "ok" : "null")} " +
                    $"partList={(controller && controller.partList ? "ok" : "null")}");
                return;
            }

            RnDUiDiag($"TryRefreshPartUi source={source}: enter (will partList.Refresh + UpdatePanel)");
            TryRefreshResearchAndDevelopmentControllerPartUiCore(source, controller, updatePanelAttempt: 0);
        }

        private static void TryRefreshResearchAndDevelopmentControllerPartUiCore(string source, RDController controller, int updatePanelAttempt)
        {
            try
            {
                // Re-running Refresh every frame can interact badly with half-built stock UI; retry passes only nudge UpdatePanel.
                if (updatePanelAttempt == 0)
                {
                    try
                    {
                        controller.partList.Refresh();
                    }
                    catch (Exception refreshEx)
                    {
                        // Stock RDPartList.SetupParts can throw when no RDNode is selected yet; skip noisy failures.
                        RnDUiDiag(
                            $"TryRefreshPartUi source={source}: partList.Refresh threw {refreshEx.GetType().Name}: {refreshEx.Message}");
                    }
                }

                try
                {
                    controller.UpdatePanel();
                    if (updatePanelAttempt == 0)
                    {
                        RnDUiDiag($"TryRefreshPartUi source={source}: UpdatePanel ok attempt={updatePanelAttempt}");
                        LogRdControllerAndPartListReflection($"TryRefreshPartUi post-UpdatePanel source={source}", controller);
                    }
                }
                catch (NullReferenceException nre)
                {
                    if (updatePanelAttempt == 0)
                    {
                        RnDUiDiag($"TryRefreshPartUi source={source}: UpdatePanel NRE (scheduling retry) msg={nre.Message}");
                    }

                    if (updatePanelAttempt + 1 >= RdControllerUpdatePanelMaxAttempts)
                    {
                        // Stock can keep NRE-ing here while the tree + RnD tech state are already correct (RefreshTechTreeUI /
                        // proto mirror). UpdatePanel is best-effort for the side panel; do not log as a hard failure.
                        LunaLog.Log(
                            $"[PersistentSync] technology UI refresh: skipped RDController.UpdatePanel after {RdControllerUpdatePanelMaxAttempts} null-ref attempts " +
                            $"(side panel is optional; tech-tree + R&D state sync already ran) source={source}");
                        RnDUiDiag($"TryRefreshPartUi source={source}: gave up UpdatePanel after {RdControllerUpdatePanelMaxAttempts} NREs");
                        return;
                    }

                    var nextAttempt = updatePanelAttempt + 1;
                    CoroutineUtil.StartFrameDelayedRoutine($"{RdControllerUpdatePanelRetryRoutine}_{nextAttempt}", () =>
                    {
                        var c = RDController.Instance;
                        if (!c || !c.partList)
                        {
                            return;
                        }

                        TryRefreshResearchAndDevelopmentControllerPartUiCore(source, c, nextAttempt);
                    }, 1);
                    return;
                }

                LunaLog.Log($"[PersistentSync] technology UI refresh source={source} adapter=rd-controller");
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[PersistentSync] technology UI refresh failed source={source} adapter=rd-controller error={e}");
            }
        }

        private static void RefreshEditorPartsList(string source)
        {
            if (!EditorPartList.Instance)
            {
                return;
            }

            try
            {
                EditorPartList.Instance.Refresh();
                LunaLog.Log($"[PersistentSync] technology UI refresh source={source} adapter=editor-parts");
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[PersistentSync] technology UI refresh failed source={source} adapter=editor-parts error={e}");
            }
        }

        /// <summary>
        /// Remember which tech the player just finished researching locally so tree rebuilds and purchase flushes can
        /// re-select it (stock clears selection and leaves the side panel on stale purchase affordances).
        /// </summary>
        internal static void RegisterLocalResearchTechForSidePanelReselect(string techId)
        {
            if (string.IsNullOrEmpty(techId))
            {
                return;
            }

            _lastLocalResearchTechIdForSidePanelReselect = techId;
        }

        private static void LogRdtTechSidePanelTruth(string tag, RDTech tech)
        {
            if (tech == null)
            {
                RnDUiDiag($"{tag} treeTech=null");
                return;
            }

            try
            {
                var purchased = tech.partsPurchased;
                var n = purchased?.Count ?? -1;
                RnDUiDiag($"{tag} techId={tech.techID} state={tech.state} partsPurchasedCount={n}");
            }
            catch (Exception e)
            {
                RnDUiDiag($"{tag} RDTech snapshot failed {e.GetType().Name}: {e.Message}");
            }
        }

        private static int CountSceneRdNodesForTechId(string techId)
        {
            if (string.IsNullOrEmpty(techId))
            {
                return 0;
            }

            var count = 0;
            try
            {
                foreach (var node in UnityEngine.Object.FindObjectsOfType<RDNode>())
                {
                    if (TryGetTechIdFromRdNode(node) == techId)
                    {
                        count++;
                    }
                }
            }
            catch
            {
                return -1;
            }

            return count;
        }

        private static string DescribeRdtOrRdNodeRef(object v)
        {
            if (v == null)
            {
                return "null";
            }

            try
            {
                if (v is RDTech rd)
                {
                    var n = rd.partsPurchased?.Count ?? -1;
                    return $"RDTech(id={rd.techID},state={rd.state},purchased={n})";
                }

                if (v is RDNode node)
                {
                    var id = TryGetTechIdFromRdNode(node);
                    var go = node.gameObject != null;
                    return $"RDNode(techId={id},go={go},goName={node.gameObject?.name})";
                }
            }
            catch (Exception e)
            {
                return $"{v.GetType().Name}<describe {e.GetType().Name}>";
            }

            return v.GetType().Name;
        }

        private static void LogAssignableMembers(string tag, object host, Type expectedType, int maxEntries = 24)
        {
            if (host == null || expectedType == null)
            {
                return;
            }

            var entries = new List<string>();
            try
            {
                var t = host.GetType();
                var done = false;
                while (t != null && entries.Count < maxEntries && !done)
                {
                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (!expectedType.IsAssignableFrom(f.FieldType))
                        {
                            continue;
                        }

                        object v = null;
                        try
                        {
                            v = f.GetValue(host);
                        }
                        catch
                        {
                            entries.Add($"{t.Name}.{f.Name}=<read err>");
                            if (entries.Count >= maxEntries)
                            {
                                done = true;
                            }

                            continue;
                        }

                        string desc;
                        try
                        {
                            desc = DescribeRdtOrRdNodeRef(v);
                        }
                        catch (Exception e)
                        {
                            desc = $"<describe {e.GetType().Name}>";
                        }

                        entries.Add($"{t.Name}.{f.Name}={desc}");
                        if (entries.Count >= maxEntries)
                        {
                            done = true;
                            break;
                        }
                    }

                    if (done)
                    {
                        break;
                    }

                    foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (!p.CanRead || p.GetIndexParameters().Length != 0 || !expectedType.IsAssignableFrom(p.PropertyType))
                        {
                            continue;
                        }

                        object v = null;
                        try
                        {
                            v = p.GetValue(host, null);
                        }
                        catch
                        {
                            entries.Add($"{t.Name}.{p.Name}=<read err>");
                            if (entries.Count >= maxEntries)
                            {
                                done = true;
                            }

                            continue;
                        }

                        string desc;
                        try
                        {
                            desc = DescribeRdtOrRdNodeRef(v);
                        }
                        catch (Exception e)
                        {
                            desc = $"<describe {e.GetType().Name}>";
                        }

                        entries.Add($"{t.Name}.{p.Name}={desc}");
                        if (entries.Count >= maxEntries)
                        {
                            done = true;
                            break;
                        }
                    }

                    if (done)
                    {
                        break;
                    }

                    t = t.BaseType;
                }
            }
            catch (Exception e)
            {
                RnDUiDiag($"{tag} reflection failed {e.GetType().Name}: {e.Message}");
                return;
            }

            if (entries.Count == 0)
            {
                RnDUiDiag($"{tag}: no {expectedType.Name}-typed instance members on {host.GetType().Name}");
                return;
            }

            RnDUiDiag($"{tag}: {string.Join("; ", entries)}");
        }

        private static void LogRdPartListIListFields(string tag, RDPartList partList)
        {
            if (!partList)
            {
                return;
            }

            var lines = 0;
            try
            {
                var t = partList.GetType();
                while (t != null && lines < 8)
                {
                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (!typeof(IList).IsAssignableFrom(f.FieldType) || f.FieldType == typeof(string))
                        {
                            continue;
                        }

                        IList list = null;
                        try
                        {
                            list = f.GetValue(partList) as IList;
                        }
                        catch
                        {
                            continue;
                        }

                        if (list == null)
                        {
                            continue;
                        }

                        RnDUiDiag($"{tag} RDPartList IList {t.Name}.{f.Name} count={list.Count}");
                        lines++;
                        if (lines >= 8)
                        {
                            return;
                        }
                    }

                    t = t.BaseType;
                }
            }
            catch (Exception e)
            {
                RnDUiDiag($"{tag} RDPartList IList scan {e.GetType().Name}: {e.Message}");
            }
        }

        private static void LogRdControllerAndPartListReflection(string tag, RDController controller)
        {
            if (!controller)
            {
                RnDUiDiag($"{tag}: RDController=null");
                return;
            }

            RnDUiDiag($"{tag}: RDController concreteType={controller.GetType().FullName}");
            LogAssignableMembers($"{tag} RDController→RDTech", controller, typeof(RDTech), maxEntries: 16);
            LogAssignableMembers($"{tag} RDController→RDNode", controller, typeof(RDNode), maxEntries: 16);
            if (controller.partList)
            {
                RnDUiDiag($"{tag}: partList concreteType={controller.partList.GetType().FullName}");
                LogAssignableMembers($"{tag} RDPartList→RDTech", controller.partList, typeof(RDTech), maxEntries: 12);
                LogAssignableMembers($"{tag} RDPartList→RDNode", controller.partList, typeof(RDNode), maxEntries: 12);
                LogRdPartListIListFields(tag, controller.partList);
            }
            else
            {
                RnDUiDiag($"{tag}: RDController.partList=null (TryRefreshResearchAndDevelopmentControllerPartUi returns early)");
            }
        }

        private static string DescribeRdNodeButtonSummary(RDNode node)
        {
            if (node?.gameObject == null)
            {
                return "buttons=n/a";
            }

            try
            {
                var buttons = node.gameObject.GetComponentsInChildren<Button>(true);
                var total = buttons.Length;
                var usable = 0;
                foreach (var b in buttons)
                {
                    if (b != null && b.interactable && b.onClick != null)
                    {
                        usable++;
                    }
                }

                return $"buttons total={total} interactableOnClick={usable}";
            }
            catch (Exception e)
            {
                return $"buttons err {e.GetType().Name}";
            }
        }

        /// <summary>
        /// Re-run stock selection for <paramref name="techId"/> without consuming the pending id (used after snapshots).
        /// </summary>
        private static void TryReselectPendingLocalResearchTechForSidePanel()
        {
            if (string.IsNullOrEmpty(_lastLocalResearchTechIdForSidePanelReselect))
            {
                return;
            }

            TryReselectRnDTechOnControllerForSidePanel(_lastLocalResearchTechIdForSidePanelReselect, "PendingReselect");
        }

        /// <summary>
        /// Stock clears or desynchronizes the graph selection vs. <see cref="RDTech.partsPurchased"/> after multiplayer
        /// sync; re-drive the same entry points the player uses when clicking a node. Public for delayed coroutines;
        /// <paramref name="diagContext"/> labels <c>[RnDUiDiag]</c> lines (optional).
        /// </summary>
        public static void TryReselectRnDTechOnControllerForSidePanel(string techId, string diagContext = null)
        {
            var tag = string.IsNullOrEmpty(diagContext) ? "TryReselect" : diagContext;
            RnDUiDiag($"{tag}: begin techId={techId} sceneRDNodesForTech={CountSceneRdNodesForTechId(techId)}");
            if (string.IsNullOrEmpty(techId) || AssetBase.RnDTechTree == null)
            {
                RnDUiDiag($"{tag}: early-exit (no techId or RnDTechTree)");
                return;
            }

            var controller = RDController.Instance;
            if (!controller)
            {
                RnDUiDiag($"{tag}: early-exit RDController.Instance=null");
                return;
            }

            var treeTech = TryResolveRdtForTechId(techId);
            if (treeTech != null)
            {
                LogRdtTechSidePanelTruth($"{tag} treeRDTech(resolved)", treeTech);

                if (TryInvokeRdControllerSingleArg(controller, treeTech, out var viaTech))
                {
                    RnDUiDiag($"{tag}: RDController single-arg OK (RDTech) → {viaTech}");
                    return;
                }

                RnDUiDiag($"{tag}: RDController whitelisted single-arg(RDTech) missed; trying RDNode path");
            }
            else
            {
                RnDUiDiag($"{tag}: TryResolveRdtForTechId returned null; trying RDNode path");
            }

            var fromController = TryFindRdNodeForTechFromController(controller, techId);
            var rdNode = fromController ?? TryFindRdNodeForTechInScene(techId);
            RnDUiDiag(
                $"{tag}: RDNode fromController={(fromController != null)} sceneFallback={(fromController == null && rdNode != null)} " +
                $"{DescribeRdNodeButtonSummary(rdNode)}");

            if (rdNode == null)
            {
                RnDUiDiag($"{tag}: early-exit RDNode not found");
                return;
            }

            if (TryInvokeRdNodeUiActivate(rdNode))
            {
                RnDUiDiag($"{tag}: RDNode Button.onClick.Invoke returned true");
                return;
            }

            RnDUiDiag($"{tag}: RDNode button invoke path missed");

            if (TryInvokeRdControllerSingleArg(controller, rdNode, out var viaNode))
            {
                RnDUiDiag($"{tag}: RDController single-arg OK (RDNode) → {viaNode}");
                return;
            }

            TryInvokeRdControllerRdNodeAndBool(controller, rdNode, tag);
        }

        private static readonly string[] RdControllerRdtTechSelectionMethodNames =
        {
            "SelectTech", "ShowTech", "ShowTechDetails", "ShowTechNode", "SelectTechTreeNode", "SetSelectedTech",
            "OnTechTreeSelected", "SelectRDTech", "ShowRDTech", "ShowNode", "ShowTechPanel", "QueueTechView",
            "showTech", "selectTech"
        };

        private static readonly string[] RdControllerRdNodeSelectionMethodNames =
        {
            "SelectNode", "SetSelectedNode", "SelectRDNode", "ShowNode", "OnNodeSelected", "selectNode", "showNode"
        };

        private static bool TryInvokeRdControllerSingleArg(RDController controller, object argument)
        {
            return TryInvokeRdControllerSingleArg(controller, argument, out _);
        }

        private static bool TryInvokeRdControllerSingleArg(RDController controller, object argument, out string invokedDetail)
        {
            invokedDetail = null;
            if (controller == null || argument == null)
            {
                return false;
            }

            var argType = argument.GetType();
            var names = typeof(RDNode).IsAssignableFrom(argType)
                ? RdControllerRdNodeSelectionMethodNames
                : RdControllerRdtTechSelectionMethodNames;

            try
            {
                var ct = controller.GetType();
                foreach (var methodName in names)
                {
                    foreach (var method in ct.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (method.IsGenericMethodDefinition ||
                            !string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var ps = method.GetParameters();
                        if (ps.Length != 1 || !ps[0].ParameterType.IsAssignableFrom(argType))
                        {
                            continue;
                        }

                        method.Invoke(controller, new[] { argument });
                        invokedDetail = $"{ct.Name}.{method.Name}({argType.Name})";
                        return true;
                    }
                }

                // Last resort: any single-arg method assignable from arg whose name suggests selection.
                foreach (var method in ct.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    var ps = method.GetParameters();
                    if (ps.Length != 1 || !ps[0].ParameterType.IsAssignableFrom(argType))
                    {
                        continue;
                    }

                    if (!RdControllerMethodNameLooksLikeSelection(method.Name))
                    {
                        continue;
                    }

                    try
                    {
                        method.Invoke(controller, new[] { argument });
                        invokedDetail = $"{ct.Name}.{method.Name}({argType.Name})#nameHeuristic";
                        return true;
                    }
                    catch
                    {
                        // try next
                    }
                }
            }
            catch
            {
                // Stock API differs by version.
            }

            return false;
        }

        private static bool RdControllerMethodNameLooksLikeSelection(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            foreach (var hint in new[] { "Select", "Show", "Focus", "Display", "Open", "Set", "On", "Tech", "Node" })
            {
                if (name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Some KSP builds return <see cref="ProtoTechNode"/> (or other types) from <see cref="RnDTechTree.GetTreeTechs"/>;
        /// stock handlers match on <c>techID</c> without requiring <see cref="RDTech"/>.
        /// </summary>
        private static string TryGetTreeNodeTechId(object candidate)
        {
            if (candidate == null)
            {
                return null;
            }

            if ((object)candidate is RDTech rdt)
            {
                return rdt.techID;
            }

            try
            {
                var t = candidate.GetType();
                var p = t.GetProperty("techID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
                {
                    return p.GetValue(candidate, null) as string;
                }

                var f = t.GetField("techID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(string))
                {
                    return f.GetValue(candidate) as string;
                }
            }
            catch
            {
            }

            return null;
        }

        private static RDTech TryGetRdtFromRdNode(RDNode node)
        {
            if (node == null)
            {
                return null;
            }

            try
            {
                var nodeType = typeof(RDNode);
                foreach (var fieldName in new[] { "tech", "_tech", "m_tech", "rdTech", "linkedTech" })
                {
                    var field = nodeType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var v = field?.GetValue(node);
                    if (v is RDTech rd)
                    {
                        return rd;
                    }
                }

                foreach (var propName in new[] { "Tech", "RDTech" })
                {
                    var prop = nodeType.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var v = prop?.GetValue(node, null);
                    if (v is RDTech rd)
                    {
                        return rd;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>
        /// Resolves an <see cref="RDTech"/> for <paramref name="techId"/> for controller navigation / void(RDTech) hooks.
        /// Prefer a tree entry that is already <see cref="RDTech"/>; otherwise use <see cref="RDNode"/> embedded tech.
        /// </summary>
        private static RDTech TryResolveRdtForTechId(string techId)
        {
            if (string.IsNullOrEmpty(techId) || AssetBase.RnDTechTree == null)
            {
                return null;
            }

            var sawIdMatchNonRdt = false;
            foreach (var candidate in AssetBase.RnDTechTree.GetTreeTechs())
            {
                if (candidate == null)
                {
                    continue;
                }

                if (!string.Equals(TryGetTreeNodeTechId(candidate), techId, StringComparison.Ordinal))
                {
                    continue;
                }

                if ((object)candidate is RDTech rd)
                {
                    return rd;
                }

                sawIdMatchNonRdt = true;
            }

            var ctrl = RDController.Instance;
            var node = (ctrl ? TryFindRdNodeForTechFromController(ctrl, techId) : null) ?? TryFindRdNodeForTechInScene(techId);
            var fromNode = TryGetRdtFromRdNode(node);
            if (fromNode != null)
            {
                if (sawIdMatchNonRdt)
                {
                    RnDUiDiag($"ResolveRdt techId={techId}: used RDNode.tech (GetTreeTechs had id match but not RDTech)");
                }
                else
                {
                    RnDUiDiag($"ResolveRdt techId={techId}: used RDNode.tech (no GetTreeTechs id match as RDTech)");
                }

                return fromNode;
            }

            if (sawIdMatchNonRdt)
            {
                RnDUiDiag($"ResolveRdt techId={techId}: tree had non-RDT id match but RDNode did not yield RDTech");
            }

            return null;
        }

        private static string TryGetTechIdFromRdNode(RDNode node)
        {
            return TryGetRdtFromRdNode(node)?.techID;
        }

        private static RDNode TryFindRdNodeForTechFromController(RDController controller, string techId)
        {
            if (controller == null || string.IsNullOrEmpty(techId))
            {
                return null;
            }

            try
            {
                var ct = controller.GetType();
                foreach (var field in ct.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var match = FirstRdNodeMatchingTech(EnumerateRdNodesFromValue(field.GetValue(controller)), techId);
                    if (match != null)
                    {
                        return match;
                    }
                }

                foreach (var prop in ct.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!prop.CanRead)
                    {
                        continue;
                    }

                    var match = FirstRdNodeMatchingTech(EnumerateRdNodesFromValue(prop.GetValue(controller, null)), techId);
                    if (match != null)
                    {
                        return match;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static RDNode FirstRdNodeMatchingTech(IEnumerable nodes, string techId)
        {
            if (nodes == null)
            {
                return null;
            }

            foreach (var o in nodes)
            {
                if (o is RDNode n && TryGetTechIdFromRdNode(n) == techId)
                {
                    return n;
                }
            }

            return null;
        }

        private static IEnumerable EnumerateRdNodesFromValue(object v)
        {
            if (v == null)
            {
                yield break;
            }

            if (v is RDNode single)
            {
                yield return single;
                yield break;
            }

            if (v is RDNode[] arr)
            {
                foreach (var n in arr)
                {
                    if (n != null)
                    {
                        yield return n;
                    }
                }

                yield break;
            }

            if (v is IList list)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i] is RDNode n)
                    {
                        yield return n;
                    }
                }

                yield break;
            }

            if (v is IEnumerable en && !(v is string))
            {
                foreach (var o in en)
                {
                    if (o is RDNode n)
                    {
                        yield return n;
                    }
                }
            }
        }

        private static bool TryInvokeRdNodeUiActivate(RDNode node)
        {
            if (node?.gameObject == null)
            {
                return false;
            }

            try
            {
                var buttons = node.gameObject.GetComponentsInChildren<Button>(true);
                foreach (var b in buttons)
                {
                    if (b != null && b.interactable && b.onClick != null)
                    {
                        b.onClick.Invoke();
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static void TryInvokeRdControllerRdNodeAndBool(RDController controller, RDNode node, string logTag)
        {
            if (controller == null || node == null)
            {
                return;
            }

            try
            {
                var ct = controller.GetType();
                var nodeType = typeof(RDNode);
                foreach (var method in ct.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    var ps = method.GetParameters();
                    if (ps.Length != 2 || !ps[0].ParameterType.IsAssignableFrom(nodeType) || ps[1].ParameterType != typeof(bool))
                    {
                        continue;
                    }

                    if (!RdControllerMethodNameLooksLikeSelection(method.Name))
                    {
                        continue;
                    }

                    try
                    {
                        method.Invoke(controller, new object[] { node, true });
                        RnDUiDiag($"{logTag}: RDController.{method.Name}(RDNode,bool true) invoked");
                        return;
                    }
                    catch (Exception e)
                    {
                        RnDUiDiag($"{logTag}: RDController.{method.Name}(RDNode,bool) threw {e.GetType().Name}: {e.Message}");
                    }
                }

                RnDUiDiag($"{logTag}: no RDController(RDNode,bool) selection invoke succeeded");
            }
            catch (Exception e)
            {
                RnDUiDiag($"{logTag}: RDController(RDNode,bool) sweep outer {e.GetType().Name}: {e.Message}");
            }
        }

        private static RDNode TryFindRdNodeForTechInScene(string techId)
        {
            if (string.IsNullOrEmpty(techId))
            {
                return null;
            }

            try
            {
                foreach (var node in UnityEngine.Object.FindObjectsOfType<RDNode>())
                {
                    if (node == null || node.gameObject == null || !node.gameObject.scene.IsValid())
                    {
                        continue;
                    }

                    if (TryGetTechIdFromRdNode(node) == techId)
                    {
                        return node;
                    }
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
