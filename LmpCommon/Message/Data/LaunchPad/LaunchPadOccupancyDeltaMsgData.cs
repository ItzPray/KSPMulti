using Lidgren.Network;
using LmpCommon.Message.Base;
using LmpCommon.Message.Types;
using System.Collections.Generic;

namespace LmpCommon.Message.Data.LaunchPad
{
    /// <summary>
    /// Incremental occupancy update. Kind 0 = upsert entry, 1 = remove by vessel id.
    /// </summary>
    public class LaunchPadOccupancyDeltaMsgData : LaunchPadBaseMsgData
    {
        internal LaunchPadOccupancyDeltaMsgData() { }

        public override LmpCommon.Message.Types.LaunchPadMessageType LaunchPadMessageType => LmpCommon.Message.Types.LaunchPadMessageType.OccupancyDelta;

        public override string ClassName { get; } = nameof(LaunchPadOccupancyDeltaMsgData);

        public bool OverflowBubbleActive;
        public float EffectiveSafetyBubbleDistance;

        public readonly List<LaunchPadDeltaOperation> Operations = new List<LaunchPadDeltaOperation>();

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);
            lidgrenMsg.Write(OverflowBubbleActive);
            lidgrenMsg.Write(EffectiveSafetyBubbleDistance);
            lidgrenMsg.Write(Operations.Count);
            for (var i = 0; i < Operations.Count; i++)
            {
                lidgrenMsg.Write(Operations[i].Kind);
                if (Operations[i].Kind == 0)
                {
                    lidgrenMsg.Write(Operations[i].Entry.SiteKey ?? string.Empty);
                    lidgrenMsg.Write(Operations[i].Entry.PlayerName ?? string.Empty);
                    GuidUtil.Serialize(Operations[i].Entry.VesselId, lidgrenMsg);
                }
                else
                    GuidUtil.Serialize(Operations[i].RemoveVesselId, lidgrenMsg);
            }
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);
            OverflowBubbleActive = lidgrenMsg.ReadBoolean();
            EffectiveSafetyBubbleDistance = lidgrenMsg.ReadFloat();
            var n = lidgrenMsg.ReadInt32();
            Operations.Clear();
            for (var i = 0; i < n; i++)
            {
                var kind = lidgrenMsg.ReadByte();
                if (kind == 0)
                {
                    Operations.Add(new LaunchPadDeltaOperation
                    {
                        Kind = 0,
                        Entry = new LaunchPadOccupancyEntry
                        {
                            SiteKey = lidgrenMsg.ReadString(),
                            PlayerName = lidgrenMsg.ReadString(),
                            VesselId = GuidUtil.Deserialize(lidgrenMsg)
                        }
                    });
                }
                else
                {
                    Operations.Add(new LaunchPadDeltaOperation
                    {
                        Kind = 1,
                        RemoveVesselId = GuidUtil.Deserialize(lidgrenMsg)
                    });
                }
            }
        }

        internal override int InternalGetMessageSize()
        {
            var s = base.InternalGetMessageSize() + sizeof(bool) + sizeof(float) + sizeof(int);
            for (var i = 0; i < Operations.Count; i++)
            {
                s += sizeof(byte);
                if (Operations[i].Kind == 0)
                {
                    s += (Operations[i].Entry.SiteKey ?? string.Empty).GetByteCount();
                    s += (Operations[i].Entry.PlayerName ?? string.Empty).GetByteCount();
                    s += GuidUtil.ByteSize;
                }
                else
                    s += GuidUtil.ByteSize;
            }

            return s;
        }
    }

    public struct LaunchPadDeltaOperation
    {
        /// <summary>0 = upsert <see cref="Entry"/>, 1 = remove by <see cref="RemoveVesselId"/>.</summary>
        public byte Kind;
        public LaunchPadOccupancyEntry Entry;
        public System.Guid RemoveVesselId;
    }
}
