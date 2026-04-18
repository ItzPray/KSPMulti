using KSP.UI.Screens;
using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.ShareProgress;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using System;

namespace LmpClient.Systems.ShareTechnology
{
    public class ShareTechnologySystem : ShareProgressBaseSystem<ShareTechnologySystem, ShareTechnologyMessageSender, ShareTechnologyMessageHandler>
    {
        public override string SystemName { get; } = nameof(ShareTechnologySystem);

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

            //Always try to remove the event, as when we disconnect from a server the server settings will get the default values
            GameEvents.OnTechnologyResearched.Remove(ShareTechnologyEvents.TechnologyResearched);
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

            RefreshResearchAndDevelopmentPanel(source);
            RefreshEditorPartsList(source);
        }

        /// <summary>
        /// Updates the R&amp;D side panel part list and VAB/SPH part browser without reloading the entire tech tree.
        /// Used after part-purchase or experimental-part snapshots so one reconciler pass does not invoke
        /// <see cref="ResearchAndDevelopment.RefreshTechTreeUI"/> twice (Technology domain then PartPurchases domain).
        /// </summary>
        public void RefreshResearchAndDevelopmentPurchasesOnly(string source)
        {
            RefreshResearchAndDevelopmentPanel(source);
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

        private static void RefreshResearchAndDevelopmentPanel(string source)
        {
            if (!RDController.Instance)
            {
                return;
            }

            try
            {
                if (RDController.Instance.partList)
                {
                    RDController.Instance.partList.Refresh();
                }

                RDController.Instance.UpdatePanel();
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
