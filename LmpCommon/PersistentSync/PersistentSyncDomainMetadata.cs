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

    }
}
