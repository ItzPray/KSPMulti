using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.Utilities;
using System.IO;

namespace ServerTest
{
    [TestClass]
    public class ServerSelfUpdaterTests
    {
        [TestMethod]
        public void ShouldSkipMerging_Preserves_Universe()
        {
            var p = "Universe" + Path.AltDirectorySeparatorChar + "Scenarios" + Path.AltDirectorySeparatorChar + "X.txt";
            Assert.IsTrue(ServerSelfUpdater.ShouldSkipMergingPath(p));
        }

        [TestMethod]
        public void ShouldSkipMerging_Preserves_LMPPlayerBans()
        {
            Assert.IsTrue(ServerSelfUpdater.ShouldSkipMergingPath("LMPPlayerBans.txt"));
        }

        [TestMethod]
        public void ShouldNotSkip_Dlls()
        {
            Assert.IsFalse(ServerSelfUpdater.ShouldSkipMergingPath("Server.dll"));
        }
    }
}
