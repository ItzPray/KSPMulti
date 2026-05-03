using HarmonyLib;
using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.ShareProgress;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using System.Collections.Generic;

namespace LmpClient.Systems.ShareScienceSubject
{
    public class ShareScienceSubjectSystem : ShareProgressBaseSystem<ShareScienceSubjectSystem, ShareScienceSubjectMessageSender, ShareScienceSubjectMessageHandler>
    {
        public override string SystemName { get; } = nameof(ShareScienceSubjectSystem);

        private Dictionary<string, ScienceSubject> _lastScienceSubjects = new Dictionary<string, ScienceSubject>();

        private static Dictionary<string, ScienceSubject> _scienceSubjects;
        public Dictionary<string, ScienceSubject> ScienceSubjects
        {
            get
            {
                if (_scienceSubjects == null)
                {
                    _scienceSubjects = Traverse.Create(ResearchAndDevelopment.Instance).Field("scienceSubjects").GetValue<Dictionary<string, ScienceSubject>>();
                }

                return _scienceSubjects;
            }
        }

        protected override bool ShareSystemReady => ResearchAndDevelopment.Instance != null;

        protected override GameMode RelevantGameModes => GameMode.Career | GameMode.Science;

        protected override bool UseSessionApplicabilityInsteadOfGameModeMask => true;

        protected override bool IsShareSystemApplicableForSession()
        {
            var caps = PersistentSyncSessionCapabilitiesFactory.CreateForCurrentSession();
            return PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainNames.ScienceSubjects,
                SettingsSystem.ServerSettings.GameMode,
                in caps);
        }

        protected override void OnEnabled()
        {
            base.OnEnabled();
            // Science/recieved + revert hooks: ScienceSubjectsPersistentSyncClientDomain
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            _lastScienceSubjects.Clear();
            _scienceSubjects = null;
        }

        public override void SaveState()
        {
            base.SaveState();
            _lastScienceSubjects = ScienceSubjects;
        }

        public override void RestoreState()
        {
            base.RestoreState();
            Traverse.Create(ResearchAndDevelopment.Instance).Field("scienceSubjects").SetValue(_lastScienceSubjects);
        }

        public void ReplaceScienceSubjects(Dictionary<string, ScienceSubject> subjects, string source)
        {
            Traverse.Create(ResearchAndDevelopment.Instance).Field("scienceSubjects").SetValue(subjects);
            _scienceSubjects = subjects;
            LunaLog.Log($"Science subject snapshot applied from {source} count={subjects.Count}");
        }
    }
}
