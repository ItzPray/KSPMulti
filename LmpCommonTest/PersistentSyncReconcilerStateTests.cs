using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace LmpCommonTest
{
    [TestClass]
    public class PersistentSyncReconcilerStateTests
    {
        [TestMethod]
        public void TestResetTracksRequiredDomains()
        {
            var state = new PersistentSyncReconcilerState();
            state.Reset(new[] { PersistentSyncDomainId.Funds, PersistentSyncDomainId.Science });

            CollectionAssert.AreEquivalent(
                new[] { PersistentSyncDomainId.Funds, PersistentSyncDomainId.Science },
                state.RequiredDomains.ToArray());
            Assert.IsFalse(state.AreAllInitialSnapshotsApplied());
        }

        [TestMethod]
        public void TestShouldIgnoreSnapshotWhenRevisionIsStale()
        {
            var state = new PersistentSyncReconcilerState();
            state.Reset(new[] { PersistentSyncDomainId.Funds });
            state.MarkApplied(PersistentSyncDomainId.Funds, 3);

            Assert.IsTrue(state.ShouldIgnoreSnapshot(PersistentSyncDomainId.Funds, 3));
            Assert.IsTrue(state.ShouldIgnoreSnapshot(PersistentSyncDomainId.Funds, 2));
            Assert.IsFalse(state.ShouldIgnoreSnapshot(PersistentSyncDomainId.Funds, 4));
        }

        [TestMethod]
        public void TestStoreDeferredReplacesOlderSnapshotForSameDomain()
        {
            var state = new PersistentSyncReconcilerState();
            state.Reset(new[] { PersistentSyncDomainId.Science });
            state.StoreDeferred(new PersistentSyncBufferedSnapshot
            {
                DomainId = PersistentSyncDomainId.Science,
                Revision = 1,
                NumBytes = 4,
                Payload = new byte[] { 1, 2, 3, 4 }
            });
            state.StoreDeferred(new PersistentSyncBufferedSnapshot
            {
                DomainId = PersistentSyncDomainId.Science,
                Revision = 2,
                NumBytes = 4,
                Payload = new byte[] { 5, 6, 7, 8 }
            });

            Assert.IsTrue(state.TryGetDeferred(PersistentSyncDomainId.Science, out var snapshot));
            Assert.AreEqual(2L, snapshot.Revision);
            CollectionAssert.AreEqual(new byte[] { 5, 6, 7, 8 }, snapshot.Payload);
        }

        [TestMethod]
        public void TestAllInitialSnapshotsAppliedAfterMarkApplied()
        {
            var state = new PersistentSyncReconcilerState();
            state.Reset(new[] { PersistentSyncDomainId.Funds, PersistentSyncDomainId.Reputation });

            state.MarkApplied(PersistentSyncDomainId.Funds, 1);
            Assert.IsFalse(state.AreAllInitialSnapshotsApplied());

            state.MarkApplied(PersistentSyncDomainId.Reputation, 2);
            Assert.IsTrue(state.AreAllInitialSnapshotsApplied());
        }
    }
}
