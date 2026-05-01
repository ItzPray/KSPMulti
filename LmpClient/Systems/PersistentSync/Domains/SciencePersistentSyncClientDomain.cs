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
    public class SciencePersistentSyncClientDomain : SyncClientDomain<float>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesClientDomain<SciencePersistentSyncClientDomain>();
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
}
