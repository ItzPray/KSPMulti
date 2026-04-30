namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Maps each <see cref="PersistentSyncDomainId"/> to a <see cref="PersistentSyncMaterializationSlot"/>
    /// so one reconciler flush can materialize each stock persistence target at most once.
    /// </summary>
    public static class PersistentSyncMaterializationDomainMap
    {
        public static PersistentSyncMaterializationSlot GetSlot(PersistentSyncDomainId domainId)
        {
            return PersistentSyncDomainCatalog.Get(domainId).MaterializationSlot;
        }
    }
}
