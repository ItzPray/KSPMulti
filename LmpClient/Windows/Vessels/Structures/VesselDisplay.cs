using LmpClient.VesselUtilities;
using System;
using System.Linq;
using UnityEngine;

namespace LmpClient.Windows.Vessels.Structures
{
    internal class VesselDisplay : VesselBaseDisplay
    {
        public override bool Display { get; set; }
        public Guid VesselId { get; set; }

        /// <summary>Min/max <see cref="Part.launchID"/> on parts (not contract VSP threshold; see clamp log / D7).</summary>
        public string PartLaunchIdSummary { get; private set; } = string.Empty;

        public string VesselName { get; set; }
        public VesselDataDisplay Data { get; set; }
        public VesselLockDisplay Locks { get; set; }
        public VesselOrbitDisplay Orbit { get; set; }
        public VesselInterpolationDisplay Interpolation { get; set; }
        public VesselPositionDisplay Position { get; set; }
        public VesselVectorsDisplay Vectors { get; set; }

        public VesselDisplay(Guid vesselId)
        {
            VesselId = vesselId;
            PartLaunchIdSummary = string.Empty;
            Data = new VesselDataDisplay(VesselId);
            Locks = new VesselLockDisplay(VesselId);
            Orbit = new VesselOrbitDisplay(VesselId);
            Interpolation = new VesselInterpolationDisplay(VesselId);
            Position = new VesselPositionDisplay(VesselId);
            Vectors = new VesselVectorsDisplay(VesselId);
        }

        /// <summary>
        /// Updates header fields used on the collapsed vessel row (always call from <see cref="Vessels.VesselsWindow"/>;
        /// <see cref="VesselBaseDisplay.Update"/> skips work when the row is collapsed).
        /// </summary>
        public void RefreshHeader(Vessel vessel)
        {
            if (vessel == null)
            {
                return;
            }

            VesselId = vessel.id;
            PartLaunchIdSummary = BuildPartLaunchIdSummary(vessel);
        }

        private static string BuildPartLaunchIdSummary(Vessel vessel)
        {
            if (vessel.loaded && vessel.Parts != null && vessel.Parts.Count > 0)
            {
                uint min = uint.MaxValue;
                uint max = 0u;
                foreach (var p in vessel.Parts)
                {
                    if (p == null)
                    {
                        continue;
                    }

                    var id = p.launchID;
                    if (id < min)
                    {
                        min = id;
                    }

                    if (id > max)
                    {
                        max = id;
                    }
                }

                if (min == uint.MaxValue)
                {
                    return string.Empty;
                }

                return min == max ? $"  partsLaunchID={min}" : $"  partsLaunchID={min}-{max}";
            }

            var snaps = vessel.protoVessel?.protoPartSnapshots;
            if (snaps == null || snaps.Count == 0)
            {
                return string.Empty;
            }

            var ids = snaps.Where(s => s != null).Select(s => s.launchID).ToArray();
            if (ids.Length == 0)
            {
                return string.Empty;
            }

            var pmin = ids.Min();
            var pmax = ids.Max();
            return pmin == pmax ? $"  partsLaunchID={pmin}" : $"  partsLaunchID={pmin}-{pmax}";
        }

        protected override void UpdateDisplay(Vessel vessel)
        {
            VesselId = vessel.id;
            VesselName = vessel.vesselName;
            PartLaunchIdSummary = BuildPartLaunchIdSummary(vessel);
            Data.Update(vessel);
            Locks.Update(vessel);
            Orbit.Update(vessel);
            Interpolation.Update(vessel);
            Position.Update(vessel);
            Vectors.Update(vessel);
        }

        protected override void PrintDisplay()
        {
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label(VesselName);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reload"))
            {
                var vessel = FlightGlobals.FindVessel(VesselId);

                //Do not call BackupVessel() as that would overwrite the proto with the actual vessel values and mess up part syncs
                VesselLoader.LoadVessel(vessel.protoVessel, true);
            }
            GUILayout.EndHorizontal();

            Data.Display = GUILayout.Toggle(Data.Display, nameof(Data), ButtonStyle);
            Data.Print();
            Locks.Display = GUILayout.Toggle(Locks.Display, nameof(Locks), ButtonStyle);
            Locks.Print();
            Orbit.Display = GUILayout.Toggle(Orbit.Display, nameof(Orbit), ButtonStyle);
            Orbit.Print();
            Interpolation.Display = GUILayout.Toggle(Interpolation.Display, nameof(Interpolation), ButtonStyle);
            Interpolation.Print();
            Position.Display = GUILayout.Toggle(Position.Display, nameof(Position), ButtonStyle);
            Position.Print();
            Vectors.Display = GUILayout.Toggle(Vectors.Display, nameof(Vectors), ButtonStyle);
            Vectors.Print();
            GUILayout.EndVertical();
        }
    }
}
