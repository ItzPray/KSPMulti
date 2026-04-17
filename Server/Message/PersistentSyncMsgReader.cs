using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;
using Server.Client;
using Server.Message.Base;
using Server.System.PersistentSync;

namespace Server.Message
{
    public class PersistentSyncMsgReader : ReaderBase
    {
        public override void HandleMessage(ClientStructure client, IClientMessageBase message)
        {
            var data = message.Data as PersistentSyncBaseMsgData;
            switch (data?.PersistentSyncMessageType)
            {
                case PersistentSyncMessageType.Request:
                    PersistentSyncRegistry.HandleRequest(client, (PersistentSyncRequestMsgData)data);
                    break;
                case PersistentSyncMessageType.Intent:
                    PersistentSyncRegistry.HandleIntent(client, (PersistentSyncIntentMsgData)data);
                    break;
            }

            message.Recycle();
        }
    }
}
