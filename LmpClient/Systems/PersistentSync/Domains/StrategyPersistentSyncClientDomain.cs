using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
using System;
using System.Collections.Generic;
using System.Linq;
using KSP.UI.Screens;
using LmpClient.Extensions;
using LmpClient.Systems.ShareAchievements;
using LmpClient.Systems.ShareExperimentalParts;
using LmpClient.Systems.SharePurchaseParts;
using LmpClient.Systems.ShareScience;
using LmpClient.Systems.ShareContracts;
using LmpClient.Systems.ShareScienceSubject;
using LmpClient.Systems.ShareStrategy;
using LmpClient.Systems.ShareTechnology;
using LmpCommon.PersistentSync;
using Strategies;

namespace LmpClient.Systems.PersistentSync
{
    [PersistentSyncStockScenario("StrategySystem")]
    public class StrategyPersistentSyncClientDomain : SyncClientDomain<StrategyPayload>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesClientDomain<StrategyPersistentSyncClientDomain>();
        }

        private Dictionary<string, StrategySnapshotInfo> _pendingStrategies;

        protected override void OnPayloadBuffered(PersistentSyncBufferedSnapshot snapshot, StrategyPayload payload)
        {
            var items = payload?.Items ?? Array.Empty<StrategySnapshotInfo>();
            _pendingStrategies = items
                .Where(strategy => strategy != null && !string.IsNullOrEmpty(strategy.Name))
                .ToDictionary(strategy => strategy.Name, strategy => strategy, StringComparer.Ordinal);
        }

        public override PersistentSyncApplyOutcome FlushPendingState()
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
}
