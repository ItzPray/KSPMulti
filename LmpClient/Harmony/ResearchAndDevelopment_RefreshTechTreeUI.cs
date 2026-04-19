using HarmonyLib;
using LmpClient.Systems.ShareTechnology;

namespace LmpClient.Harmony
{
    /// <summary>
    /// Stock rebuilds the R&amp;D graph in <see cref="ResearchAndDevelopment.RefreshTechTreeUI"/> and clears selection /
    /// leaves the purchase panel inconsistent with <see cref="RDTech.partsPurchased"/>. LMP also calls that API from
    /// PersistentSync; a postfix runs for every caller so we can re-drive selection immediately after the tree exists.
    /// </summary>
    [HarmonyPatch(typeof(ResearchAndDevelopment), nameof(ResearchAndDevelopment.RefreshTechTreeUI))]
    public class ResearchAndDevelopment_RefreshTechTreeUI
    {
        [HarmonyPostfix]
        private static void PostfixAfterStockTreeRefresh()
        {
            ShareTechnologySystem.NotifyStockRefreshTechTreeUiCompleted();
        }
    }
}
