using Contracts;
using FinePrint.Contracts.Parameters;
using UnityEngine;

namespace LmpClient.Systems.ShareContracts
{
    /// <summary>
    /// Stock sets <see cref="VesselSystemsParameter.launchID"/> from <see cref="Game.launchID"/> in
    /// <c>OnAccepted</c>. PersistentSync applies Active contracts via <see cref="Contract.Load"/> without calling
    /// <c>Accept()</c>, so the server-serialized threshold can exceed this client's
    /// <see cref="Game.launchID"/> — then every part stamped at launch fails
    /// <see cref="FinePrint.Utilities.VesselUtilities.VesselLaunchedAfterID"/> forever. Clamp down to the local
    /// game counter so "require new" matches what this session can ever satisfy.
    /// </summary>
    internal static class VesselSystemsParameterLaunchIdFix
    {
        /// <summary>
        /// For every Active contract, if any <see cref="VesselSystemsParameter"/> has
        /// <c>requireNew</c> and <c>launchID</c> &gt; <see cref="Game.launchID"/>, set it to
        /// <see cref="Game.launchID"/> (same field stock reads at accept time).
        /// </summary>
        public static void ClampRequireNewLaunchIdsToLocalGame()
        {
            if (HighLogic.CurrentGame == null || ContractSystem.Instance == null)
            {
                return;
            }

            var gameLaunchId = HighLogic.CurrentGame.launchID;

            foreach (var contract in ContractSystem.Instance.Contracts)
            {
                if (contract == null || contract.ContractState != Contract.State.Active)
                {
                    continue;
                }

                foreach (var p in contract.AllParameters)
                {
                    if (!(p is VesselSystemsParameter vsp) || !vsp.requireNew)
                    {
                        continue;
                    }

                    if (vsp.launchID <= gameLaunchId)
                    {
                        continue;
                    }

                    var before = vsp.launchID;
                    vsp.launchID = gameLaunchId;
                    var safeTitle = (contract.Title ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
                    LunaLog.Log(
                        "[PersistentSync] VesselSystemsParameter requireNew launchID clamped to local Game.launchID " +
                        $"guid={contract.ContractGuid:N} title=\"{safeTitle}\" " +
                        $"before={before} after={vsp.launchID} gameLaunchID={gameLaunchId}");
                }
            }
        }
    }
}
