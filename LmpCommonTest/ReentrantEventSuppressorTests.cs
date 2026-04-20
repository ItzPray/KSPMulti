using System.Collections.Generic;
using LmpCommon.PersistentSync;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LmpCommonTest
{
    /// <summary>
    /// Behavioral regression suite for <see cref="ReentrantEventSuppressor"/>. The primitive backs every
    /// client Share-progress system's <c>IgnoreEvents</c>/<c>Start/StopIgnoringEvents</c> surface; getting
    /// nesting wrong silently swallows real player intents (suppression stuck true) or silently echoes
    /// server-applied state back as fresh client intents (suppression dropped early). Both bug classes
    /// have been observed in production. These tests pin the contract — particularly outermost-only
    /// Save/Restore and the "restore runs while still active" timing — so concrete client systems can
    /// rely on it without per-system retests.
    /// </summary>
    [TestClass]
    public class ReentrantEventSuppressorTests
    {
        [TestMethod]
        public void NewSuppressor_IsInactive_DepthIsZero()
        {
            var suppressor = new ReentrantEventSuppressor();

            Assert.IsFalse(suppressor.IsActive);
            Assert.AreEqual(0, suppressor.Depth);
        }

        [TestMethod]
        public void Start_TogglesActiveAndIncrementsDepth()
        {
            var suppressor = new ReentrantEventSuppressor();
            var saveCount = 0;

            suppressor.Start(() => saveCount++);

            Assert.IsTrue(suppressor.IsActive);
            Assert.AreEqual(1, suppressor.Depth);
            Assert.AreEqual(1, saveCount);
        }

        [TestMethod]
        public void Start_OnlyInvokesSaveStateOnOutermostEntry()
        {
            var suppressor = new ReentrantEventSuppressor();
            var saveCount = 0;

            suppressor.Start(() => saveCount++);
            suppressor.Start(() => saveCount++);
            suppressor.Start(() => saveCount++);

            Assert.AreEqual(3, suppressor.Depth);
            Assert.AreEqual(1, saveCount, "SaveState must run exactly once across nested Start calls.");
        }

        [TestMethod]
        public void Stop_AtDepthOne_RestoresAndDeactivates_WhenRequested()
        {
            var suppressor = new ReentrantEventSuppressor();
            var restoreCount = 0;

            suppressor.Start(null);
            var balanced = suppressor.Stop(() => restoreCount++, restoreOldValue: true);

            Assert.IsTrue(balanced);
            Assert.IsFalse(suppressor.IsActive);
            Assert.AreEqual(0, suppressor.Depth);
            Assert.AreEqual(1, restoreCount);
        }

        [TestMethod]
        public void Stop_AtDepthOne_DoesNotRestore_WhenNotRequested()
        {
            var suppressor = new ReentrantEventSuppressor();
            var restoreCount = 0;

            suppressor.Start(null);
            suppressor.Stop(() => restoreCount++, restoreOldValue: false);

            Assert.AreEqual(0, restoreCount);
        }

        [TestMethod]
        public void Stop_RunsRestoreStateBeforeDeactivating_SoEventsFiredDuringRestoreRemainSuppressed()
        {
            var suppressor = new ReentrantEventSuppressor();
            var observedActiveDuringRestore = false;

            suppressor.Start(null);
            suppressor.Stop(() => observedActiveDuringRestore = suppressor.IsActive, restoreOldValue: true);

            Assert.IsTrue(observedActiveDuringRestore,
                "RestoreState must run while IsActive is still true so events it triggers (e.g. " +
                "Reputation.SetReputation -> OnReputationChanged) are suppressed by handlers reading IgnoreEvents.");
            Assert.IsFalse(suppressor.IsActive);
        }

        [TestMethod]
        public void Stop_OnInnerScope_DoesNotRestore_EvenWhenInnerRequestsRestore()
        {
            var suppressor = new ReentrantEventSuppressor();
            var restoreCalls = new List<int>();

            suppressor.Start(null);
            suppressor.Start(null);

            suppressor.Stop(() => restoreCalls.Add(suppressor.Depth), restoreOldValue: true);

            Assert.AreEqual(0, restoreCalls.Count,
                "Inner Stop must not invoke RestoreState — outer scope is still active and may have " +
                "intentionally mutated state under suppression.");
            Assert.IsTrue(suppressor.IsActive);
            Assert.AreEqual(1, suppressor.Depth);
        }

        [TestMethod]
        public void Stop_OuterRestoreRequest_HonoredEvenWhenInnerScopeAlreadyClosedWithRestore()
        {
            var suppressor = new ReentrantEventSuppressor();
            var restoreCount = 0;

            suppressor.Start(null);
            suppressor.Start(null);
            suppressor.Stop(() => restoreCount++, restoreOldValue: true);
            suppressor.Stop(() => restoreCount++, restoreOldValue: true);

            Assert.AreEqual(1, restoreCount,
                "Only the outermost Stop's RestoreState should ever fire across a nesting sequence.");
        }

        [TestMethod]
        public void Stop_OuterDoesNotRestore_WhenOuterPassesFalse_EvenIfInnerPassedTrue()
        {
            var suppressor = new ReentrantEventSuppressor();
            var restoreCount = 0;

            suppressor.Start(null);
            suppressor.Start(null);
            suppressor.Stop(() => restoreCount++, restoreOldValue: true);
            suppressor.Stop(() => restoreCount++, restoreOldValue: false);

            Assert.AreEqual(0, restoreCount,
                "Outer Stop owns restoreOldValue. Inner restore requests must not survive past the inner Stop.");
        }

        [TestMethod]
        public void Stop_AtZeroDepth_ReturnsFalse_AndDoesNotMutateState()
        {
            var suppressor = new ReentrantEventSuppressor();
            var restoreCount = 0;

            var balanced = suppressor.Stop(() => restoreCount++, restoreOldValue: true);

            Assert.IsFalse(balanced);
            Assert.AreEqual(0, restoreCount);
            Assert.AreEqual(0, suppressor.Depth);
            Assert.IsFalse(suppressor.IsActive);
        }

        [TestMethod]
        public void Reset_ClearsDepth_WithoutInvokingCallbacks()
        {
            var suppressor = new ReentrantEventSuppressor();
            var restoreCount = 0;

            suppressor.Start(null);
            suppressor.Start(null);
            suppressor.Reset();

            Assert.AreEqual(0, suppressor.Depth);
            Assert.IsFalse(suppressor.IsActive);

            // After Reset, a fresh Stop is unbalanced (no Save was paired) and must report it as unbalanced
            // rather than silently invoking RestoreState against a stale baseline.
            var balanced = suppressor.Stop(() => restoreCount++, restoreOldValue: true);
            Assert.IsFalse(balanced);
            Assert.AreEqual(0, restoreCount);
        }

        [TestMethod]
        public void StartStopRoundTrip_RestoresIndependentlyAcrossSequentialScopes()
        {
            var suppressor = new ReentrantEventSuppressor();
            var saveCount = 0;
            var restoreCount = 0;

            for (var i = 0; i < 3; i++)
            {
                suppressor.Start(() => saveCount++);
                Assert.IsTrue(suppressor.IsActive);
                suppressor.Stop(() => restoreCount++, restoreOldValue: true);
                Assert.IsFalse(suppressor.IsActive);
            }

            Assert.AreEqual(3, saveCount);
            Assert.AreEqual(3, restoreCount);
        }
    }
}
