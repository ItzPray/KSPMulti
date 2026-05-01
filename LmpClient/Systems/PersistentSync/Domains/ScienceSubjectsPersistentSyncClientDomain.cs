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
    public class ScienceSubjectsPersistentSyncClientDomain : SyncClientDomain<ScienceSubjectsPayload>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesClientDomain<ScienceSubjectsPersistentSyncClientDomain>();
        }

        private ScienceSubjectSnapshotInfo[] _pendingSubjects;

        protected override void OnPayloadBuffered(PersistentSyncBufferedSnapshot snapshot, ScienceSubjectsPayload payload)
        {
            _pendingSubjects = payload?.Items ?? Array.Empty<ScienceSubjectSnapshotInfo>();
        }

        public override PersistentSyncApplyOutcome FlushPendingState()
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
                foreach (var snapshot in _pendingSubjects.Where(value => value != null && value.Data.Length > 0))
                {
                    var subject = ShareScienceSubjectMessageHandler.ConvertByteArrayToScienceSubject(snapshot.Data, snapshot.Data.Length);
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
}
