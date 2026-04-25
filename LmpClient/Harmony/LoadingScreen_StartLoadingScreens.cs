using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// Injects a KSPMulti <see cref="LoadingScreenState"/> for our cached texture. De-dupe is by object
    /// reference so a separate Sigma-supplied copy of the same file does not block injection.
    /// </summary>
    [HarmonyPatch(typeof(LoadingScreen))]
    [HarmonyPatch("StartLoadingScreens")]
    public class LoadingScreen_StartLoadingScreens
    {
        internal const string LoadingScreenRelativePath = "GameData/LunaMultiplayer/LoadingScreens/KSPMultiLoadingScreen.png";
        internal const string TextureName = "KSPMultiLoadingScreen";
        internal const string LoadingTip = "KSPMulti";
        private const int PreferredScreenIndex = 1;
        private const float DisplayTime = 8f;
        private const float FadeTime = 0.5f;

        private static Texture2D _lmpTextureCache;
        private static string _lmpImagePath;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void PostfixStartLoadingScreens(LoadingScreen __instance)
        {
            TryEnsureLmpScreen(__instance);
        }

        internal static void TryEnsureLmpScreen(LoadingScreen loadingScreen)
        {
            if (loadingScreen?.Screens == null)
                return;

            try
            {
                var lmp = LoadLmpLoadingTexture();
                if (lmp == null)
                    return;

                if (ListContainsOurTexture(loadingScreen.Screens, lmp))
                    return;

                AddLoadingScreen(loadingScreen, lmp, _lmpImagePath);
            }
            catch (Exception ex)
            {
                Debug.Log($"[LMP]: Failed to add KSPMulti loading screen: {ex}");
            }
        }

        private static bool ListContainsOurTexture(List<LoadingScreen.LoadingScreenState> screens, Texture2D lmp)
        {
            foreach (var s in screens)
            {
                if (s == null) continue;
                if (s.activeScreen == lmp) return true;
                if (s.screens == null) continue;
                foreach (var o in s.screens)
                {
                    if (o == lmp) return true;
                }
            }
            return false;
        }

        internal static Texture2D LoadLmpLoadingTexture()
        {
            if (_lmpTextureCache != null)
                return _lmpTextureCache;

            var imagePath = Path.Combine(KSPUtil.ApplicationRootPath, LoadingScreenRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(imagePath))
            {
                Debug.Log($"[LMP]: KSPMulti loading screen image not found at {imagePath}");
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = TextureName };
            if (!texture.LoadImage(File.ReadAllBytes(imagePath)))
            {
                UnityEngine.Object.Destroy(texture);
                Debug.Log($"[LMP]: Failed to load KSPMulti loading screen image at {imagePath}");
                return null;
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            _lmpTextureCache = texture;
            _lmpImagePath = imagePath;
            return texture;
        }

        private static void AddLoadingScreen(LoadingScreen loadingScreen, Texture2D texture, string imagePath)
        {
            var state = new LoadingScreen.LoadingScreenState
            {
                screens = new UnityEngine.Object[] { texture },
                activeScreen = texture,
                tips = new[] { LoadingTip },
                displayTime = DisplayTime,
                tipTime = DisplayTime,
                fadeInTime = FadeTime,
                fadeOutTime = FadeTime
            };

            var insertIndex = Math.Min(PreferredScreenIndex, loadingScreen.Screens.Count);
            loadingScreen.Screens.Insert(insertIndex, state);

            Debug.Log($"[LMP]: Injected KSPMulti loading screen at cycle index {insertIndex} from {imagePath}");
        }
    }

    /// <summary>
    /// Fills the list after the game or Sigma finishes async or late population (postfixes alone miss it).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class LmpLoadingScreenListRetry : MonoBehaviour
    {
        private const float RetryForSeconds = 3f;
        private const float Interval = 0.1f;
        private float _stopAt;
        private float _next;

        public void Start()
        {
            _stopAt = Time.realtimeSinceStartup + RetryForSeconds;
            _next = 0f;
        }

        public void Update()
        {
            if (Time.realtimeSinceStartup >= _stopAt)
            {
                UnityEngine.Object.Destroy(this);
                return;
            }

            if (Time.realtimeSinceStartup < _next)
                return;

            _next = Time.realtimeSinceStartup + Interval;
            var loadingScreen = UnityEngine.Object.FindObjectOfType<LoadingScreen>();
            if (loadingScreen != null)
                LoadingScreen_StartLoadingScreens.TryEnsureLmpScreen(loadingScreen);
        }
    }
}
