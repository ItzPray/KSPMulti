using System;
using LmpCommon.Message.Types;

namespace LmpCommon.Message.Data.Settings
{
    /// <summary>
    /// Join merges gameplay settings and persistent-sync catalog only when both messages arrived.
    /// Order matches Scenario Sync UX plan: if the reply landed first, apply catalog then reply; if catalog landed first, apply reply then catalog.
    /// </summary>
    public static class SettingsCatalogJoinMerge
    {
        /// <param name="replyArrivedFirst">True when <see cref="SettingsMessageType.Reply"/> was received before the catalog message.</param>
        public static bool RunMerge(bool replyArrivedFirst, Func<bool> applyPersistentSyncCatalog, Func<bool> applySettingsReply)
        {
            if (replyArrivedFirst)
            {
                if (!applyPersistentSyncCatalog()) return false;
                return applySettingsReply();
            }

            if (!applySettingsReply()) return false;
            return applyPersistentSyncCatalog();
        }
    }
}
