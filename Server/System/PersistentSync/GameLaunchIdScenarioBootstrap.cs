using LunaConfigNode.CfgNode;
using Server.Properties;
using Server.System;

namespace Server.System.PersistentSync
{
    /// <summary>
    /// Ensures the KSPMP-only <c>LmpGameLaunchId</c> scenario exists in <see cref="ScenarioStoreSystem.CurrentScenarios"/>
    /// before <see cref="GameLaunchIdPersistentSyncDomainStore"/> loads (older universes predate this file).
    /// </summary>
    internal static class GameLaunchIdScenarioBootstrap
    {
        internal const string ScenarioKey = "LmpGameLaunchId";

        internal static void EnsureScenarioInStore()
        {
            lock (ScenarioStoreSystem.ConfigTreeAccessLock)
            {
                if (ScenarioStoreSystem.CurrentScenarios.ContainsKey(ScenarioKey))
                {
                    return;
                }

                ScenarioStoreSystem.CurrentScenarios[ScenarioKey] = new ConfigNode(Resources.LmpGameLaunchId);
            }
        }
    }
}
