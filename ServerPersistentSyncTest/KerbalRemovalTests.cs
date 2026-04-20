using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.Context;
using Server.System;
using System.IO;

namespace ServerPersistentSyncTest
{
    [TestClass]
    public class KerbalRemovalTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            ServerContext.Clients.Clear();
        }

        [TestMethod]
        public void TryRemoveKerbalRecord_DuplicateMissingKerbal_ReturnsFalseAfterFirstDelete()
        {
            FileHandler.FolderCreate(KerbalSystem.KerbalsPath);

            const string kerbalName = "Spam Tourist Kerman";
            var kerbalPath = Path.Combine(KerbalSystem.KerbalsPath, kerbalName + ".txt");
            FileHandler.WriteToFile(kerbalPath, "tourist");

            Assert.IsTrue(KerbalSystem.TryRemoveKerbalRecord(kerbalName));
            Assert.IsFalse(File.Exists(kerbalPath));
            Assert.IsFalse(KerbalSystem.TryRemoveKerbalRecord(kerbalName));
        }
    }
}
