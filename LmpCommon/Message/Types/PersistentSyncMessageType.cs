namespace LmpCommon.Message.Types
{
    public enum PersistentSyncMessageType : ushort
    {
        Request = 0,
        Intent = 1,
        Snapshot = 2
    }
}
