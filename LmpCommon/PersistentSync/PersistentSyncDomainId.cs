namespace LmpCommon.PersistentSync
{
    public enum PersistentSyncDomainId : byte
    {
        Funds = 0,
        Science = 1,
        Reputation = 2,
        UpgradeableFacilities = 3,
        Contracts = 4,
        Technology = 5,
        Strategy = 6,
        Achievements = 7,
        ScienceSubjects = 8,
        ExperimentalParts = 9,
        PartPurchases = 10,
        /// <summary>
        /// Authoritative <c>Game.launchID</c> high-water mark (KSPMP-owned scenario <c>LmpGameLaunchId</c> on server).
        /// Not part of the mandatory initial-sync domain list: the client requests it once after the join handshake
        /// so older servers without this domain remain join-compatible.
        /// </summary>
        GameLaunchId = 11
    }
}
