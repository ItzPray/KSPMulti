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
            switch (domainId)
            {
                case PersistentSyncDomainId.Funds:
                    return PersistentSyncMaterializationSlot.Funding;
                case PersistentSyncDomainId.Reputation:
                    return PersistentSyncMaterializationSlot.Reputation;
                case PersistentSyncDomainId.Strategy:
                    return PersistentSyncMaterializationSlot.StrategySystem;
                case PersistentSyncDomainId.Science:
                case PersistentSyncDomainId.Technology:
                case PersistentSyncDomainId.ExperimentalParts:
                case PersistentSyncDomainId.PartPurchases:
                case PersistentSyncDomainId.ScienceSubjects:
                    return PersistentSyncMaterializationSlot.ResearchAndDevelopment;
                case PersistentSyncDomainId.UpgradeableFacilities:
                    return PersistentSyncMaterializationSlot.UpgradeableFacilities;
                case PersistentSyncDomainId.Contracts:
                case PersistentSyncDomainId.Achievements:
                default:
                    return PersistentSyncMaterializationSlot.None;
            }
        }
    }
}
