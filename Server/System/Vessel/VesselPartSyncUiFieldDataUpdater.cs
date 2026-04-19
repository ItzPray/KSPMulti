using LmpCommon.Enums;
using LmpCommon.Message.Data.Vessel;
using LunaConfigNode.CfgNode;
using Server.Log;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Server.System.Vessel
{
    /// <summary>
    /// We try to avoid working with protovessels as much as possible as they can be huge files.
    /// This class patches the vessel file with the information messages we receive about a position and other vessel properties.
    /// This way we send the whole vessel definition only when there are parts that have changed 
    /// </summary>
    public partial class VesselDataUpdater
    {
        /// <summary>
        /// We received a part module change from a player
        /// Then we rewrite the vesselproto with that last information so players that connect later receive an updated vesselproto
        /// </summary>
        public static void WritePartSyncUiFieldDataToFile(VesselBaseMsgData message)
        {
            if (!(message is VesselPartSyncUiFieldMsgData msgData)) return;
            if (VesselContext.RemovedVessels.Contains(msgData.VesselId)) return;

            //Sync part changes ALWAYS and ignore the rate they arrive
            Task.Run(() =>
            {
                try
                {
                    lock (Semaphore.GetOrAdd(msgData.VesselId, new object()))
                    {
                        if (!VesselStoreSystem.CurrentVessels.TryGetValue(msgData.VesselId, out var vessel)) return;

                        UpdateProtoVesselFileWithNewPartSyncUiFieldData(vessel, msgData);
                    }
                }
                catch (Exception ex)
                {
                    LunaLog.Error($"[VesselPartSyncUiField] vessel={msgData.VesselId} part={msgData.PartFlightId} module={msgData.ModuleName} field={msgData.FieldName}: {ex}");
                }
            });
        }

        /// <summary>
        /// Updates the proto vessel with the values we received about a part module change
        /// </summary>
        private static void UpdateProtoVesselFileWithNewPartSyncUiFieldData(Classes.Vessel vessel, VesselPartSyncUiFieldMsgData msgData)
        {
            var part = vessel.GetPart(msgData.PartFlightId);
            if (part == null)
            {
                return;
            }

            var modules = part.GetModulesNamed(msgData.ModuleName).ToList();
            if (modules.Count == 0)
            {
                return;
            }

            foreach (var module in modules)
            {
                ApplyPartSyncUiFieldToModule(module, msgData);
            }
        }

        private static void ApplyPartSyncUiFieldToModule(ConfigNode module, VesselPartSyncUiFieldMsgData msgData)
        {
            switch (msgData.FieldType)
            {
                case PartSyncFieldType.Boolean:
                    module.UpdateValue(msgData.FieldName, msgData.BoolValue.ToString(CultureInfo.InvariantCulture));
                    break;
                case PartSyncFieldType.Integer:
                    module.UpdateValue(msgData.FieldName, msgData.IntValue.ToString(CultureInfo.InvariantCulture));
                    break;
                case PartSyncFieldType.Float:
                    module.UpdateValue(msgData.FieldName, msgData.FloatValue.ToString(CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
