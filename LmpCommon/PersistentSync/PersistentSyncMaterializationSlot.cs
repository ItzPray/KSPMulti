namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Deduplicated persistence targets for pushing live PersistentSync-owned state into structures
    /// KSP reloads (<see cref="PersistentSyncMaterializationDomainMap"/> maps domains here).
    /// </summary>
    public enum PersistentSyncMaterializationSlot : byte
    {
        None = 0,
        Funding = 1,
        Reputation = 2,
        StrategySystem = 3,
        ResearchAndDevelopment = 4,
        UpgradeableFacilities = 5
    }
}
