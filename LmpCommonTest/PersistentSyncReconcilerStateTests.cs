using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace LmpCommonTest
{
    [TestClass]
    public class PersistentSyncReconcilerStateTests
    {
        private static PersistentSyncBufferedSnapshot Snapshot(PersistentSyncDomainId domainId, long revision, byte marker = 0)
        {
            return new PersistentSyncBufferedSnapshot
            {
                DomainId = domainId,
                Revision = revision,
                AuthorityPolicy = PersistentAuthorityPolicy.AnyClientIntent,
                NumBytes = 1,
                Payload = new[] { marker }
            };
        }

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
        public void TestShouldNotIgnoreFirstSnapshotAtRevisionZero()
        {
            var state = new PersistentSyncReconcilerState();
            state.Reset(new[] { PersistentSyncDomainId.Funds });

            Assert.IsFalse(state.ShouldIgnoreSnapshot(PersistentSyncDomainId.Funds, 0));
            state.MarkApplied(PersistentSyncDomainId.Funds, 0);
            Assert.IsTrue(state.ShouldIgnoreSnapshot(PersistentSyncDomainId.Funds, 0));
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

        /// <summary>
        /// Models snapshot handling where the domain apply outcome is Deferred: nothing is committed yet,
        /// so the framework must not treat the domain as having completed its initial snapshot.
        /// </summary>
        [TestMethod]
        public void DeferredSnapshotDoesNotMarkDomainInitiallySynced()
        {
            var state = new PersistentSyncReconcilerState();
            state.Reset(new[] { PersistentSyncDomainId.Funds, PersistentSyncDomainId.Science });

            state.StoreDeferred(Snapshot(PersistentSyncDomainId.Funds, 1, 11));

            Assert.IsFalse(state.HasInitialSnapshot(PersistentSyncDomainId.Funds));
            Assert.IsFalse(state.HasInitialSnapshot(PersistentSyncDomainId.Science));
            Assert.AreEqual(0L, state.GetLastAppliedRevision(PersistentSyncDomainId.Funds));
            Assert.IsFalse(state.AreAllInitialSnapshotsApplied());
            Assert.IsTrue(state.TryGetDeferred(PersistentSyncDomainId.Funds, out var pending));
            Assert.AreEqual(1L, pending.Revision);
        }

        /// <summary>
        /// Models a Rejected apply outcome: no revision is applied and no deferred buffer is kept for that attempt.
        /// Initial-sync bookkeeping must stay false until a later attempt succeeds.
        /// </summary>
        [TestMethod]
        public void RejectedApplyOutcomeLeavesNoInitialSnapshotAndNoDeferredBuffer_Model()
        {
            var state = new PersistentSyncReconcilerState();
            state.Reset(new[] { PersistentSyncDomainId.Funds });

            Assert.IsFalse(state.HasInitialSnapshot(PersistentSyncDomainId.Funds));
            Assert.IsFalse(state.TryGetDeferred(PersistentSyncDomainId.Funds, out _));

            // Reconciler-side resync is not represented in PersistentSyncReconcilerState; state stays unsynced.
            Assert.IsFalse(state.AreAllInitialSnapshotsApplied());
        }

        /// <summary>
        /// When revision 5 is already deferred, an older snapshot (rev 4) must be ignored so it cannot
        /// clobber or race the pending newer payload (reconciler should not process it).
        /// </summary>
        [TestMethod]
        public void StaleSnapshotMustBeIgnoredWhenNewerRevisionIsAlreadyDeferred()
        {
            var state = new PersistentSyncReconcilerState();
            state.Reset(new[] { PersistentSyncDomainId.Funds });
            state.StoreDeferred(Snapshot(PersistentSyncDomainId.Funds, 5, 55));

            Assert.IsTrue(
                state.ShouldIgnoreSnapshot(PersistentSyncDomainId.Funds, 4),
                "Revision strictly older than the deferred revision must be ignored.");
        }

        /// <summary>
        /// Marking a lower revision applied while a strictly newer revision is still deferred must not
        /// silently discard the deferred work; that deferred snapshot still represents unconsumed server state.
        /// </summary>
        [TestMethod]
        public void MarkAppliedForOlderRevisionMustNotDiscardNewerDeferredSnapshot()
        {
            var state = new PersistentSyncReconcilerState();
            state.Reset(new[] { PersistentSyncDomainId.Science });
            state.StoreDeferred(Snapshot(PersistentSyncDomainId.Science, 9, 99));

            state.MarkApplied(PersistentSyncDomainId.Science, 8);

            Assert.IsTrue(
                state.TryGetDeferred(PersistentSyncDomainId.Science, out var stillPending) && stillPending.Revision == 9,
                "A newer deferred snapshot must survive while its revision is still above the last applied revision.");
        }

        [TestMethod]
        public void MarkAppliedOlderRevisionAfterNewerDoesNotLowerStoredAppliedRevision()
        {
            var state = new PersistentSyncReconcilerState();
            state.Reset(new[] { PersistentSyncDomainId.Funds });
            state.MarkApplied(PersistentSyncDomainId.Funds, 5);
            state.MarkApplied(PersistentSyncDomainId.Funds, 3);

            Assert.AreEqual(5L, state.GetLastAppliedRevision(PersistentSyncDomainId.Funds));
        }

        [TestMethod]
        public void InitialSyncNotCompleteUntilEveryRequiredDomainHasMarkedAppliedSnapshot()
        {
            var state = new PersistentSyncReconcilerState();
            state.Reset(new[]
            {
                PersistentSyncDomainId.Funds,
                PersistentSyncDomainId.Science,
                PersistentSyncDomainId.Reputation
            });

            state.MarkApplied(PersistentSyncDomainId.Funds, 1);
            state.StoreDeferred(Snapshot(PersistentSyncDomainId.Science, 1, 1));
            Assert.IsFalse(state.AreAllInitialSnapshotsApplied());

            state.MarkApplied(PersistentSyncDomainId.Science, 1);
            Assert.IsFalse(state.AreAllInitialSnapshotsApplied());

            state.MarkApplied(PersistentSyncDomainId.Reputation, 1);
            Assert.IsTrue(state.AreAllInitialSnapshotsApplied());
        }

        [TestMethod]
        public void AllDomainsOnlyDeferredNeverReportsInitialSyncComplete()
        {
            var state = new PersistentSyncReconcilerState();
            state.Reset(new[]
            {
                PersistentSyncDomainId.Funds,
                PersistentSyncDomainId.Science
            });

            state.StoreDeferred(Snapshot(PersistentSyncDomainId.Funds, 1));
            state.StoreDeferred(Snapshot(PersistentSyncDomainId.Science, 1));

            Assert.IsFalse(state.AreAllInitialSnapshotsApplied());
            Assert.IsFalse(state.HasInitialSnapshot(PersistentSyncDomainId.Funds));
            Assert.IsFalse(state.HasInitialSnapshot(PersistentSyncDomainId.Science));
        }
    }
}
