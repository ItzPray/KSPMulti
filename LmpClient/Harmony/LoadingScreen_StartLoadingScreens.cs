using HarmonyLib;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// Forces the KSPMulti loading art into KSP's loading-screen cycle before the cycle starts.
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

        private static bool LoadingScreenAdded;

        [HarmonyPrefix]
        private static void PrefixStartLoadingScreens(LoadingScreen __instance)
        {
            if (LoadingScreenAdded || __instance?.Screens == null)
                return;

            try
            {
                AddLoadingScreen(__instance);
            }
            catch (Exception ex)
            {
                Debug.Log($"[LMP]: Failed to force KSPMulti loading screen: {ex}");
            }
        }

        internal static Texture2D LoadLmpLoadingTexture()
        {
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
            return texture;
        }

        private static void AddLoadingScreen(LoadingScreen loadingScreen)
        {
            var imagePath = Path.Combine(KSPUtil.ApplicationRootPath, LoadingScreenRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var texture = LoadLmpLoadingTexture();
            if (texture == null)
                return;

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
            LoadingScreenAdded = true;

            Debug.Log($"[LMP]: Forced KSPMulti loading screen at cycle index {insertIndex} from {imagePath}");
        }
    }

    /// <summary>
    /// LoadingScreen.StartLoadingScreens often runs before LMP's Harmony patch is installed.
    /// Keep forcing the visible RawImage during the live loading phase so loading-screen mods
    /// that rebuild the screen list cannot hide the LMP screen.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KspMultiLoadingScreenForcer : MonoBehaviour
    {
        private const float ForceDurationSeconds = 60f;

        private static readonly FieldInfo ScreenImageField =
            AccessTools.Field(typeof(LoadingScreen), "screenImage");

        private Texture2D _texture;
        private float _forceUntil;
        private bool _loggedSuccess;
        private bool _loggedMissingLoadingScreen;
        private bool _loggedMissingImageField;

        public void Awake()
        {
            _texture = LoadingScreen_StartLoadingScreens.LoadLmpLoadingTexture();
            _forceUntil = Time.realtimeSinceStartup + ForceDurationSeconds;

            if (_texture == null)
            {
                Debug.Log("[LMP]: KSPMulti loading screen forcer stopped because the texture did not load");
                Destroy(this);
                return;
            }

            Debug.Log($"[LMP]: KSPMulti loading screen forcer started with {_texture.width}x{_texture.height} texture");
        }

        public void LateUpdate()
        {
            if (_texture == null)
                return;

            if (Time.realtimeSinceStartup > _forceUntil)
            {
                Debug.Log("[LMP]: KSPMulti loading screen forcer stopped after timeout");
                Destroy(this);
                return;
            }

            var loadingScreen = FindObjectOfType<LoadingScreen>();
            if (loadingScreen == null)
            {
                if (!_loggedMissingLoadingScreen)
                {
                    _loggedMissingLoadingScreen = true;
                    Debug.Log("[LMP]: KSPMulti loading screen forcer found no active LoadingScreen yet");
                }

                return;
            }

            var screenImage = ScreenImageField?.GetValue(loadingScreen) as RawImage;
            if (screenImage == null)
            {
                if (!_loggedMissingImageField)
                {
                    _loggedMissingImageField = true;
                    Debug.Log("[LMP]: KSPMulti loading screen forcer could not access LoadingScreen.screenImage");
                }

                return;
            }

            screenImage.texture = _texture;
            screenImage.enabled = true;
            screenImage.color = Color.white;
            screenImage.gameObject.SetActive(true);

            if (!_loggedSuccess)
            {
                _loggedSuccess = true;
                Debug.Log($"[LMP]: KSPMulti loading screen forcer is displaying {LoadingScreen_StartLoadingScreens.TextureName}");
            }
        }
    }
}
