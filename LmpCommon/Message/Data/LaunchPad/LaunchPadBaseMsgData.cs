using Lidgren.Network;
using LmpCommon.Message.Base;
using LmpCommon.Message.Types;

namespace LmpCommon.Message.Data.LaunchPad
{
    public abstract class LaunchPadBaseMsgData : MessageData
    {
        internal LaunchPadBaseMsgData() { }

        public override ushort SubType => (ushort)(int)this.LaunchPadMessageType;

        public virtual LmpCommon.Message.Types.LaunchPadMessageType LaunchPadMessageType => throw new System.NotImplementedException();

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg) { }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg) { }

        internal override int InternalGetMessageSize() => 0;
    }
}
