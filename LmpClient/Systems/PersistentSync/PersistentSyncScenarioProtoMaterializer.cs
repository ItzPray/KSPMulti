using HarmonyLib;
using System.Linq;

namespace LmpClient.Systems.PersistentSync
{
    /// <summary>
    /// Writes a live <see cref="ScenarioModule"/> snapshot into the matching
    /// <see cref="ProtoScenarioModule"/> <c>moduleValues</c> so stock save/load and scene reload paths
    /// read the same state PersistentSync just applied.
    /// </summary>
    internal static class PersistentSyncScenarioProtoMaterializer
    {
        public static void TryMirrorScenarioModule(ScenarioModule instance, string moduleName, string reason)
        {
            try
            {
                if (instance == null || string.IsNullOrEmpty(moduleName))
                {
                    return;
                }

                if (HighLogic.CurrentGame?.scenarios == null)
                {
                    return;
                }

                var proto = HighLogic.CurrentGame.scenarios.FirstOrDefault(s => s != null && s.moduleName == moduleName);
                if (proto == null)
                {
                    LunaLog.LogWarning($"[PersistentSync] scenario proto mirror: no ProtoScenarioModule '{moduleName}' (reason={reason})");
                    return;
                }

                var saved = new ConfigNode();
                instance.Save(saved);
                Traverse.Create(proto).Field<ConfigNode>("moduleValues").Value = saved;
                LunaLog.Log($"[PersistentSync] scenario proto mirror ok module={moduleName} reason={reason}");
            }
            catch (System.Exception ex)
            {
                LunaLog.LogError($"[PersistentSync] scenario proto mirror failed module={moduleName} reason={reason}: {ex}");
            }
        }
    }
}
