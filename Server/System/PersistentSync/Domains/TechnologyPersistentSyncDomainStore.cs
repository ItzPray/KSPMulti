using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
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
    [PersistentSyncStockScenario("ResearchAndDevelopment")]
    public sealed class TechnologyPersistentSyncDomainStore : SyncDomainStore<TechnologyPayload>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncServerDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesServerDomain<TechnologyPersistentSyncDomainStore>();
        }

        private const string TechNodeName = "Tech";
        private const string TechIdFieldName = "id";
        private const string TechStateFieldName = "state";
        private const string TechCostFieldName = "cost";
        private const string TechPartFieldName = "part";
        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        /// <summary>
        /// Exposes the current canonical state for PartPurchasesPersistentSyncDomainStore to
        /// project its wire snapshot. Internal so projection domains living in the same assembly can read it
        /// without widening the public API.
        /// </summary>
        internal Canonical CurrentForProjection => ToCanonical(CurrentForTests?.Payload);

        /// <summary>
        /// Routing hook used by PartPurchasesPersistentSyncDomainStore to fold its decoded
        /// intent into this domain's canonical state. Produces a result whose Snapshot payload is the
        /// Technology wire format; callers re-project into PartPurchases wire format before returning.
        /// </summary>
        internal PersistentSyncDomainApplyResult ApplyPartPurchasesIntent(PartPurchaseSnapshotInfo[] records, long? clientKnownRevision, string reason, bool isServerMutation)
        {
            return ApplyPartPurchasesInternal(records, clientKnownRevision, reason, isServerMutation);
        }

        protected override TechnologyPayload CreateDefaultPayload()
        {
            return BuildPayload(CreateEmptyCanonical());
        }

        protected override TechnologyPayload LoadPayload(ConfigNode scenario, bool createdFromScratch)
        {
            return BuildPayload(LoadCanonicalState(scenario, createdFromScratch));
        }

        protected override ReduceResult<TechnologyPayload> ReducePayload(ClientStructure client, TechnologyPayload current, TechnologyPayload incoming, string reason, bool isServerMutation)
        {
            var canonical = ToCanonical(current);
            var techReduced = ReduceTechnologyPayload(canonical, incoming?.Technologies, reason, isServerMutation);
            if (techReduced == null || !techReduced.Accepted)
            {
                return ReduceResult<TechnologyPayload>.Reject();
            }

            var partsReduced = ReducePartPurchases(techReduced.NextState ?? canonical, incoming?.PartPurchases);
            if (partsReduced == null || !partsReduced.Accepted)
            {
                return ReduceResult<TechnologyPayload>.Reject();
            }

            return ReduceResult<TechnologyPayload>.Accept(BuildPayload(partsReduced.NextState), partsReduced.ForceReplyToOriginClient, partsReduced.ReplyToProducerClient);
        }

        protected override ConfigNode WritePayload(ConfigNode scenario, TechnologyPayload payload)
        {
            return WriteCanonicalState(scenario, ToCanonical(payload));
        }

        protected override bool PayloadsAreEqual(TechnologyPayload left, TechnologyPayload right)
        {
            return AreEquivalent(ToCanonical(left), ToCanonical(right));
        }

        private static Canonical CreateEmptyCanonical()
        {
            return new Canonical(
                new SortedDictionary<string, TechStateInfo>(StringComparer.Ordinal),
                new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal));
        }

        private static Canonical LoadCanonicalState(ConfigNode scenario, bool createdFromScratch)
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

        private static ReduceResult<Canonical> ReduceTechnologyPayload(Canonical current, TechnologySnapshotInfo[] intent, string reason, bool isServerMutation)
        {
            var nextStates = new SortedDictionary<string, TechStateInfo>(current.TechStates, StringComparer.Ordinal);

            foreach (var technology in intent ?? new TechnologySnapshotInfo[0])
            {
                var normalized = NormalizeSnapshotInfo(technology);
                if (normalized == null) continue;

                var parsed = ParseSnapshotNode(normalized.Data);
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
            var payload = PersistentSyncPayloadSerializer.Serialize(new PartPurchasesPayload { Items = records ?? Array.Empty<PartPurchaseSnapshotInfo>() });
            var proxyReason = "[PartPurchases] " + (reason ?? string.Empty);

            // Route through a thin internal seam that bypasses Technology's ReduceIntent (which interprets
            // payload as Technology wire format) and instead reduces the PartPurchases wire format.
            return ApplyWithCustomReduce<PartPurchasesPayload>(payload, clientKnownRevision, proxyReason, isServerMutation,
                (current, partPurchasesPayload) =>
                {
                    var reduced = ReducePartPurchases(ToCanonical(current.Payload), partPurchasesPayload?.Items);
                    return reduced == null || !reduced.Accepted
                        ? ReduceResult<PayloadBox>.Reject()
                        : ReduceResult<PayloadBox>.Accept(
                            new PayloadBox(BuildPayload(reduced.NextState)),
                            reduced.ForceReplyToOriginClient,
                            reduced.ReplyToProducerClient);
                });
        }

        private static ReduceResult<Canonical> ReducePartPurchases(Canonical current, PartPurchaseSnapshotInfo[] records)
        {
            records = records ?? new PartPurchaseSnapshotInfo[0];
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

        private static ConfigNode WriteCanonicalState(ConfigNode scenario, Canonical canonical)
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

        private static TechnologyPayload BuildPayload(Canonical canonical)
        {
            var records = canonical.TechStates.Values
                .Select(state => state.ToSnapshotInfo())
                .Select(CloneInfo)
                .ToArray();
            LunaLog.Normal($"[PersistentSync] Technology BuildSnapshotPayload: techCount={canonical.TechStates.Count}");
            return new TechnologyPayload
            {
                Technologies = records,
                PartPurchases = BuildPartPurchasesPayload(canonical)
            };
        }

        /// <summary>
        /// PartPurchases wire projection: emits a snapshot with the PartPurchases binary format using the
        /// current canonical PartsByTech. Kept on this class so the projection
        /// domain does not need to reach into internal canonical types.
        /// </summary>
        internal PartPurchaseSnapshotInfo[] BuildPartPurchasesSnapshotPayload()
        {
            return BuildPartPurchasesPayload(CurrentForProjection);
        }

        private static PartPurchaseSnapshotInfo[] BuildPartPurchasesPayload(Canonical canonical)
        {
            canonical = canonical ?? CreateEmptyCanonical();
            return canonical.PartsByTech
                .Where(value => value.Value != null && value.Value.Count > 0)
                .Select(value => new PartPurchaseSnapshotInfo
                {
                    TechId = value.Key,
                    PartNames = value.Value.ToArray()
                })
                .ToArray();
        }

        private static bool AreEquivalent(Canonical a, Canonical b)
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
            if (technology == null || technology.Data == null || technology.Data.Length <= 0)
            {
                return null;
            }

            var node = ParseSnapshotNode(technology.Data);
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
                Data = normalizedBytes
            };
        }

        private static ConfigNode ParseSnapshotNode(byte[] data)
        {
            data = data ?? Array.Empty<byte>();
            return new ConfigNode(Encoding.UTF8.GetString(data, 0, data.Length));
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
            var data = new byte[source.Data.Length];
            Buffer.BlockCopy(source.Data, 0, data, 0, source.Data.Length);
            return new TechnologySnapshotInfo
            {
                TechId = source.TechId,
                Data = data
            };
        }

        private static Canonical ToCanonical(TechnologyPayload payload)
        {
            var techStates = new SortedDictionary<string, TechStateInfo>(StringComparer.Ordinal);
            foreach (var technology in payload?.Technologies ?? new TechnologySnapshotInfo[0])
            {
                var normalized = NormalizeSnapshotInfo(technology);
                if (normalized == null)
                {
                    continue;
                }

                var parsed = ParseSnapshotNode(normalized.Data);
                techStates[normalized.TechId] = new TechStateInfo(
                    normalized.TechId,
                    parsed.GetValue(TechStateFieldName)?.Value ?? string.Empty,
                    parsed.GetValue(TechCostFieldName)?.Value ?? string.Empty);
            }

            var partsByTech = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (var record in payload?.PartPurchases ?? new PartPurchaseSnapshotInfo[0])
            {
                if (record == null || string.IsNullOrEmpty(record.TechId))
                {
                    continue;
                }

                var parts = new SortedSet<string>(
                    (record.PartNames ?? new string[0]).Where(value => !string.IsNullOrEmpty(value)),
                    StringComparer.Ordinal);
                if (parts.Count > 0)
                {
                    partsByTech[record.TechId] = parts;
                }
            }

            return new Canonical(techStates, partsByTech);
        }

        /// <summary>
        /// Typed canonical state for the Technology domain. Owns both the scalar tech fields and the repeated
        /// <c>part=</c> purchased-parts values (to satisfy the "one scenario, one domain" rule against
        /// PartPurchasesPersistentSyncDomainStore).
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

