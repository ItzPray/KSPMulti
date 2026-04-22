using Lidgren.Network;
using LmpCommon.Message.Base;
using LmpCommon.Message.Types;
using System;
using System.Collections.Generic;

namespace LmpCommon.Message.Data.LaunchPad
{
    public class LaunchPadOccupancySnapshotMsgData : LaunchPadBaseMsgData
    {
        internal LaunchPadOccupancySnapshotMsgData() { }

        public override LmpCommon.Message.Types.LaunchPadMessageType LaunchPadMessageType => LmpCommon.Message.Types.LaunchPadMessageType.OccupancySnapshot;

        public override string ClassName { get; } = nameof(LaunchPadOccupancySnapshotMsgData);

        /// <summary>When true, clients should use <see cref="EffectiveSafetyBubbleDistance"/> for bubble checks.</summary>
        public bool OverflowBubbleActive;

        public float EffectiveSafetyBubbleDistance;

        public readonly List<LaunchPadOccupancyEntry> Entries = new List<LaunchPadOccupancyEntry>();

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);
            lidgrenMsg.Write(OverflowBubbleActive);
            lidgrenMsg.Write(EffectiveSafetyBubbleDistance);
            lidgrenMsg.Write(Entries.Count);
            for (var i = 0; i < Entries.Count; i++)
            {
                lidgrenMsg.Write(Entries[i].SiteKey ?? string.Empty);
                lidgrenMsg.Write(Entries[i].PlayerName ?? string.Empty);
                GuidUtil.Serialize(Entries[i].VesselId, lidgrenMsg);
            }
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);
            OverflowBubbleActive = lidgrenMsg.ReadBoolean();
            EffectiveSafetyBubbleDistance = lidgrenMsg.ReadFloat();
            var count = lidgrenMsg.ReadInt32();
            Entries.Clear();
            for (var i = 0; i < count; i++)
            {
                Entries.Add(new LaunchPadOccupancyEntry
                {
                    SiteKey = lidgrenMsg.ReadString(),
                    PlayerName = lidgrenMsg.ReadString(),
                    VesselId = GuidUtil.Deserialize(lidgrenMsg)
                });
            }
        }

        internal override int InternalGetMessageSize()
        {
            var n = base.InternalGetMessageSize() + sizeof(bool) + sizeof(float) + sizeof(int);
            for (var i = 0; i < Entries.Count; i++)
            {
                n += Entries[i].SiteKey.GetByteCount();
                n += Entries[i].PlayerName.GetByteCount();
                n += GuidUtil.ByteSize;
            }

            return n;
        }
    }

    public struct LaunchPadOccupancyEntry
    {
        public string SiteKey;
        public string PlayerName;
        public Guid VesselId;
    }
}
