using LmpCommon.Enums;
using LmpCommon.Message;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.Client;
using Server.System;
using Server.System.PersistentSync;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ServerPersistentSyncTest
{
    /// <summary>
    /// Template-level regression tests for <see cref="ScenarioSyncDomainStore{TCanonical}"/>. These tests use a
    /// minimal probe subclass rather than a real game domain so failures isolate to base-class behavior (rule 4
    /// of the Scenario Sync Domain Contract: revision bumps, equality short-circuit, scenario write lock,
    /// reject paths) instead of leaking through a specific domain's reducer.
    ///
    /// Additionally contains the "no raw IPersistentSyncServerDomain implementations" regression gate that
    /// AGENTS.md pins down: new domains must inherit the template, the single exception is the documented
    /// projection allowlist (PartPurchases over Technology).
    /// </summary>
    [TestClass]
    public class ScenarioSyncDomainStoreTemplateTests
    {
        private const string ProbeScenarioName = "TemplateProbe";
        private static readonly ClientMessageFactory ClientMessageFactory = new ClientMessageFactory();

        [TestInitialize]
        public void Setup()
        {
            PersistentSyncRegistry.Reset();
            ScenarioStoreSystem.CurrentScenarios.Clear();
        }

        [TestMethod]
        public void EqualityShortCircuitDoesNotBumpRevisionOrRewriteScenario()
        {
            ScenarioStoreSystem.CurrentScenarios[ProbeScenarioName] = CreateScenario(42);
            var store = new ProbeDomainStore();
            store.LoadFromPersistence(false);
            var initialRevision = store.RevisionForTestsExposed;

            var result = store.ApplyClientIntent(null, MakeIntent(42));

            Assert.IsTrue(result.Accepted, "Equivalent reduce must still be accepted.");
            Assert.IsFalse(result.Changed, "Equivalent reduce must not mark Changed.");
            Assert.AreEqual(initialRevision, result.Snapshot.Revision, "Equivalent reduce must not bump Revision.");
            Assert.AreEqual(0, store.WriteCanonicalCallCount, "Equivalent reduce must not rewrite the scenario.");
        }

        [TestMethod]
        public void EqualityShortCircuitAsksOriginClientToResyncWhenItsRevisionIsStale()
        {
            ScenarioStoreSystem.CurrentScenarios[ProbeScenarioName] = CreateScenario(42);
            var store = new ProbeDomainStore();
            store.LoadFromPersistence(false);

            // Client claims to know revision 7; server is at 0. Even though the reduce is a no-op, the base
            // class must ask the origin client to resync so the client can catch up.
            var intent = MakeIntent(42);
            intent.ClientKnownRevision = 7;
            var result = store.ApplyClientIntent(null, intent);

            Assert.IsTrue(result.Accepted);
            Assert.IsFalse(result.Changed);
            Assert.IsTrue(result.ReplyToOriginClient, "Stale client revisions on equal reduces still need a reply.");
        }

        [TestMethod]
        public void ChangedReduceBumpsRevisionAndCallsWriteCanonicalOnce()
        {
            ScenarioStoreSystem.CurrentScenarios[ProbeScenarioName] = CreateScenario(10);
            var store = new ProbeDomainStore();
            store.LoadFromPersistence(false);
            var initialRevision = store.RevisionForTestsExposed;
            var initialWriteCount = store.WriteCanonicalCallCount;

            var result = store.ApplyClientIntent(null, MakeIntent(25));

            Assert.IsTrue(result.Accepted);
            Assert.IsTrue(result.Changed);
            Assert.AreEqual(initialRevision + 1, result.Snapshot.Revision);
            Assert.AreEqual(initialWriteCount + 1, store.WriteCanonicalCallCount);
        }

        [TestMethod]
        public void ReducerRejectPathLeavesStateAndRevisionUntouched()
        {
            ScenarioStoreSystem.CurrentScenarios[ProbeScenarioName] = CreateScenario(10);
            var store = new ProbeDomainStore();
            store.LoadFromPersistence(false);
            var initialRevision = store.RevisionForTestsExposed;
            var initialWriteCount = store.WriteCanonicalCallCount;

            var result = store.ApplyClientIntent(null, MakeIntent(25, rejected: true));

            Assert.IsFalse(result.Accepted);
            Assert.IsFalse(result.Changed);
            Assert.IsNull(result.Snapshot, "Rejected results must not carry a snapshot.");
            Assert.AreEqual(initialRevision, store.RevisionForTestsExposed);
            Assert.AreEqual(initialWriteCount, store.WriteCanonicalCallCount);
            Assert.AreEqual(10, store.CurrentValueForTests, "Rejected reduce must not mutate canonical state.");
        }

        [TestMethod]
        public void ReducerExceptionIsContainedAndReportedAsRejection()
        {
            ScenarioStoreSystem.CurrentScenarios[ProbeScenarioName] = CreateScenario(10);
            var store = new ProbeDomainStore();
            store.LoadFromPersistence(false);
            var initialRevision = store.RevisionForTestsExposed;

            var result = store.ApplyClientIntent(null, MakeIntent(25, throwInsideReducer: true));

            Assert.IsFalse(result.Accepted, "Exceptions in ReduceIntent must not bypass the reject path.");
            Assert.AreEqual(initialRevision, store.RevisionForTestsExposed);
        }

        [TestMethod]
        public void WriteCanonicalReturningNewConfigNodeReplacesScenarioEntry()
        {
            var originalScenario = CreateScenario(5);
            ScenarioStoreSystem.CurrentScenarios[ProbeScenarioName] = originalScenario;

            var store = new ProbeDomainStore { ReturnNewScenarioOnWrite = true };
            store.LoadFromPersistence(false);

            var result = store.ApplyClientIntent(null, MakeIntent(99));

            Assert.IsTrue(result.Accepted);
            Assert.IsTrue(result.Changed);
            Assert.IsTrue(ScenarioStoreSystem.CurrentScenarios.TryGetValue(ProbeScenarioName, out var current));
            Assert.IsFalse(ReferenceEquals(current, originalScenario),
                "WriteCanonical returning a new ConfigNode must replace the scenario store entry; domains like Contracts rely on this to work around LunaConfigNode ToString() caching.");
        }

        [TestMethod]
        public void ServerMutationPathSharesRevisionAndLockSemanticsWithClientIntent()
        {
            ScenarioStoreSystem.CurrentScenarios[ProbeScenarioName] = CreateScenario(1);
            var store = new ProbeDomainStore();
            store.LoadFromPersistence(false);

            var serverResult = store.ApplyServerMutation(EncodeValue(77), sizeof(int), "ServerSide");

            Assert.IsTrue(serverResult.Accepted);
            Assert.IsTrue(serverResult.Changed);
            Assert.AreEqual(1L, serverResult.Snapshot.Revision);
            Assert.AreEqual(77, store.CurrentValueForTests);
        }

        /// <summary>
        /// Regression gate: AGENTS.md requires every scenario sync domain to inherit the template. The single
        /// exception is the documented projection allowlist. If someone adds a new direct
        /// <see cref="IPersistentSyncServerDomain"/> implementation without extending this list, this test
        /// fails so the reviewer has to either migrate it onto the template or explicitly acknowledge the
        /// projection exception.
        /// </summary>
        [TestMethod]
        public void AllServerDomainsInheritTemplateUnlessInProjectionAllowlist()
        {
            var projectionAllowlist = new HashSet<string>(StringComparer.Ordinal)
            {
                // Pure projection over Technology's canonical; see class XML doc on PartPurchasesPersistentSyncDomainStore.
                typeof(PartPurchasesPersistentSyncDomainStore).FullName
            };

            var templateType = typeof(ScenarioSyncDomainStore<>);
            var serverAssembly = typeof(IPersistentSyncServerDomain).Assembly;

            var violators = serverAssembly
                .GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IPersistentSyncServerDomain).IsAssignableFrom(t))
                .Where(t => !InheritsFromOpenGeneric(t, templateType))
                .Where(t => !projectionAllowlist.Contains(t.FullName))
                .OrderBy(t => t.FullName)
                .Select(t => t.FullName)
                .ToList();

            if (violators.Count > 0)
            {
                Assert.Fail(
                    "The following types implement IPersistentSyncServerDomain directly without inheriting " +
                    "ScenarioSyncDomainStore<TCanonical> and are not in the projection allowlist. Migrate them onto " +
                    "the template or, if they really are pure projections, add them to the allowlist with a doc comment " +
                    "explaining why they own no scenario state:\n  - " + string.Join("\n  - ", violators));
            }
        }

        private static bool InheritsFromOpenGeneric(Type candidate, Type openGenericBase)
        {
            for (var current = candidate; current != null && current != typeof(object); current = current.BaseType)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == openGenericBase)
                {
                    return true;
                }
            }
            return false;
        }

        private static ConfigNode CreateScenario(int value)
        {
            return new ConfigNode(
                $"name = TemplateProbe\nvalue = {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n");
        }

        private static byte[] EncodeValue(int value)
        {
            return BitConverter.GetBytes(value);
        }

        private static PersistentSyncIntentMsgData MakeIntent(int newValue, bool rejected = false, bool throwInsideReducer = false)
        {
            var payload = new byte[sizeof(int) + 2];
            Buffer.BlockCopy(BitConverter.GetBytes(newValue), 0, payload, 0, sizeof(int));
            payload[sizeof(int)] = (byte)(rejected ? 1 : 0);
            payload[sizeof(int) + 1] = (byte)(throwInsideReducer ? 1 : 0);

            var data = ClientMessageFactory.CreateNewMessageData<PersistentSyncIntentMsgData>();
            data.DomainId = PersistentSyncDomainId.Funds;
            data.ClientKnownRevision = 0;
            data.Reason = "ProbeTest";
            data.Payload = payload;
            data.NumBytes = payload.Length;
            return data;
        }

        /// <summary>
        /// Minimal probe subclass. Canonical state is a single int. Payload layout:
        /// [0..3] = new int value, [4] = reject flag, [5] = throw flag.
        /// </summary>
        private sealed class ProbeDomainStore : ScenarioSyncDomainStore<ProbeDomainStore.Canonical>
        {
            public int WriteCanonicalCallCount { get; private set; }
            public bool ReturnNewScenarioOnWrite { get; set; }

            public int CurrentValueForTests => CurrentForTests?.Value ?? -1;
            public long RevisionForTestsExposed => RevisionForTests;

            public override PersistentSyncDomainId DomainId => PersistentSyncDomainId.Funds;
            public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;
            protected override string ScenarioName => ProbeScenarioName;

            protected override Canonical CreateEmpty() => new Canonical(0);

            protected override Canonical LoadCanonical(ConfigNode scenario, bool createdFromScratch)
            {
                if (scenario == null) return new Canonical(0);
                var rawValue = scenario.GetValue("value")?.Value;
                return int.TryParse(rawValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                    ? new Canonical(parsed)
                    : new Canonical(0);
            }

            protected override ReduceResult<Canonical> ReduceIntent(ClientStructure client, Canonical current, byte[] payload, int numBytes, string reason, bool isServerMutation)
            {
                if (payload == null || payload.Length < sizeof(int)) return ReduceResult<Canonical>.Reject();
                var value = BitConverter.ToInt32(payload, 0);
                var rejected = payload.Length > sizeof(int) && payload[sizeof(int)] != 0;
                var throwing = payload.Length > sizeof(int) + 1 && payload[sizeof(int) + 1] != 0;

                if (throwing) throw new InvalidOperationException("probe-throw");
                if (rejected) return ReduceResult<Canonical>.Reject();

                return ReduceResult<Canonical>.Accept(new Canonical(value));
            }

            protected override ConfigNode WriteCanonical(ConfigNode scenario, Canonical canonical)
            {
                WriteCanonicalCallCount++;
                if (scenario != null)
                {
                    scenario.UpdateValue("value", canonical.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                if (ReturnNewScenarioOnWrite)
                {
                    return new ConfigNode(
                        $"name = TemplateProbe\nvalue = {canonical.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n");
                }

                return scenario;
            }

            protected override byte[] SerializeSnapshot(Canonical canonical)
            {
                return BitConverter.GetBytes(canonical?.Value ?? 0);
            }

            protected override bool AreEquivalent(Canonical a, Canonical b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a == null || b == null) return false;
                return a.Value == b.Value;
            }

            internal sealed class Canonical
            {
                public int Value { get; }
                public Canonical(int value) { Value = value; }
            }
        }
    }
}
