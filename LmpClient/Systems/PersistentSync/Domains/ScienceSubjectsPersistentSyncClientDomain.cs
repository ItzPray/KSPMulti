using LmpClient.Events;
using LmpClient.Extensions;
using LmpClient.Systems.ShareScienceSubject;
using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using System;
using System.Collections.Generic;
using System.Linq;

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

        private bool _reverting;

        protected override void OnDomainEnabled()
        {
            GameEvents.OnScienceRecieved.Add(OnScienceReceived);
            RevertEvent.onRevertingToLaunch.Add(OnRevertingToLaunch);
            RevertEvent.onReturningToEditor.Add(OnReturningToEditor);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelWasLoadedGuiReady);
        }

        protected override void OnDomainDisabled()
        {
            GameEvents.OnScienceRecieved.Remove(OnScienceReceived);
            RevertEvent.onRevertingToLaunch.Remove(OnRevertingToLaunch);
            RevertEvent.onReturningToEditor.Remove(OnReturningToEditor);
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelWasLoadedGuiReady);

            _reverting = false;
        }

        private void OnScienceReceived(float dataAmount, ScienceSubject subject, ProtoVessel source, bool reverseEngineered)
        {
            if (IgnoreLocalEvents)
            {
                return;
            }

            var configNode = ConvertScienceSubjectToConfigNode(subject);
            if (configNode == null)
            {
                return;
            }

            var data = configNode.Serialize();
            SendLocalPayload(
                new ScienceSubjectsPayload
                {
                    Items = new[]
                    {
                        new ScienceSubjectSnapshotInfo
                        {
                            Id = subject.id,
                            Data = data
                        }
                    }
                },
                $"ScienceSubjectUpdate:{subject.id}");
            LunaLog.Log($"Science experiment \"{subject.id}\" sent as persistent sync intent");
        }

        private void OnRevertingToLaunch()
        {
            _reverting = true;
            StartIgnoringLocalEvents();
        }

        private void OnReturningToEditor(EditorFacility data)
        {
            _reverting = true;
            StartIgnoringLocalEvents();
        }

        private void OnLevelWasLoadedGuiReady(GameScenes data)
        {
            if (!_reverting)
            {
                return;
            }

            _reverting = false;
            StopIgnoringLocalEvents(true);
        }

        private static ConfigNode ConvertScienceSubjectToConfigNode(ScienceSubject subject)
        {
            var configNode = new ConfigNode();
            try
            {
                subject.Save(configNode);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[KSPMP]: Error while saving science subject: {e}");
                return null;
            }

            return configNode;
        }

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

            using (PersistentSyncDomainSuppressionScope.Begin(
                PersistentSyncEventSuppressorRegistry.Resolve(PersistentSyncDomainNames.ScienceSubjects),
                restoreOldValueOnDispose: false))
            {
                ShareScienceSubjectSystem.Singleton.ReplaceScienceSubjects(subjects, "PersistentSyncSnapshotApply");
            }

            _pendingSubjects = null;
            return PersistentSyncApplyOutcome.Applied;
        }
    }
}
