using HarmonyLib;
using System;
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
                if (string.Equals(moduleName, "ContractSystem", StringComparison.Ordinal))
                {
                    try
                    {
                        // ContractSystem.OnSave nests offers under gameNode.AddNode("CONTRACTS"); a root-level
                        // GetNodes("CONTRACT") therefore always returns 0 and was misleading past diagnostics.
                        var contractsContainer = saved.GetNode("CONTRACTS");
                        var offerNodes = contractsContainer?.GetNodes("CONTRACT");
                        var finishedNodes = contractsContainer?.GetNodes("CONTRACT_FINISHED");
                        LunaLog.Log(
                            $"[PersistentSync] scenario proto mirror ok module={moduleName} reason={reason} " +
                            $"CONTRACT_nodes={offerNodes?.Length ?? 0} " +
                            $"CONTRACT_FINISHED_nodes={finishedNodes?.Length ?? 0}");
                    }
                    catch (System.Exception shapeEx)
                    {
                        LunaLog.LogWarning(
                            $"[PersistentSync] scenario proto mirror ok module={moduleName} reason={reason} " +
                            $"(CONTRACT_nodes log failed: {shapeEx.Message})");
                    }
                }
                else
                {
                    LunaLog.Log($"[PersistentSync] scenario proto mirror ok module={moduleName} reason={reason}");
                }
            }
            catch (System.Exception ex)
            {
                LunaLog.LogError($"[PersistentSync] scenario proto mirror failed module={moduleName} reason={reason}: {ex}");
            }
        }
    }
}
