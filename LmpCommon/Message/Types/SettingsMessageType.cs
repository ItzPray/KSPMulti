namespace LmpCommon.Message.Types
{
    public enum SettingsMessageType
    {
        Request = 0,
        Reply = 1,
        /// <summary>Server authoritative persistent-sync catalog (session wire map). Sent after <see cref="Reply"/> on settings sync.</summary>
        PersistentSyncCatalog = 2
    }
}
