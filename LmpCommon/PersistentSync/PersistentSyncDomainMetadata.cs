using System;

namespace LmpCommon.PersistentSync
{
    public static class PersistentSyncDomainNames
    {
        public const string Funds = "Funds";
        public const string Science = "Science";
        public const string Reputation = "Reputation";
        public const string UpgradeableFacilities = "UpgradeableFacilities";
        public const string Contracts = "Contracts";
        public const string Technology = "Technology";
        public const string Strategy = "Strategy";
        public const string Achievements = "Achievements";
        public const string ScienceSubjects = "ScienceSubjects";
        public const string ExperimentalParts = "ExperimentalParts";
        public const string PartPurchases = "PartPurchases";
        public const string GameLaunchId = "GameLaunchId";
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class PersistentSyncDomainNameAttribute : Attribute
    {
        public PersistentSyncDomainNameAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class PersistentSyncStockScenarioAttribute : Attribute
    {
        public PersistentSyncStockScenarioAttribute(string scenarioName)
        {
            ScenarioName = scenarioName;
        }

        public string ScenarioName { get; }
        public string ScalarField { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class PersistentSyncOwnedScenarioAttribute : Attribute
    {
        public PersistentSyncOwnedScenarioAttribute(string scenarioName)
        {
            ScenarioName = scenarioName;
        }

        public string ScenarioName { get; }
        public string ScalarField { get; set; }
    }

    public static class PersistentSyncDomainNaming
    {
        public static string InferDomainName(Type domainType)
        {
            if (domainType == null) throw new ArgumentNullException(nameof(domainType));

            var overrideName = (PersistentSyncDomainNameAttribute)Attribute.GetCustomAttribute(
                domainType,
                typeof(PersistentSyncDomainNameAttribute));
            if (!string.IsNullOrEmpty(overrideName?.Name))
            {
                return overrideName.Name;
            }

            var name = domainType.Name;
            foreach (var suffix in new[]
            {
                "PersistentSyncDomainStore",
                "PersistentSyncClientDomain",
                "SyncDomainStore",
                "SyncClientDomain"
            })
            {
                if (name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return name.Substring(0, name.Length - suffix.Length);
                }
            }

            return name;
        }

        public static ushort GetKnownWireId(string domainName)
        {
            switch (domainName)
            {
                case "Funds": return 0;
                case "Science": return 1;
                case "Reputation": return 2;
                case "UpgradeableFacilities": return 3;
                case "Contracts": return 4;
                case "Technology": return 5;
                case "Strategy": return 6;
                case "Achievements": return 7;
                case "ScienceSubjects": return 8;
                case "ExperimentalParts": return 9;
                case "PartPurchases": return 10;
                case "GameLaunchId": return 11;
                default: return 0;
            }
        }

        public static bool TryGetKnownName(ushort wireId, out string domainName)
        {
            switch (wireId)
            {
                case 0: domainName = PersistentSyncDomainNames.Funds; return true;
                case 1: domainName = PersistentSyncDomainNames.Science; return true;
                case 2: domainName = PersistentSyncDomainNames.Reputation; return true;
                case 3: domainName = PersistentSyncDomainNames.UpgradeableFacilities; return true;
                case 4: domainName = PersistentSyncDomainNames.Contracts; return true;
                case 5: domainName = PersistentSyncDomainNames.Technology; return true;
                case 6: domainName = PersistentSyncDomainNames.Strategy; return true;
                case 7: domainName = PersistentSyncDomainNames.Achievements; return true;
                case 8: domainName = PersistentSyncDomainNames.ScienceSubjects; return true;
                case 9: domainName = PersistentSyncDomainNames.ExperimentalParts; return true;
                case 10: domainName = PersistentSyncDomainNames.PartPurchases; return true;
                case 11: domainName = PersistentSyncDomainNames.GameLaunchId; return true;
                default: domainName = null; return false;
            }
        }
    }
}
