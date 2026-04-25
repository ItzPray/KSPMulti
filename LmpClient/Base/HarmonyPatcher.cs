using System.Reflection;
using LmpClient.Harmony;

namespace LmpClient.Base
{
    public static class HarmonyPatcher
    {
        public static HarmonyLib.Harmony HarmonyInstance = new HarmonyLib.Harmony("KSPMultiplayer");

        public static void Awake()
        {
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            KerbalKonstructsLaunchPadHarmony.TryRegister(HarmonyInstance);
        }
    }
}
