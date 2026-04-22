using Lidgren.Network;
using LmpCommon.Message.Base;
using LmpCommon.Message.Types;

namespace LmpCommon.Message.Data.LaunchPad
{
    public class LaunchPadReserveSiteRequestMsgData : LaunchPadClientBaseMsgData
    {
        internal LaunchPadReserveSiteRequestMsgData() { }

        public override LaunchPadClientMessageType LaunchPadClientMessageType => LaunchPadClientMessageType.ReserveSite;

        public override string ClassName { get; } = nameof(LaunchPadReserveSiteRequestMsgData);

        public string SiteKey = string.Empty;

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);
            lidgrenMsg.Write(SiteKey ?? string.Empty);
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);
            SiteKey = lidgrenMsg.ReadString();
        }

        internal override int InternalGetMessageSize()
        {
            return base.InternalGetMessageSize() + (SiteKey ?? string.Empty).GetByteCount();
        }
    }
}
