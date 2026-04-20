using System;
using System.Collections.Generic;
using LmpCommon.PersistentSync;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LmpCommonTest
{
    /// <summary>
    /// Behavioral regression suite for <see cref="ScenarioSyncApplyScope"/>. The scope is tiny but security-
    /// critical: if a peer is left with <c>IgnoreEvents == true</c> after apply, subsequent real player actions
    /// will be silently dropped. These tests pin the contract so the scope can be used in client domain apply
    /// paths (which themselves aren't unit-testable due to KSP/Unity dependencies) with confidence.
    /// </summary>
    [TestClass]
    public class ScenarioSyncApplyScopeTests
    {
        [TestMethod]
        public void Begin_WithNullPeersList_IsNoOp_DisposeDoesNotThrow()
        {
            using (ScenarioSyncApplyScope.Begin(null))
            {
                // nothing to do; we only care that Begin/Dispose with a null peers list is safe.
            }
        }

        [TestMethod]
        public void Begin_WithEmptyPeersList_IsNoOp_DisposeDoesNotThrow()
        {
            using (ScenarioSyncApplyScope.Begin(new List<IShareProgressEventSuppressor>()))
            {
            }
        }

        [TestMethod]
        public void Begin_InvokesStartIgnoringEventsOncePerPeer_InOrder()
        {
            var peers = new[] { new RecordingPeer("a"), new RecordingPeer("b"), new RecordingPeer("c") };

            using (ScenarioSyncApplyScope.Begin(peers))
            {
                Assert.AreEqual(1, peers[0].StartCalls);
                Assert.AreEqual(1, peers[1].StartCalls);
                Assert.AreEqual(1, peers[2].StartCalls);
                Assert.AreEqual(0, peers[0].StopCalls);
                Assert.AreEqual(0, peers[1].StopCalls);
                Assert.AreEqual(0, peers[2].StopCalls);
            }
        }

        [TestMethod]
        public void Dispose_StopsIgnoringEventsOnEveryPeer_WithRestoreOldValueFalse()
        {
            var peers = new[] { new RecordingPeer("a"), new RecordingPeer("b") };

            using (ScenarioSyncApplyScope.Begin(peers))
            {
            }

            Assert.AreEqual(1, peers[0].StopCalls);
            Assert.AreEqual(1, peers[1].StopCalls);
            Assert.IsFalse(peers[0].LastStopRestoreOldValue, "StopIgnoringEvents must be called with restoreOldValue: false");
            Assert.IsFalse(peers[1].LastStopRestoreOldValue, "StopIgnoringEvents must be called with restoreOldValue: false");
        }

        [TestMethod]
        public void Dispose_RestoresPeersInReverseOrderOfBegin()
        {
            var callLog = new List<string>();
            var peers = new[]
            {
                new RecordingPeer("a", callLog),
                new RecordingPeer("b", callLog),
                new RecordingPeer("c", callLog)
            };

            using (ScenarioSyncApplyScope.Begin(peers))
            {
            }

            CollectionAssert.AreEqual(
                new[] { "a:start", "b:start", "c:start", "c:stop", "b:stop", "a:stop" },
                callLog);
        }

        [TestMethod]
        public void Dispose_ContainsPeerExceptionsSoLaterPeersStillRestore()
        {
            var peers = new IShareProgressEventSuppressor[]
            {
                new RecordingPeer("first"),
                new ThrowingOnStopPeer(),
                new RecordingPeer("last")
            };

            using (ScenarioSyncApplyScope.Begin(peers))
            {
            }

            var first = (RecordingPeer)peers[0];
            var last = (RecordingPeer)peers[2];
            Assert.AreEqual(1, first.StopCalls, "First peer (reached last due to reverse-order dispose) must still be restored when a mid-iteration peer throws.");
            Assert.AreEqual(1, last.StopCalls, "Last peer (reached first due to reverse-order dispose) must still be restored even though the next peer throws.");
        }

        [TestMethod]
        public void Begin_IgnoresNullEntriesInsidePeersList()
        {
            var peer = new RecordingPeer("only");
            var peers = new IShareProgressEventSuppressor[] { null, peer, null };

            using (ScenarioSyncApplyScope.Begin(peers))
            {
                Assert.AreEqual(1, peer.StartCalls);
            }

            Assert.AreEqual(1, peer.StopCalls);
        }

        [TestMethod]
        public void Scope_IsReentrantAcrossNestedUsings_EachNestedScopeOnlyAffectsItsPeers()
        {
            var outerPeer = new RecordingPeer("outer");
            var innerPeer = new RecordingPeer("inner");

            using (ScenarioSyncApplyScope.Begin(new[] { outerPeer }))
            {
                using (ScenarioSyncApplyScope.Begin(new[] { innerPeer }))
                {
                    Assert.AreEqual(1, outerPeer.StartCalls);
                    Assert.AreEqual(1, innerPeer.StartCalls);
                }

                Assert.AreEqual(1, innerPeer.StopCalls, "Inner peer must be stopped when inner scope ends.");
                Assert.AreEqual(0, outerPeer.StopCalls, "Outer peer must still be ignoring while outer scope is alive.");
            }

            Assert.AreEqual(1, outerPeer.StopCalls, "Outer peer must be stopped when outer scope ends.");
        }

        private sealed class RecordingPeer : IShareProgressEventSuppressor
        {
            private readonly string _name;
            private readonly List<string> _callLog;

            public RecordingPeer(string name, List<string> callLog = null)
            {
                _name = name;
                _callLog = callLog;
            }

            public int StartCalls { get; private set; }
            public int StopCalls { get; private set; }
            public bool LastStopRestoreOldValue { get; private set; }

            public void StartIgnoringEvents()
            {
                StartCalls++;
                _callLog?.Add(_name + ":start");
            }

            public void StopIgnoringEvents(bool restoreOldValue = false)
            {
                StopCalls++;
                LastStopRestoreOldValue = restoreOldValue;
                _callLog?.Add(_name + ":stop");
            }
        }

        private sealed class ThrowingOnStopPeer : IShareProgressEventSuppressor
        {
            public void StartIgnoringEvents()
            {
            }

            public void StopIgnoringEvents(bool restoreOldValue = false)
            {
                throw new InvalidOperationException("simulated peer failure");
            }
        }
    }
}
