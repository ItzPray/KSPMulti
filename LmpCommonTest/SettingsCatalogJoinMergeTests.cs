using System.Collections.Generic;
using System.Text;
using LmpCommon.Message.Data.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LmpCommonTest
{
    [TestClass]
    public class SettingsCatalogJoinMergeTests
    {
        [TestMethod]
        public void RunMerge_ReplyArrivedFirst_AppliesCatalogThenReply()
        {
            var log = new List<char>();
            Assert.IsTrue(SettingsCatalogJoinMerge.RunMerge(
                replyArrivedFirst: true,
                applyPersistentSyncCatalog: () => { log.Add('C'); return true; },
                applySettingsReply: () => { log.Add('R'); return true; }));
            CollectionAssert.AreEqual(new[] { 'C', 'R' }, log);
        }

        [TestMethod]
        public void RunMerge_CatalogArrivedFirst_AppliesReplyThenCatalog()
        {
            var log = new List<char>();
            Assert.IsTrue(SettingsCatalogJoinMerge.RunMerge(
                replyArrivedFirst: false,
                applyPersistentSyncCatalog: () => { log.Add('C'); return true; },
                applySettingsReply: () => { log.Add('R'); return true; }));
            CollectionAssert.AreEqual(new[] { 'R', 'C' }, log);
        }

        [TestMethod]
        public void RunMerge_ShortCircuits_OnFirstFailure()
        {
            var log = new StringBuilder();
            Assert.IsFalse(SettingsCatalogJoinMerge.RunMerge(
                replyArrivedFirst: true,
                applyPersistentSyncCatalog: () => { log.Append('C'); return false; },
                applySettingsReply: () => { log.Append('R'); return true; }));
            Assert.AreEqual("C", log.ToString());
        }
    }
}
