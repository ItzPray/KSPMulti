using Lidgren.Network;
using LmpCommon.Message.Base;
using LmpCommon.Message.Types;

namespace LmpCommon.Message.Data.Handshake
{
    public class HandshakeRequestMsgData : HandshakeBaseMsgData
    {
        /// <inheritdoc />
        internal HandshakeRequestMsgData() { }
        public override HandshakeMessageType HandshakeMessageType => HandshakeMessageType.Request;

        public string PlayerName;
        public string UniqueIdentifier;
        /// <summary>Canonical fork/protocol identifier for this client build family.</summary>
        public string ProtocolForkId;
        /// <summary>Exact client build string (must match server session build).</summary>
        public string ExactClientBuild;

        public override string ClassName { get; } = nameof(HandshakeRequestMsgData);

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);

            lidgrenMsg.Write(PlayerName);
            lidgrenMsg.Write(UniqueIdentifier);
            lidgrenMsg.Write(ProtocolForkId ?? string.Empty);
            lidgrenMsg.Write(ExactClientBuild ?? string.Empty);
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);

            PlayerName = lidgrenMsg.ReadString();
            UniqueIdentifier = lidgrenMsg.ReadString();
            ProtocolForkId = lidgrenMsg.ReadString();
            ExactClientBuild = lidgrenMsg.ReadString();
        }

        internal override int InternalGetMessageSize()
        {
            return base.InternalGetMessageSize() + PlayerName.GetByteCount() + UniqueIdentifier.GetByteCount() +
                   (ProtocolForkId ?? string.Empty).GetByteCount() + (ExactClientBuild ?? string.Empty).GetByteCount();
        }
    }
}
