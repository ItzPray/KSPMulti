using KSP.UI.Screens;
using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.ShareProgress;
using LmpClient.Systems.SettingsSys;
using LmpClient.Utilities;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using System;

namespace LmpClient.Systems.ShareTechnology
{
    public class ShareTechnologySystem : ShareProgressBaseSystem<ShareTechnologySystem, ShareTechnologyMessageSender, ShareTechnologyMessageHandler>
    {
        public override string SystemName { get; } = nameof(ShareTechnologySystem);

        private const string RdControllerUpdatePanelRetryRoutine = nameof(ShareTechnologySystem) + ".RdControllerUpdatePanelRetry";

        private const string PsRnDCoalescedRefreshRoutine = nameof(ShareTechnologySystem) + ".PsRnDCoalescedRefresh";

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

        private ShareTechnologyEvents ShareTechnologyEvents { get; } = new ShareTechnologyEvents();

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
            }

            TryRefreshResearchAndDevelopmentControllerPartUi(source);
            RefreshEditorPartsList(source);
        }

        /// <summary>
        /// Updates the R&amp;D side panel part list and VAB/SPH part browser without reloading the entire tech tree.
        /// Used after part-purchase or experimental-part snapshots so one reconciler pass does not invoke
        /// <see cref="ResearchAndDevelopment.RefreshTechTreeUI"/> twice (Technology domain then PartPurchases domain).
        /// </summary>
        public void RefreshResearchAndDevelopmentPurchasesOnly(string source)
        {
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
                return;
            }

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
                    catch
                    {
                        // Stock RDPartList.SetupParts can throw when no RDNode is selected yet; skip noisy failures.
                    }
                }

                try
                {
                    controller.UpdatePanel();
                }
                catch (NullReferenceException)
                {
                    if (updatePanelAttempt + 1 >= RdControllerUpdatePanelMaxAttempts)
                    {
                        // Stock can keep NRE-ing here while the tree + RnD tech state are already correct (RefreshTechTreeUI /
                        // proto mirror). UpdatePanel is best-effort for the side panel; do not log as a hard failure.
                        LunaLog.Log(
                            $"[PersistentSync] technology UI refresh: skipped RDController.UpdatePanel after {RdControllerUpdatePanelMaxAttempts} null-ref attempts " +
                            $"(side panel is optional; tech-tree + R&D state sync already ran) source={source}");
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
    }
}
