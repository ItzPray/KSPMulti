using KSP.UI.Screens;
using LmpClient.Base;
using System;
using UnityEngine;

namespace LmpClient.Systems.Toolbar
{
    public class ToolbarEvents : SubSystem<ToolbarSystem>
    {
        private static bool _launcherButtonRegistered;

        /// <summary>
        /// Call once after systems are constructed. The stock <see cref="GameEvents.onGUIApplicationLauncherReady"/>
        /// may have already fired before we subscribed; this registers the button if the launcher is already up.
        /// </summary>
        public static void TryRegisterImmediatelyIfLauncherReady()
        {
            if (_launcherButtonRegistered)
            {
                return;
            }

            if (ApplicationLauncher.Instance == null)
            {
                return;
            }

            ToolbarSystem.Singleton.ToolbarEvents.EnableToolBar();
        }

        public void EnableToolBar()
        {
            if (_launcherButtonRegistered)
            {
                GameEvents.onGUIApplicationLauncherReady.Remove(EnableToolBar);
                return;
            }

            if (ApplicationLauncher.Instance == null)
            {
                return;
            }

            GameEvents.onGUIApplicationLauncherReady.Remove(EnableToolBar);

            Texture buttonTexture = null;
            if (GameDatabase.Instance != null)
            {
                buttonTexture = GameDatabase.Instance.GetTexture("KSPMultiplayer/Button/LMPButton", false);
            }

            if (buttonTexture == null)
            {
                LmpClient.LunaLog.LogError("[KSPMP] Toolbar icon not found at GameData/KSPMultiplayer/Button/LMPButton - using a fallback. Copy Button/LMPButton.png from the KSP Multiplayer release into your GameData/KSPMultiplayer folder.");
                buttonTexture = CreateFallbackLauncherTexture();
            }

            try
            {
                ApplicationLauncher.Instance.AddModApplication(System.HandleButtonClick, System.HandleButtonClick,
                    () => { }, () => { }, () => { }, () => { }, ApplicationLauncher.AppScenes.ALWAYS, buttonTexture);
                _launcherButtonRegistered = true;
            }
            catch (Exception ex)
            {
                LmpClient.LunaLog.LogError($"[KSPMP] Failed to add ApplicationLauncher button: {ex.Message}");
                GameEvents.onGUIApplicationLauncherReady.Add(EnableToolBar);
            }
        }

        private static Texture2D CreateFallbackLauncherTexture()
        {
            const int s = 38;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var c = new Color(0.18f, 0.42f, 0.78f, 1f);
            var px = new Color[s * s];
            for (var i = 0; i < px.Length; i++)
            {
                px[i] = c;
            }

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
