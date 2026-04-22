using Lidgren.Network;
using LmpCommon.Message.Base;
using LmpCommon.Message.Types;

namespace LmpCommon.Message.Data.LaunchPad
{
    public class LaunchPadLaunchDeniedMsgData : LaunchPadBaseMsgData
    {
        internal LaunchPadLaunchDeniedMsgData() { }

        public override LmpCommon.Message.Types.LaunchPadMessageType LaunchPadMessageType => LmpCommon.Message.Types.LaunchPadMessageType.LaunchDenied;

        public override string ClassName { get; } = nameof(LaunchPadLaunchDeniedMsgData);

        public string Reason;

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);
            lidgrenMsg.Write(Reason ?? string.Empty);
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);
            Reason = lidgrenMsg.ReadString();
        }

        internal override int InternalGetMessageSize()
        {
            return base.InternalGetMessageSize() + Reason.GetByteCount();
        }
    }
}
