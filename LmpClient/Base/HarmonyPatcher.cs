using System.Reflection;
using LmpClient.Harmony;

namespace LmpClient.Base
{
    public static class HarmonyPatcher
    {
        public static HarmonyLib.Harmony HarmonyInstance = new HarmonyLib.Harmony("KSPMultiplayer");
        private static bool _patched;

        public static void Awake()
        {
            if (!_patched)
            {
                HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
                _patched = true;
            }

            KerbalKonstructsLaunchPadHarmony.TryRegister(HarmonyInstance);
        }
    }
}
