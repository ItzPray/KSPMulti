using LmpCommon.Enums;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Server.Client;
using Server.Log;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Server.System.PersistentSync
{
    /// <summary>
    /// Sole writer of <c>ResearchAndDevelopment/Tech/*</c> nodes. Owns both the scalar tech state
    /// (<c>id</c>/<c>state</c>/<c>cost</c>) and the repeated <c>part=</c> purchased-parts values so that the
    /// "one scenario, one domain" Scenario Sync Domain Contract rule is satisfied. <see
    /// cref="PartPurchasesPersistentSyncDomainStore"/> projects from this domain's canonical state and routes
    /// its intents here; it does not write the scenario.
    /// </summary>
    public sealed class TechnologyPersistentSyncDomainStore : ScenarioSyncDomainStore<TechnologyPersistentSyncDomainStore.Canonical>
    {
        private const string TechNodeName = "Tech";
        private const string TechIdFieldName = "id";
        private const string TechStateFieldName = "state";
        private const string TechCostFieldName = "cost";
        private const string TechPartFieldName = "part";

        public override PersistentSyncDomainId DomainId => PersistentSyncDomainId.Technology;
        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;
        protected override string ScenarioName => "ResearchAndDevelopment";

        public override bool AuthorizeIntent(ClientStructure client, byte[] payload, int numBytes) => AuthorizeByPolicy(client);

        /// <summary>
        /// Exposes the current canonical state for <see cref="PartPurchasesPersistentSyncDomainStore"/> to
        /// project its wire snapshot. Internal so projection domains living in the same assembly can read it
        /// without widening the public API.
        /// </summary>
        internal Canonical CurrentForProjection => CurrentForTests;

        /// <summary>
        /// Routing hook used by <see cref="PartPurchasesPersistentSyncDomainStore"/> to fold its decoded
        /// intent into this domain's canonical state. Produces a result whose Snapshot payload is the
        /// Technology wire format; callers re-project into PartPurchases wire format before returning.
        /// </summary>
        internal PersistentSyncDomainApplyResult ApplyPartPurchasesIntent(PartPurchaseSnapshotInfo[] records, long? clientKnownRevision, string reason, bool isServerMutation)
        {
            return ApplyPartPurchasesInternal(records, clientKnownRevision, reason, isServerMutation);
        }

        protected override Canonical CreateEmpty()
        {
            return new Canonical(
                new SortedDictionary<string, TechStateInfo>(StringComparer.Ordinal),
                new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal));
        }

        protected override Canonical LoadCanonical(ConfigNode scenario, bool createdFromScratch)
        {
            var techStates = new SortedDictionary<string, TechStateInfo>(StringComparer.Ordinal);
            var partsByTech = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

            if (scenario == null)
            {
                LunaLog.Normal($"[PersistentSync] Technology LoadFromPersistence: scenario 'ResearchAndDevelopment' not found; starting empty");
                return new Canonical(techStates, partsByTech);
            }

            var scenarioTechNodeCount = scenario.GetNodes(TechNodeName).Count(node => node?.Value != null);
            foreach (var techNode in scenario.GetNodes(TechNodeName).Select(node => node.Value).Where(node => node != null))
            {
                var techId = techNode.GetValue(TechIdFieldName)?.Value;
                if (string.IsNullOrEmpty(techId)) continue;

                var state = techNode.GetValue(TechStateFieldName)?.Value ?? string.Empty;
                var cost = techNode.GetValue(TechCostFieldName)?.Value ?? string.Empty;
                techStates[techId] = new TechStateInfo(techId, state, cost);

                var parts = new SortedSet<string>(
                    techNode.GetValues(TechPartFieldName).Select(value => value.Value).Where(value => !string.IsNullOrEmpty(value)),
                    StringComparer.Ordinal);
                // Omit techs with no persisted purchases (legacy PartPurchases convention: avoids broadcasting
                // all-empty entries that would otherwise cause the client to clobber partsPurchased).
                if (parts.Count > 0)
                {
                    partsByTech[techId] = parts;
                }
            }

            LunaLog.Normal($"[PersistentSync] Technology LoadFromPersistence: scenarioTechNodes={scenarioTechNodeCount} techStates={techStates.Count} partsByTech={partsByTech.Count}");
            return new Canonical(techStates, partsByTech);
        }

        protected override ReduceResult<Canonical> ReduceIntent(ClientStructure client, Canonical current, byte[] payload, int numBytes, string reason, bool isServerMutation)
        {
            var technologies = TechnologySnapshotPayloadSerializer.Deserialize(payload, numBytes) ?? new List<TechnologySnapshotInfo>();
            var nextStates = new SortedDictionary<string, TechStateInfo>(current.TechStates, StringComparer.Ordinal);

            foreach (var technology in technologies)
            {
                var normalized = NormalizeSnapshotInfo(technology);
                if (normalized == null) continue;

                var parsed = ParseSnapshotNode(normalized.Data, normalized.NumBytes);
                var techId = normalized.TechId;
                var state = parsed.GetValue(TechStateFieldName)?.Value ?? string.Empty;
                var cost = parsed.GetValue(TechCostFieldName)?.Value ?? string.Empty;
                nextStates[techId] = new TechStateInfo(techId, state, cost);
            }

            return ReduceResult<Canonical>.Accept(new Canonical(nextStates, current.PartsByTech));
        }

        /// <summary>
        /// Applies a PartPurchases intent against the Technology canonical. Reuses the base class locking,
        /// equality short-circuit, revision bump and scenario write path; we only need to compute the
        /// candidate next canonical here and feed it through the base class seam.
        /// </summary>
        private PersistentSyncDomainApplyResult ApplyPartPurchasesInternal(PartPurchaseSnapshotInfo[] records, long? clientKnownRevision, string reason, bool isServerMutation)
        {
            var payload = PartPurchasesSnapshotPayloadSerializer.Serialize(records ?? new PartPurchaseSnapshotInfo[0]);
            var proxyReason = "[PartPurchases] " + (reason ?? string.Empty);

            // Route through a thin internal seam that bypasses Technology's ReduceIntent (which interprets
            // payload as Technology wire format) and instead reduces the PartPurchases wire format.
            return ApplyWithCustomReduce(payload, payload.Length, clientKnownRevision, proxyReason, isServerMutation,
                (current, decodedPayload) => ReducePartPurchases(current, decodedPayload));
        }

        private static ReduceResult<Canonical> ReducePartPurchases(Canonical current, byte[] payload)
        {
            var records = PartPurchasesSnapshotPayloadSerializer.Deserialize(payload) ?? new PartPurchaseSnapshotInfo[0];
            var nextParts = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (var existing in current.PartsByTech)
            {
                nextParts[existing.Key] = new SortedSet<string>(existing.Value, StringComparer.Ordinal);
            }

            foreach (var record in records)
            {
                if (record == null || string.IsNullOrEmpty(record.TechId)) continue;

                var normalizedParts = new SortedSet<string>(
                    (record.PartNames ?? new string[0]).Where(value => !string.IsNullOrEmpty(value)),
                    StringComparer.Ordinal);

                if (normalizedParts.Count == 0)
                {
                    nextParts.Remove(record.TechId);
                    continue;
                }

                nextParts[record.TechId] = normalizedParts;
            }

            return ReduceResult<Canonical>.Accept(new Canonical(current.TechStates, nextParts));
        }

        protected override ConfigNode WriteCanonical(ConfigNode scenario, Canonical canonical)
        {
            // Technology is the sole writer of Tech/* nodes: the scalar id/state/cost fields AND the
            // repeated part= values. Build each node wholesale from canonical state so the on-disk
            // representation always matches what we serialize over the wire.
            var existingNodes = scenario.GetNodes(TechNodeName).Select(node => node.Value).Where(node => node != null).ToList();

            var firstNodeByTechId = new Dictionary<string, ConfigNode>(StringComparer.Ordinal);
            foreach (var node in existingNodes)
            {
                var techId = node.GetValue(TechIdFieldName)?.Value;
                if (string.IsNullOrEmpty(techId))
                {
                    scenario.RemoveNode(node);
                    continue;
                }

                if (firstNodeByTechId.ContainsKey(techId))
                {
                    scenario.RemoveNode(node);
                    continue;
                }

                firstNodeByTechId[techId] = node;
            }

            foreach (var kvp in firstNodeByTechId.ToArray())
            {
                if (!canonical.TechStates.ContainsKey(kvp.Key))
                {
                    scenario.RemoveNode(kvp.Value);
                    firstNodeByTechId.Remove(kvp.Key);
                }
            }

            foreach (var techState in canonical.TechStates.Values)
            {
                canonical.PartsByTech.TryGetValue(techState.TechId, out var parts);
                if (firstNodeByTechId.TryGetValue(techState.TechId, out var techNode))
                {
                    ApplyTechStateToScenarioNode(techNode, techState, parts);
                }
                else
                {
                    scenario.AddNode(CreateScenarioTechNode(techState, parts));
                }
            }

            return scenario;
        }

        protected override byte[] SerializeSnapshot(Canonical canonical)
        {
            var records = canonical.TechStates.Values
                .Select(state => state.ToSnapshotInfo())
                .Select(CloneInfo)
                .ToArray();
            var payload = TechnologySnapshotPayloadSerializer.Serialize(records);
            LunaLog.Normal($"[PersistentSync] Technology SerializeSnapshot: techCount={canonical.TechStates.Count} payloadBytes={payload.Length}");
            return payload;
        }

        /// <summary>
        /// PartPurchases wire projection: emits a snapshot with the PartPurchases binary format using the
        /// current canonical <see cref="Canonical.PartsByTech"/>. Kept on this class so the projection
        /// domain does not need to reach into internal canonical types.
        /// </summary>
        internal byte[] SerializePartPurchasesSnapshot()
        {
            var canonical = CurrentForTests ?? CreateEmpty();
            return PartPurchasesSnapshotPayloadSerializer.Serialize(canonical.PartsByTech
                .Where(value => value.Value != null && value.Value.Count > 0)
                .Select(value => new PartPurchaseSnapshotInfo
                {
                    TechId = value.Key,
                    PartNames = value.Value.ToArray()
                })
                .ToArray());
        }

        protected override bool AreEquivalent(Canonical a, Canonical b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;

            if (a.TechStates.Count != b.TechStates.Count) return false;
            foreach (var kvp in a.TechStates)
            {
                if (!b.TechStates.TryGetValue(kvp.Key, out var other) || !TechStateInfo.Equals(kvp.Value, other))
                {
                    return false;
                }
            }

            if (a.PartsByTech.Count != b.PartsByTech.Count) return false;
            foreach (var kvp in a.PartsByTech)
            {
                if (!b.PartsByTech.TryGetValue(kvp.Key, out var other) || !kvp.Value.SetEquals(other))
                {
                    return false;
                }
            }

            return true;
        }

        private static void ApplyTechStateToScenarioNode(ConfigNode techNode, TechStateInfo state, SortedSet<string> parts)
        {
            ReplaceScalarValue(techNode, TechIdFieldName, state.TechId);
            ReplaceScalarValue(techNode, TechStateFieldName, state.State);
            ReplaceScalarValue(techNode, TechCostFieldName, state.Cost);

            while (techNode.GetValues(TechPartFieldName).Any())
            {
                techNode.RemoveValue(TechPartFieldName);
            }

            if (parts != null)
            {
                foreach (var partName in parts)
                {
                    techNode.CreateValue(new CfgNodeValue<string, string>(TechPartFieldName, partName));
                }
            }
        }

        private static void ReplaceScalarValue(ConfigNode techNode, string key, string value)
        {
            while (techNode.GetValues(key).Any())
            {
                techNode.RemoveValue(key);
            }

            if (!string.IsNullOrEmpty(value))
            {
                techNode.CreateValue(new CfgNodeValue<string, string>(key, value));
            }
        }

        private static ConfigNode CreateScenarioTechNode(TechStateInfo state, SortedSet<string> parts)
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(state.TechId)) lines.Add($"{TechIdFieldName} = {state.TechId}");
            if (!string.IsNullOrEmpty(state.State)) lines.Add($"{TechStateFieldName} = {state.State}");
            if (!string.IsNullOrEmpty(state.Cost)) lines.Add($"{TechCostFieldName} = {state.Cost}");
            if (parts != null)
            {
                foreach (var partName in parts)
                {
                    lines.Add($"{TechPartFieldName} = {partName}");
                }
            }

            return new ConfigNode(string.Join("\n", lines) + "\n") { Name = TechNodeName };
        }

        private static TechnologySnapshotInfo NormalizeSnapshotInfo(TechnologySnapshotInfo technology)
        {
            if (technology == null || technology.Data == null || technology.NumBytes <= 0)
            {
                return null;
            }

            var node = ParseSnapshotNode(technology.Data, technology.NumBytes);
            var techId = node?.GetValue(TechIdFieldName)?.Value;
            if (string.IsNullOrEmpty(techId))
            {
                return null;
            }

            var normalizedText = BuildBareNodeText(techId,
                node.GetValue(TechStateFieldName)?.Value,
                node.GetValue(TechCostFieldName)?.Value);
            var normalizedBytes = Encoding.UTF8.GetBytes(normalizedText);
            return new TechnologySnapshotInfo
            {
                TechId = techId,
                NumBytes = normalizedBytes.Length,
                Data = normalizedBytes
            };
        }

        private static ConfigNode ParseSnapshotNode(byte[] data, int numBytes)
        {
            return new ConfigNode(Encoding.UTF8.GetString(data, 0, numBytes));
        }

        private static string BuildBareNodeText(string techId, string state, string cost)
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(techId)) lines.Add($"{TechIdFieldName} = {techId}");
            if (!string.IsNullOrEmpty(state)) lines.Add($"{TechStateFieldName} = {state}");
            if (!string.IsNullOrEmpty(cost)) lines.Add($"{TechCostFieldName} = {cost}");
            return string.Join("\n", lines) + "\n";
        }

        private static TechnologySnapshotInfo CloneInfo(TechnologySnapshotInfo source)
        {
            var data = new byte[source.NumBytes];
            Buffer.BlockCopy(source.Data, 0, data, 0, source.NumBytes);
            return new TechnologySnapshotInfo
            {
                TechId = source.TechId,
                NumBytes = source.NumBytes,
                Data = data
            };
        }

        /// <summary>
        /// Typed canonical state for the Technology domain. Owns both the scalar tech fields and the repeated
        /// <c>part=</c> purchased-parts values (to satisfy the "one scenario, one domain" rule against
        /// <see cref="PartPurchasesPersistentSyncDomainStore"/>).
        /// </summary>
        public sealed class Canonical
        {
            public Canonical(SortedDictionary<string, TechStateInfo> techStates, SortedDictionary<string, SortedSet<string>> partsByTech)
            {
                TechStates = techStates ?? new SortedDictionary<string, TechStateInfo>(StringComparer.Ordinal);
                PartsByTech = partsByTech ?? new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            }

            public SortedDictionary<string, TechStateInfo> TechStates { get; }
            public SortedDictionary<string, SortedSet<string>> PartsByTech { get; }
        }

        /// <summary>Scalar tech node values (<c>id</c>, <c>state</c>, <c>cost</c>) in their canonical form.</summary>
        public sealed class TechStateInfo
        {
            public TechStateInfo(string techId, string state, string cost)
            {
                TechId = techId ?? string.Empty;
                State = state ?? string.Empty;
                Cost = cost ?? string.Empty;
            }

            public string TechId { get; }
            public string State { get; }
            public string Cost { get; }

            public TechnologySnapshotInfo ToSnapshotInfo()
            {
                var text = BuildBareNodeText(TechId, State, Cost);
                var bytes = Encoding.UTF8.GetBytes(text);
                return new TechnologySnapshotInfo
                {
                    TechId = TechId,
                    NumBytes = bytes.Length,
                    Data = bytes
                };
            }

            public static bool Equals(TechStateInfo a, TechStateInfo b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a == null || b == null) return false;
                return string.Equals(a.TechId, b.TechId, StringComparison.Ordinal)
                       && string.Equals(a.State, b.State, StringComparison.Ordinal)
                       && string.Equals(a.Cost, b.Cost, StringComparison.Ordinal);
            }
        }
    }
}
