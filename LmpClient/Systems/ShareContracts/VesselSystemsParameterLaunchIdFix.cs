using Contracts;
using FinePrint.Contracts.Parameters;
using UnityEngine;

namespace LmpClient.Systems.ShareContracts
{
    /// <summary>
    /// <para>
    /// Stock sets <see cref="VesselSystemsParameter.launchID"/> from <see cref="Game.launchID"/> in
    /// <c>OnAccepted</c>. PersistentSync applies Active contracts via <see cref="Contract.Load"/> without calling
    /// <c>Accept()</c>, so the server-serialized threshold can exceed this client's
    /// <see cref="Game.launchID"/> — then every part stamped at launch fails
    /// <see cref="FinePrint.Utilities.VesselUtilities.VesselLaunchedAfterID"/> forever.
    /// </para>
    /// <para>
    /// <b>Root cause:</b> <see cref="MainSystem.StartGameNow"/> builds <see cref="HighLogic.CurrentGame"/> with
    /// <see cref="MainSystem.CreateBlankGame"/> (default <c>launchID = 1</c>). Scenario sync sends scenario modules, not
    /// the full <c>GAME</c> node from <c>persistent.sfs</c>, so the global launch counter is never aligned with the
    /// server universe until stock advances it locally. Contract rows from PersistentSync still carry the server's
    /// <c>VesselSystemsParameter.launchID</c>.
    /// </para>
    /// <para>
    /// <b>Mitigations:</b> Authoritative <see cref="Game.launchID"/> is synced via PersistentSync
    /// <see cref="LmpCommon.PersistentSync.PersistentSyncDomainId.GameLaunchId"/> (server <c>LmpGameLaunchId</c> scenario),
    /// requested once after the mandatory persistent-sync handshake so older servers stay join-compatible.
    /// (1) <see cref="ClampRequireNewLaunchIdsToLocalGame"/> and (2)
    /// <see cref="AdvanceGameLaunchIdIfBelowMaxProtoPartLaunchIdAcrossVessels"/> remain as safety nets for edge races
    /// or older servers.
    /// </para>
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

        /// <summary>
        /// Raises <see cref="Game.launchID"/> to the maximum <c>launchID</c> found on any vessel's
        /// <see cref="ProtoPartSnapshot"/> or loaded <see cref="Part"/> so the global counter matches the universe
        /// already present after scenario/vessel sync (typically from the server).
        /// Call after <see cref="ClampRequireNewLaunchIdsToLocalGame"/>.
        /// </summary>
        public static void AdvanceGameLaunchIdIfBelowMaxProtoPartLaunchIdAcrossVessels()
        {
            if (HighLogic.CurrentGame == null || FlightGlobals.fetch == null || FlightGlobals.Vessels == null)
            {
                return;
            }

            var maxPart = 0u;
            foreach (var v in FlightGlobals.Vessels)
            {
                if (v == null)
                {
                    continue;
                }

                if (v.loaded && v.Parts != null)
                {
                    foreach (var p in v.Parts)
                    {
                        if (p != null && p.launchID > maxPart)
                        {
                            maxPart = p.launchID;
                        }
                    }
                }

                var snaps = v.protoVessel?.protoPartSnapshots;
                if (snaps == null)
                {
                    continue;
                }

                for (var i = 0; i < snaps.Count; i++)
                {
                    var s = snaps[i];
                    if (s != null && s.launchID > maxPart)
                    {
                        maxPart = s.launchID;
                    }
                }
            }

            if (maxPart == 0u)
            {
                return;
            }

            var cur = HighLogic.CurrentGame.launchID;
            if (maxPart <= cur)
            {
                return;
            }

            LunaLog.Log(
                "[PersistentSync] Game.launchID advanced to match max part/proto launchID seen on FlightGlobals.Vessels " +
                $"before={cur} after={maxPart}");
            HighLogic.CurrentGame.launchID = maxPart;
        }
    }
}
