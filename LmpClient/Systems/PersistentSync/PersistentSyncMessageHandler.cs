using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;
using System.Collections.Concurrent;

namespace LmpClient.Systems.PersistentSync
{
    public class PersistentSyncMessageHandler : SubSystem<PersistentSyncSystem>, IMessageHandler
    {
        public ConcurrentQueue<IServerMessageBase> IncomingMessages { get; set; } = new ConcurrentQueue<IServerMessageBase>();

        public void HandleMessage(IServerMessageBase msg)
        {
            if (!(msg.Data is PersistentSyncBaseMsgData msgData)) return;
            if (msgData.PersistentSyncMessageType != PersistentSyncMessageType.Snapshot) return;

            System.Reconciler.HandleSnapshot((PersistentSyncSnapshotMsgData)msg.Data);
        }
    }
}
