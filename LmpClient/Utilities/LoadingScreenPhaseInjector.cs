using System.Collections;
using System.IO;
using UnityEngine;

namespace LmpClient.Utilities
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class LoadingScreenPhaseInjector : MonoBehaviour
    {
        private const string LoadingScreenRelativePath = "GameData/LunaMultiplayer/LoadingScreens/KSPMultiLoadingScreen.png";
        private const string TextureName = "KSPMultiLoadingScreen";
        private const string LoadingTip = "KSPMulti";
        private const float DisplayTime = 8f;
        private const float FadeTime = 0.5f;

        private static bool LoadingScreenAdded;

        public void Start()
        {
            StartCoroutine(AddLoadingScreenPhase());
        }

        private static IEnumerator AddLoadingScreenPhase()
        {
            if (LoadingScreenAdded)
                yield break;

            var imagePath = Path.Combine(KSPUtil.ApplicationRootPath, LoadingScreenRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(imagePath))
            {
                Debug.Log($"[LMP]: Loading screen image not found at {imagePath}");
                yield break;
            }

            LoadingScreen loadingScreen = null;
            for (var i = 0; i < 120; i++)
            {
                loadingScreen = LoadingScreen.Instance;
                if (loadingScreen?.Screens != null)
                    break;

                yield return null;
            }

            if (loadingScreen?.Screens == null)
            {
                Debug.Log("[LMP]: Loading screen was not ready; KSPMulti loading phase was not added");
                yield break;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = TextureName };
            if (!texture.LoadImage(File.ReadAllBytes(imagePath)))
            {
                Destroy(texture);
                Debug.Log($"[LMP]: Failed to load loading screen image at {imagePath}");
                yield break;
            }

            texture.wrapMode = TextureWrapMode.Clamp;

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

            loadingScreen.Screens.Insert(0, state);
            LoadingScreenAdded = true;
            Debug.Log($"[LMP]: Added KSPMulti loading screen phase from {imagePath}");
        }
    }
}
