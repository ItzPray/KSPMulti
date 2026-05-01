using System;

namespace LmpCommon.PersistentSync
{
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
    }
}
