using Lidgren.Network;
using LmpCommon.Message.Base;
using LmpCommon.Message.Types;

namespace LmpCommon.Message.Data.LaunchPad
{
    public class LaunchPadReserveSiteReplyMsgData : LaunchPadBaseMsgData
    {
        internal LaunchPadReserveSiteReplyMsgData() { }

        public override LmpCommon.Message.Types.LaunchPadMessageType LaunchPadMessageType => LmpCommon.Message.Types.LaunchPadMessageType.ReserveSiteReply;

        public override string ClassName { get; } = nameof(LaunchPadReserveSiteReplyMsgData);

        public bool Granted;
        public string SiteKey = string.Empty;
        public string Reason = string.Empty;

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);
            lidgrenMsg.Write(Granted);
            lidgrenMsg.Write(SiteKey ?? string.Empty);
            lidgrenMsg.Write(Reason ?? string.Empty);
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);
            Granted = lidgrenMsg.ReadBoolean();
            SiteKey = lidgrenMsg.ReadString();
            Reason = lidgrenMsg.ReadString();
        }

        internal override int InternalGetMessageSize()
        {
            return base.InternalGetMessageSize() + sizeof(bool) +
                   (SiteKey ?? string.Empty).GetByteCount() + (Reason ?? string.Empty).GetByteCount();
        }
    }
}
