using HarmonyLib;
using System.Linq;

namespace LmpClient.Systems.PersistentSync
{
    /// <summary>
    /// Keeps <see cref="HighLogic.CurrentGame"/> R&amp;D <see cref="ProtoScenarioModule"/> aligned with the live
    /// <see cref="ResearchAndDevelopment.Instance"/> after PersistentSync mutates tech state.
    /// When <see cref="LmpCommon.IgnoredScenarios.IgnoreReceive"/> skips the server's R&amp;D scenario blob,
    /// <c>LoadMissingScenarioDataIntoGame</c> still adds an empty R&amp;D proto — any KSP path that re-hydrates
    /// from that proto (R&amp;D complex spawn, scene edges) can rebuild wrong/placeholder tree UI until
    /// PersistentSync reasserts. Writing the authoritative in-memory state back into <c>moduleValues</c>
    /// removes that split-brain without re-enabling generic scenario receive (which could still deliver
    /// stale R&amp;D overwrites mid-session).
    /// </summary>
    internal static class ResearchAndDevelopmentProtoMirror
    {
        private const string ModuleName = "ResearchAndDevelopment";

        public static void TrySyncLiveInstanceToGameProto(string reason)
        {
            try
            {
                if (HighLogic.CurrentGame?.scenarios == null || ResearchAndDevelopment.Instance == null)
                {
                    return;
                }

                var proto = HighLogic.CurrentGame.scenarios.FirstOrDefault(s => s != null && s.moduleName == ModuleName);
                if (proto == null)
                {
                    LunaLog.LogWarning($"[PersistentSync] R&D proto mirror: no '{ModuleName}' ProtoScenarioModule (reason={reason})");
                    return;
                }

                var saved = new ConfigNode();
                ResearchAndDevelopment.Instance.Save(saved);
                Traverse.Create(proto).Field<ConfigNode>("moduleValues").Value = saved;
                LunaLog.Log($"[PersistentSync] R&D proto mirror ok reason={reason}");
            }
            catch (System.Exception ex)
            {
                LunaLog.LogError($"[PersistentSync] R&D proto mirror failed reason={reason}: {ex}");
            }
        }
    }
}
