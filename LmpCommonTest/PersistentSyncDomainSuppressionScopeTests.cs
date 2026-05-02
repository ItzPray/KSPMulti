using System;
using System.Collections.Generic;
using LmpCommon.PersistentSync;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LmpCommonTest
{
    /// <summary>
    /// Behavioral parity suite for <see cref="PersistentSyncDomainSuppressionScope"/> vs
    /// <see cref="ScenarioSyncApplyScope"/>.
    /// </summary>
    [TestClass]
    public class PersistentSyncDomainSuppressionScopeTests
    {
        [TestMethod]
        public void Begin_WithNullSuppressorsList_IsNoOp_DisposeDoesNotThrow()
        {
            using (PersistentSyncDomainSuppressionScope.Begin(null, false))
            {
            }
        }

        [TestMethod]
        public void Begin_WithEmptySuppressorsList_IsNoOp_DisposeDoesNotThrow()
        {
            using (PersistentSyncDomainSuppressionScope.Begin(new List<IPersistentSyncEventSuppressor>(), false))
            {
            }
        }

        [TestMethod]
        public void Begin_InvokesStartOncePerSuppressor_InOrder()
        {
            var peers = new[] { new RecordingSuppressor("a"), new RecordingSuppressor("b"), new RecordingSuppressor("c") };

            using (PersistentSyncDomainSuppressionScope.Begin(peers, false))
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
        public void Dispose_StopsSuppressors_WithRestoreFalse()
        {
            AssertRestorePassThrough(false);
        }

        [TestMethod]
        public void Dispose_StopsSuppressors_WithRestoreTrue()
        {
            AssertRestorePassThrough(true);
        }

        private static void AssertRestorePassThrough(bool restore)
        {
            var peers = new[] { new RecordingSuppressor("a"), new RecordingSuppressor("b") };

            using (PersistentSyncDomainSuppressionScope.Begin(peers, restore))
            {
            }

            Assert.AreEqual(1, peers[0].StopCalls);
            Assert.AreEqual(1, peers[1].StopCalls);
            Assert.AreEqual(restore, peers[0].LastStopRestoreOldValue);
            Assert.AreEqual(restore, peers[1].LastStopRestoreOldValue);
        }

        [TestMethod]
        public void Dispose_RestoresSuppressorsInReverseOrderOfBegin()
        {
            var callLog = new List<string>();
            var peers = new[]
            {
                new RecordingSuppressor("a", callLog),
                new RecordingSuppressor("b", callLog),
                new RecordingSuppressor("c", callLog)
            };

            using (PersistentSyncDomainSuppressionScope.Begin(peers, false))
            {
            }

            CollectionAssert.AreEqual(
                new[] { "a:start", "b:start", "c:start", "c:stop", "b:stop", "a:stop" },
                callLog);
        }

        [TestMethod]
        public void Dispose_ContainsSuppressorExceptionsSoLaterSuppressorsStillRestore()
        {
            var suppressors = new IPersistentSyncEventSuppressor[]
            {
                new RecordingSuppressor("first"),
                new ThrowingOnStopSuppressor(),
                new RecordingSuppressor("last")
            };

            using (PersistentSyncDomainSuppressionScope.Begin(suppressors, false))
            {
            }

            var first = (RecordingSuppressor)suppressors[0];
            var last = (RecordingSuppressor)suppressors[2];
            Assert.AreEqual(1, first.StopCalls);
            Assert.AreEqual(1, last.StopCalls);
        }

        [TestMethod]
        public void Begin_IgnoresNullEntriesInsideSuppressorsList()
        {
            var peer = new RecordingSuppressor("only");
            var peers = new IPersistentSyncEventSuppressor[] { null, peer, null };

            using (PersistentSyncDomainSuppressionScope.Begin(peers, false))
            {
                Assert.AreEqual(1, peer.StartCalls);
            }

            Assert.AreEqual(1, peer.StopCalls);
        }

        [TestMethod]
        public void Scope_IsReentrantAcrossNestedUsings_EachNestedScopeOnlyAffectsItsSuppressors()
        {
            var outer = new RecordingSuppressor("outer");
            var inner = new RecordingSuppressor("inner");

            using (PersistentSyncDomainSuppressionScope.Begin(new[] { outer }, false))
            {
                using (PersistentSyncDomainSuppressionScope.Begin(new[] { inner }, false))
                {
                    Assert.AreEqual(1, outer.StartCalls);
                    Assert.AreEqual(1, inner.StartCalls);
                }

                Assert.AreEqual(1, inner.StopCalls);
                Assert.AreEqual(0, outer.StopCalls);
            }

            Assert.AreEqual(1, outer.StopCalls);
        }

        private sealed class RecordingSuppressor : IPersistentSyncEventSuppressor
        {
            private readonly string _name;
            private readonly List<string> _callLog;

            public RecordingSuppressor(string name, List<string> callLog = null)
            {
                _name = name;
                _callLog = callLog;
            }

            public string DomainName => _name;

            public bool IsSuppressingLocalEvents => StartCalls > StopCalls;

            public int StartCalls { get; private set; }
            public int StopCalls { get; private set; }
            public bool LastStopRestoreOldValue { get; private set; }

            public void StartSuppressingLocalEvents()
            {
                StartCalls++;
                _callLog?.Add(_name + ":start");
            }

            public void StopSuppressingLocalEvents(bool restoreOldValue = false)
            {
                StopCalls++;
                LastStopRestoreOldValue = restoreOldValue;
                _callLog?.Add(_name + ":stop");
            }
        }

        private sealed class ThrowingOnStopSuppressor : IPersistentSyncEventSuppressor
        {
            public string DomainName => "throw";

            public bool IsSuppressingLocalEvents => false;

            public void StartSuppressingLocalEvents()
            {
            }

            public void StopSuppressingLocalEvents(bool restoreOldValue = false)
            {
                throw new InvalidOperationException("simulated suppressor failure");
            }
        }
    }
}
