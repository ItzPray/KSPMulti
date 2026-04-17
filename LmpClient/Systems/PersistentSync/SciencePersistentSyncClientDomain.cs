using System;
using System.Collections.Generic;
using System.Linq;
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
                    rootNode.AddNode(new ConfigNode(System.Text.Encoding.UTF8.GetString(achievement.Data, 0, achievement.NumBytes)));
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

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.PartPurchases;

        public PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot)
        {
            try
            {
                _pendingPurchases = PartPurchasesSnapshotPayloadSerializer.Deserialize(snapshot.Payload)
                    .Where(value => value != null && !string.IsNullOrEmpty(value.TechId))
                    .ToDictionary(value => value.TechId, value => value, StringComparer.Ordinal);
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            return FlushPendingState();
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

        public PersistentSyncDomainId DomainId => PersistentSyncDomainId.Technology;

        public PersistentSyncApplyOutcome ApplySnapshot(PersistentSyncBufferedSnapshot snapshot)
        {
            try
            {
                _pendingTechnologyById = TechnologySnapshotPayloadSerializer.Deserialize(snapshot.Payload, snapshot.NumBytes)
                    .Where(technology => technology != null && !string.IsNullOrEmpty(technology.TechId))
                    .ToDictionary(technology => technology.TechId, technology => technology, StringComparer.Ordinal);
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }

            return FlushPendingState();
        }

        public PersistentSyncApplyOutcome FlushPendingState()
        {
            if (_pendingTechnologyById == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (ResearchAndDevelopment.Instance == null || AssetBase.RnDTechTree == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            ShareTechnologySystem.Singleton.StartIgnoringEvents();
            try
            {
                ApplyTechnologySnapshot(_pendingTechnologyById);
            }
            catch
            {
                return PersistentSyncApplyOutcome.Rejected;
            }
            finally
            {
                ShareTechnologySystem.Singleton.StopIgnoringEvents();
            }

            ShareTechnologySystem.Singleton.RefreshResearchAndDevelopmentUiAdapters("PersistentSyncSnapshotApply");
            _pendingTechnologyById = null;
            return PersistentSyncApplyOutcome.Applied;
        }

        private static void ApplyTechnologySnapshot(IReadOnlyDictionary<string, TechnologySnapshotInfo> technologyById)
        {
            foreach (var tech in AssetBase.RnDTechTree.GetTreeTechs().Where(node => node != null))
            {
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
                }

                ResearchAndDevelopment.Instance.SetTechState(tech.techID, protoTechNode);
            }
        }

        private static void ApplySnapshotInfo(ProtoTechNode protoTechNode, TechnologySnapshotInfo snapshot)
        {
            var node = new ConfigNode(System.Text.Encoding.UTF8.GetString(snapshot.Data, 0, snapshot.NumBytes));
            var stateValue = node.GetValue("state");
            if (!string.IsNullOrEmpty(stateValue))
            {
                protoTechNode.state = (RDTech.State)Enum.Parse(typeof(RDTech.State), stateValue);
            }

            if (int.TryParse(node.GetValue("cost"), out var cost))
            {
                protoTechNode.scienceCost = cost;
            }
        }
    }
}
