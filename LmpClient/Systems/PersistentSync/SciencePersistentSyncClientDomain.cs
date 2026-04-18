using System;
using System.Collections.Generic;
using System.Linq;
using KSP.UI.Screens;
using LmpClient.Extensions;
using LmpClient.Systems.ShareAchievements;
using LmpClient.Systems.ShareExperimentalParts;
using LmpClient.Systems.SharePurchaseParts;
using LmpClient.Systems.ShareScience;
using LmpClient.Systems.ShareScienceSubject;
using LmpClient.Systems.ShareStrategy;
using LmpClient.Systems.ShareTechnology;
using LmpCommon.PersistentSync;
using Strategies;

namespace LmpClient.Systems.PersistentSync
{
    public class SciencePersistentSyncClientDomain : ScalarPersistentSyncClientDomain<float>
    {
        public override PersistentSyncDomainId DomainId => PersistentSyncDomainId.Science;

        protected override float DeserializePayload(byte[] payload, int numBytes)
        {
            return ScienceSnapshotPayloadSerializer.Deserialize(payload, numBytes);
        }

        protected override bool CanApplyLiveState()
        {
            return ResearchAndDevelopment.Instance != null;
        }

        protected override void ApplyLiveState(float value)
        {
            ShareScienceSystem.Singleton.SetScienceWithoutTriggeringEvent(value);
        }
    }

    public class StrategyPersistentSyncClientDomain : IPersistentSyncClientDomain
    {
        private Dictionary<string, StrategySnapshotInfo> _pendingStrategies;

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.Strategy;

