using Lidgren.Network;
using LmpCommon.Message.Base;
using LmpCommon.Message.Types;

namespace LmpCommon.Message.Data.LaunchPad
{
    public abstract class LaunchPadClientBaseMsgData : MessageData
    {
        internal LaunchPadClientBaseMsgData() { }

        public override ushort SubType => (ushort)(int)this.LaunchPadClientMessageType;

        public virtual LaunchPadClientMessageType LaunchPadClientMessageType => throw new System.NotImplementedException();

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg) { }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg) { }

        internal override int InternalGetMessageSize() => 0;
    }
}