        public PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot)
        {
            try
            {
                _pendingStrategies = StrategySnapshotPayloadSerializer.Deserialize(snapshot.Payload)
                    .Where(strategy => strategy != null && !string.IsNullOrEmpty(strategy.Name))
                    .ToDictionary(strategy => strategy.Name, strategy => strategy, StringComparer.Ordinal);
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            return FlushPendingState();
        }

        public PersistentSyncApplyOutcome FlushPendingState()
        {
            if (_pendingStrategies == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (StrategySystem.Instance == null || Funding.Instance == null || ResearchAndDevelopment.Instance == null || Reputation.Instance == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            foreach (var strategy in _pendingStrategies.Values.OrderBy(value => value.Name))
            {
                if (!ShareStrategySystem.Singleton.ApplyStrategySnapshot(strategy, "PersistentSyncSnapshotApply", false))
                {
                    return PersistentSyncApplyOutcome.Rejected;
                }
            }

            ShareStrategySystem.Singleton.RefreshStrategyUiAdapters("PersistentSyncSnapshotApply");
            _pendingStrategies = null;
            return PersistentSyncApplyOutcome.Applied;
        }
    }

    public class AchievementsPersistentSyncClientDomain : IPersistentSyncClientDomain
    {
        private AchievementSnapshotInfo[] _pendingAchievements;

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.Achievements;

        public PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot)
        {
            try
            {
                _pendingAchievements = AchievementSnapshotPayloadSerializer.Deserialize(snapshot.Payload);
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            return FlushPendingState();
        }

        public PersistentSyncApplyOutcome FlushPendingState()
        {
            if (_pendingAchievements == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (ProgressTracking.Instance == null || Funding.Instance == null || ResearchAndDevelopment.Instance == null || Reputation.Instance == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            var rootNode = new ConfigNode();
            try
            {
                foreach (var achievement in _pendingAchievements.Where(value => value != null && value.NumBytes > 0))
                {
                    // Payload is a ConfigNodeSerializer-serialized node; `new ConfigNode(string)` would
                    // silently store the entire text as the node name with no child values, so the
                    // achievement tree would receive empty nodes. Use the matching deserializer.
                    var achievementNode = achievement.Data.DeserializeToConfigNode(achievement.NumBytes);
                    if (achievementNode != null)
                    {
                        rootNode.AddNode(achievementNode);
                    }
                }
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            ShareAchievementsSystem.Singleton.ApplyAchievementSnapshotTree(rootNode, "PersistentSyncSnapshotApply");
            _pendingAchievements = null;
            return PersistentSyncApplyOutcome.Applied;
        }
    }

    public class ScienceSubjectsPersistentSyncClientDomain : IPersistentSyncClientDomain
    {
        private ScienceSubjectSnapshotInfo[] _pendingSubjects;

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.ScienceSubjects;

        public PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot)
        {
            try
            {
                _pendingSubjects = ScienceSubjectSnapshotPayloadSerializer.Deserialize(snapshot.Payload);
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            return FlushPendingState();
        }

        public PersistentSyncApplyOutcome FlushPendingState()
        {
            if (_pendingSubjects == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (ResearchAndDevelopment.Instance == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            var subjects = new Dictionary<string, ScienceSubject>(StringComparer.Ordinal);
            try
            {
                foreach (var snapshot in _pendingSubjects.Where(value => value != null && value.NumBytes > 0))
                {
                    var subject = ShareScienceSubjectMessageHandler.ConvertByteArrayToScienceSubject(snapshot.Data, snapshot.NumBytes);
                    if (subject == null || string.IsNullOrEmpty(subject.id))
                    {
                        return PersistentSyncApplyOutcome.Rejected;
                    }

                    subjects[subject.id] = subject;
                }
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            ShareScienceSubjectSystem.Singleton.StartIgnoringEvents();
            try
            {
                ShareScienceSubjectSystem.Singleton.ReplaceScienceSubjects(subjects, "PersistentSyncSnapshotApply");
            }
            finally
            {
                ShareScienceSubjectSystem.Singleton.StopIgnoringEvents();
            }

            _pendingSubjects = null;
            return PersistentSyncApplyOutcome.Applied;
        }
    }

    public class ExperimentalPartsPersistentSyncClientDomain : IPersistentSyncClientDomain
    {
        private ExperimentalPartSnapshotInfo[] _pendingParts;

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.ExperimentalParts;

        public PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot)
        {
            try
            {
                _pendingParts = ExperimentalPartsSnapshotPayloadSerializer.Deserialize(snapshot.Payload);
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            return FlushPendingState();
        }

        public PersistentSyncApplyOutcome FlushPendingState()
        {
            if (_pendingParts == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (ResearchAndDevelopment.Instance == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            var stock = new Dictionary<AvailablePart, int>();
            try
            {
                foreach (var snapshot in _pendingParts.Where(value => value != null && !string.IsNullOrEmpty(value.PartName) && value.Count > 0))
                {
                    var part = ResolvePart(snapshot.PartName);
                    if (part == null)
                    {
                        return PersistentSyncApplyOutcome.Rejected;
                    }

                    stock[part] = snapshot.Count;
                }
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            ShareExperimentalPartsSystem.Singleton.StartIgnoringEvents();
            try
            {
                ShareExperimentalPartsSystem.Singleton.ReplaceExperimentalPartsStock(stock, "PersistentSyncSnapshotApply");
            }
            finally
            {
                ShareExperimentalPartsSystem.Singleton.StopIgnoringEvents();
            }

            _pendingParts = null;
            return PersistentSyncApplyOutcome.Applied;
        }

        private static AvailablePart ResolvePart(string partName)
        {
            if (string.IsNullOrEmpty(partName))
            {
                return null;
            }

            var partInfo = PartLoader.getPartInfoByName(partName);
            if (partInfo != null)
            {
                return partInfo;
            }

            return PartLoader.getPartInfoByName(partName.Replace('_', '.').Trim());
        }
    }

    public class PartPurchasesPersistentSyncClientDomain : IPersistentSyncClientDomain
    {
        private Dictionary<string, PartPurchaseSnapshotInfo> _pendingPurchases;

        /// <summary>
        /// Last deserialized server purchased-parts map. Mirrors the reassert pattern used by
        /// <see cref="TechnologyPersistentSyncClientDomain"/>: when KSP re-hydrates R&amp;D state the
        /// purchased parts ride along with techStates, so we must re-stage both together.
        /// </summary>
        private Dictionary<string, PartPurchaseSnapshotInfo> _authoritativePurchases;

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.PartPurchases;

        public PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot)
        {
            try
            {
                _pendingPurchases = PartPurchasesSnapshotPayloadSerializer.Deserialize(snapshot.Payload)
                    .Where(value => value != null && !string.IsNullOrEmpty(value.TechId))
                    .ToDictionary(value => value.TechId, value => value, StringComparer.Ordinal);
                _authoritativePurchases = new Dictionary<string, PartPurchaseSnapshotInfo>(_pendingPurchases, StringComparer.Ordinal);
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            return FlushPendingState();
        }

        public bool TryStageReassertFromLastServerSnapshot()
        {
            if (_authoritativePurchases == null || _authoritativePurchases.Count == 0)
            {
                return false;
            }

            _pendingPurchases = new Dictionary<string, PartPurchaseSnapshotInfo>(_authoritativePurchases, StringComparer.Ordinal);
            return true;
        }

        public PersistentSyncApplyOutcome FlushPendingState()
        {
            if (_pendingPurchases == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (ResearchAndDevelopment.Instance == null || AssetBase.RnDTechTree == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            SharePurchasePartsSystem.Singleton.StartIgnoringEvents();
            try
            {
                foreach (var tech in AssetBase.RnDTechTree.GetTreeTechs().Where(node => node != null))
                {
                    var techState = ResearchAndDevelopment.Instance.GetTechState(tech.techID);
                    if (techState == null)
                    {
                        continue;
                    }

                    techState.partsPurchased = _pendingPurchases.TryGetValue(tech.techID, out var purchase)
                        ? (purchase.PartNames ?? new string[0]).Select(ResolvePart).Where(part => part != null).Distinct().ToList()
                        : new List<AvailablePart>();
                    ResearchAndDevelopment.Instance.SetTechState(tech.techID, techState);
                }

                ResearchAndDevelopmentProtoMirror.TrySyncLiveInstanceToGameProto("PartPurchasesFlush");

                SharePurchasePartsSystem.Singleton.RefreshPurchaseUiAdapters("PersistentSyncSnapshotApply");
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }
            finally
            {
                SharePurchasePartsSystem.Singleton.StopIgnoringEvents();
            }

            _pendingPurchases = null;
            return PersistentSyncApplyOutcome.Applied;
        }

        private static AvailablePart ResolvePart(string partName)
        {
            if (string.IsNullOrEmpty(partName))
            {
                return null;
            }

            var partInfo = PartLoader.getPartInfoByName(partName);
            if (partInfo != null)
            {
                return partInfo;
            }

            return PartLoader.getPartInfoByName(partName.Replace('_', '.').Trim());
        }
    }

    public class TechnologyPersistentSyncClientDomain : IPersistentSyncClientDomain
    {
        private Dictionary<string, TechnologySnapshotInfo> _pendingTechnologyById;

        /// <summary>
        /// Last deserialized server tech states. KSP can reinitialize R&D.Instance (or re-hydrate it from a
        /// stale ProtoScenarioModule configNode) after our first <see cref="FlushPendingState"/>, silently
        /// reverting techs back to Unavailable. Re-staging from this cache on scene-ready and on
        /// <see cref="GameEvents.onGUIRnDComplexSpawn"/> reasserts the server-authoritative state.
        /// </summary>
        private Dictionary<string, TechnologySnapshotInfo> _authoritativeTechnologyById;

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.Technology;

        public PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot)
        {
            try
            {
                _pendingTechnologyById = TechnologySnapshotPayloadSerializer.Deserialize(snapshot.Payload, snapshot.NumBytes)
                    .Where(technology => technology != null && !string.IsNullOrEmpty(technology.TechId))
                    .ToDictionary(technology => technology.TechId, technology => technology, StringComparer.Ordinal);
                _authoritativeTechnologyById = new Dictionary<string, TechnologySnapshotInfo>(_pendingTechnologyById, StringComparer.Ordinal);
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            LunaLog.Log($"[PersistentSync] Technology ApplySnapshot revision={snapshot.Revision} receivedTechCount={_pendingTechnologyById.Count} techIds=[{string.Join(",", _pendingTechnologyById.Keys.OrderBy(k => k))}]");

            return FlushPendingState();
        }

        /// <summary>
        /// Stages the last server tech map for <see cref="FlushPendingState"/> via the reconciler
        /// so <see cref="PersistentSyncReconciler.FlushPendingState"/> can reassert state when
        /// KSP may have rebuilt R&D.Instance behind us (e.g. on R&D panel spawn).
        /// </summary>
        public bool TryStageReassertFromLastServerSnapshot()
        {
            if (_authoritativeTechnologyById == null || _authoritativeTechnologyById.Count == 0)
            {
                return false;
            }

            _pendingTechnologyById = new Dictionary<string, TechnologySnapshotInfo>(_authoritativeTechnologyById, StringComparer.Ordinal);
            return true;
        }

        public PersistentSyncApplyOutcome FlushPendingState()
        {
            if (_pendingTechnologyById == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (ResearchAndDevelopment.Instance == null || AssetBase.RnDTechTree == null)
            {
                LunaLog.Log($"[PersistentSync] Technology FlushPendingState deferred: R&D.Instance={(ResearchAndDevelopment.Instance != null)} RnDTechTree={(AssetBase.RnDTechTree != null)}");
                return PersistentSyncApplyOutcome.Deferred;
            }

            ShareTechnologySystem.Singleton.StartIgnoringEvents();
            int applied = 0;
            int total = 0;
            int missedInSnapshot = 0;
            try
            {
                ApplyTechnologySnapshot(_pendingTechnologyById, out applied, out total, out missedInSnapshot);
            }
            catch (Exception ex)
            {
                LunaLog.LogError($"[PersistentSync] Technology FlushPendingState exception: {ex}");
                return PersistentSyncApplyOutcome.Rejected;
            }
            finally
            {
                ShareTechnologySystem.Singleton.StopIgnoringEvents();
            }

            var postApplyAvailable = 0;
            var postApplyUnavailable = 0;
            foreach (var tech in AssetBase.RnDTechTree.GetTreeTechs().Where(node => node != null))
            {
                var state = ResearchAndDevelopment.Instance.GetTechState(tech.techID);
                if (state == null) continue;
                if (state.state == RDTech.State.Available) postApplyAvailable++;
                else postApplyUnavailable++;
            }

            LunaLog.Log($"[PersistentSync] Technology FlushPendingState treeTechs={total} snapshotHits={applied} snapshotMisses={missedInSnapshot} pendingTechCount={_pendingTechnologyById.Count} postApplyAvailable={postApplyAvailable} postApplyUnavailable={postApplyUnavailable}");

            ResearchAndDevelopmentProtoMirror.TrySyncLiveInstanceToGameProto("TechnologyFlush");

            if (RDController.Instance != null)
            {
                ShareTechnologySystem.Singleton.RefreshResearchAndDevelopmentUiAdapters("PersistentSyncSnapshotApply");
            }
            else
            {
                ShareTechnologySystem.Singleton.RefreshEditorAfterTechSnapshot("PersistentSyncSnapshotApply");
            }

            _pendingTechnologyById = null;
            return PersistentSyncApplyOutcome.Applied;
        }

        private static void ApplyTechnologySnapshot(IReadOnlyDictionary<string, TechnologySnapshotInfo> technologyById, out int applied, out int total, out int missedInSnapshot)
        {
            applied = 0;
            total = 0;
            missedInSnapshot = 0;
            var snapshotMatched = new HashSet<string>(StringComparer.Ordinal);

            foreach (var tech in AssetBase.RnDTechTree.GetTreeTechs().Where(node => node != null))
            {
                total++;
                var protoTechNode = new ProtoTechNode
                {
                    techID = tech.techID,
                    scienceCost = tech.scienceCost,
                    state = tech.state,
                    partsPurchased = new List<AvailablePart>(tech.partsPurchased ?? new List<AvailablePart>())
                };

                if (technologyById.TryGetValue(tech.techID, out var snapshot))
                {
                    ApplySnapshotInfo(protoTechNode, snapshot);
                    snapshotMatched.Add(tech.techID);
                    applied++;
                }

                ResearchAndDevelopment.Instance.SetTechState(tech.techID, protoTechNode);
            }

            foreach (var snapshotId in technologyById.Keys)
            {
                if (!snapshotMatched.Contains(snapshotId))
                {
                    missedInSnapshot++;
                    LunaLog.LogWarning($"[PersistentSync] Technology snapshot tech '{snapshotId}' not found in RnDTechTree.GetTreeTechs(); skipping");
                }
            }
        }

        private static void ApplySnapshotInfo(ProtoTechNode protoTechNode, TechnologySnapshotInfo snapshot)
        {
            // CRITICAL: the server serializes Technology Data as bare "key = value" lines with NO
            // surrounding braces (see TechnologyPersistentSyncDomainStore.BuildBareNodeText on the
            // server). KSP's brace-aware ConfigNode parser (PreFormatConfig/RecurseFormat, used by
            // ConfigNodeSerializer.DeserializeToConfigNode) returns an empty node for that format,
            // which made GetValue("state") always return null and every tech stay Unavailable.
            // Parse the bare key=value lines directly to match the server wire format.
            if (snapshot?.Data == null || snapshot.NumBytes <= 0)
            {
                LunaLog.LogWarning($"[PersistentSync] Technology snapshot for '{snapshot?.TechId}' has no data; retaining default tech state");
                return;
            }

            var text = System.Text.Encoding.UTF8.GetString(snapshot.Data, 0, snapshot.NumBytes);
            string stateValue = null;
            string costValue = null;
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();
                if (string.Equals(key, "state", StringComparison.Ordinal)) stateValue = value;
                else if (string.Equals(key, "cost", StringComparison.Ordinal)) costValue = value;
            }

            if (!string.IsNullOrEmpty(stateValue))
            {
                try
                {
                    protoTechNode.state = (RDTech.State)Enum.Parse(typeof(RDTech.State), stateValue, ignoreCase: true);
                }
                catch (Exception ex)
                {
                    LunaLog.LogWarning($"[PersistentSync] Technology snapshot for '{snapshot.TechId}' had unparseable state='{stateValue}': {ex.Message}");
                }
            }
            else
            {
                LunaLog.LogWarning($"[PersistentSync] Technology snapshot for '{snapshot.TechId}' missing 'state' value; retaining default tech state. payload='{text.Replace('\n', '|')}'");
            }

            if (int.TryParse(costValue, out var cost))
            {
                protoTechNode.scienceCost = cost;
            }
        }
    }
}
