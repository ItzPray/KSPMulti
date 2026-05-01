using LmpCommon.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Collects fluent per-domain registration specs, validates them, and emits ordered <see cref="PersistentSyncDomainDefinition"/> rows for <see cref="PersistentSyncDomainCatalog.Configure"/>.
    /// </summary>
    public class PersistentSyncDomainRegistrar
    {
        private readonly List<PersistentSyncDomainRegistrationBuilder> _builders =
            new List<PersistentSyncDomainRegistrationBuilder>();

        private Type _currentDomainType;

        public PersistentSyncDomainRegistrationBuilder Register(PersistentSyncDomainKey key)
        {
            var builder = new PersistentSyncDomainRegistrationBuilder(key, _currentDomainType);
            _builders.Add(builder);
            return builder;
        }

        public PersistentSyncDomainRegistrationBuilder RegisterCurrent()
        {
            if (_currentDomainType == null)
            {
                throw new InvalidOperationException("RegisterCurrent requires a current persistent sync domain type.");
            }

            var domainName = PersistentSyncDomainNaming.InferDomainName(_currentDomainType);
            var key = new PersistentSyncDomainKey(domainName, PersistentSyncDomainNaming.GetKnownWireId(domainName));
            var builder = Register(key);
            var stockScenario = (PersistentSyncStockScenarioAttribute)Attribute.GetCustomAttribute(
                _currentDomainType,
                typeof(PersistentSyncStockScenarioAttribute));
            if (stockScenario != null)
            {
                builder.OwnsStockScenario(stockScenario.ScenarioName, stockScenario.ScalarField);
            }

            var ownedScenario = (PersistentSyncOwnedScenarioAttribute)Attribute.GetCustomAttribute(
                _currentDomainType,
                typeof(PersistentSyncOwnedScenarioAttribute));
            if (ownedScenario != null)
            {
                builder.OwnsKspmpScenario(ownedScenario.ScenarioName, ownedScenario.ScalarField);
            }
            else if (stockScenario == null && TryGetKnownScenarioForDomain(key.Name, out var knownScenario))
            {
                builder.OwnsStockScenario(knownScenario);
            }

            return builder;
        }

        private static bool TryGetKnownScenarioForDomain(string domainName, out string scenarioName)
        {
            switch (domainName)
            {
                case "Funds":
                    scenarioName = "Funding";
                    return true;
                case "Science":
                case "ScienceSubjects":
                case "ExperimentalParts":
                case "PartPurchases":
                case "Technology":
                    scenarioName = "ResearchAndDevelopment";
                    return true;
                case "Reputation":
                    scenarioName = "Reputation";
                    return true;
                case "UpgradeableFacilities":
                    scenarioName = "ScenarioUpgradeableFacilities";
                    return true;
                case "Contracts":
                    scenarioName = "ContractSystem";
                    return true;
                case "Strategy":
                    scenarioName = "StrategySystem";
                    return true;
                case "Achievements":
                    scenarioName = "ProgressTracking";
                    return true;
                case "GameLaunchId":
                    scenarioName = "LmpGameLaunchId";
                    return true;
                default:
                    scenarioName = null;
                    return false;
            }
        }


        public IReadOnlyList<PersistentSyncDomainDefinition> BuildDefinitions()
        {
            var definitions = _builders.Select((b, index) => b.Build((ushort)index)).ToArray();
            Validate(definitions);
            return SortDefinitions(definitions);
        }

        public void WithCurrentDomainType(Type domainType, Action action)
        {
            var previous = _currentDomainType;
            _currentDomainType = domainType;
            try
            {
                action();
            }
            finally
            {
                _currentDomainType = previous;
            }
        }

        private static void Validate(IReadOnlyCollection<PersistentSyncDomainDefinition> definitions)
        {
            var duplicateNames = definitions
                .GroupBy(d => d.Key.Name, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .OrderBy(n => n)
                .ToArray();
            if (duplicateNames.Length > 0)
            {
                throw new InvalidOperationException("Duplicate persistent sync domain names: " + string.Join(", ", duplicateNames));
            }

            var duplicateWireIds = definitions
                .GroupBy(d => d.Key.WireId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key.ToString())
                .OrderBy(n => n)
                .ToArray();
            if (duplicateWireIds.Length > 0)
            {
                throw new InvalidOperationException("Duplicate persistent sync wire ids: " + string.Join(", ", duplicateWireIds));
            }

            var missingTypes = definitions
                .Where(d => d.DomainType == null)
                .Select(d => d.Key.Name)
                .OrderBy(n => n)
                .ToArray();
            if (missingTypes.Length > 0)
            {
                throw new InvalidOperationException("Persistent sync domains missing runtime types: " + string.Join(", ", missingTypes));
            }
        }

        private static IReadOnlyList<PersistentSyncDomainDefinition> SortDefinitions(IReadOnlyCollection<PersistentSyncDomainDefinition> definitions)
        {
            var byName = definitions.ToDictionary(d => d.Key.Name, StringComparer.Ordinal);
            var result = new List<PersistentSyncDomainDefinition>();
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);

            foreach (var definition in definitions.OrderBy(d => d.Key.Name, StringComparer.Ordinal))
            {
                Visit(definition, byName, visiting, visited, result);
            }

            return result;
        }

        private static void Visit(
            PersistentSyncDomainDefinition definition,
            Dictionary<string, PersistentSyncDomainDefinition> byName,
            HashSet<string> visiting,
            HashSet<string> visited,
            List<PersistentSyncDomainDefinition> result)
        {
            if (visited.Contains(definition.Key.Name))
            {
                return;
            }

            if (!visiting.Add(definition.Key.Name))
            {
                throw new InvalidOperationException("Persistent sync domain dependency cycle includes " + definition.Key.Name);
            }

            foreach (var dependency in definition.AfterDomains)
            {
                if (!byName.TryGetValue(dependency, out var dependencyDefinition))
                {
                    throw new InvalidOperationException($"Persistent sync domain {definition.Key.Name} depends on missing domain {dependency}.");
                }

                Visit(dependencyDefinition, byName, visiting, visited, result);
            }

            visiting.Remove(definition.Key.Name);
            visited.Add(definition.Key.Name);
            result.Add(definition);
        }
    }

    /// <summary>Client-side registrar carrying the declaring domain type from Harmony entry attributes.</summary>
    public sealed class PersistentSyncClientDomainRegistrar : PersistentSyncDomainRegistrar { }

    /// <summary>Server-side registrar carrying the declaring domain type from registration entry types.</summary>
    public sealed class PersistentSyncServerDomainRegistrar : PersistentSyncDomainRegistrar { }

    /// <summary>
    /// Fluent builder returned by <see cref="PersistentSyncDomainRegistrar.Register"/>; freezes into an immutable <see cref="PersistentSyncDomainDefinition"/>.
    /// </summary>
    public sealed class PersistentSyncDomainRegistrationBuilder
    {
        private readonly PersistentSyncDomainKey _key;
        private readonly List<string> _afterDomains = new List<string>();
        private readonly List<string> _serverScenarioBypasses = new List<string>();
        private Type _domainType;
        private string _scenarioName;
        private string _scalarFieldName;
        private bool _hasGameModes;
        private GameMode _gameModes;
        private PersistentSyncCapabilityFlags _requiredCapabilities;
        private PersistentSyncCapabilityFlags _producerRequiredCapabilities;
        private bool _hasMaterializationSlot;
        private PersistentSyncMaterializationSlot _materializationSlot = PersistentSyncMaterializationSlot.None;

        internal PersistentSyncDomainRegistrationBuilder(PersistentSyncDomainKey key, Type domainType)
        {
            _key = key;
            _domainType = domainType;
        }

        public PersistentSyncDomainRegistrationBuilder ForGameModes(GameMode gameModes)
        {
            _hasGameModes = true;
            _gameModes = gameModes;
            return this;
        }

        public PersistentSyncDomainRegistrationBuilder OwnsStockScenario(string scenarioName)
        {
            return OwnsStockScenario(scenarioName, null);
        }

        public PersistentSyncDomainRegistrationBuilder OwnsStockScenario(string scenarioName, string scalarFieldName)
        {
            var metadata = PersistentSyncStockScenarioMetadata.Get(scenarioName);
            _scenarioName = scenarioName;
            _scalarFieldName = scalarFieldName;
            _serverScenarioBypasses.Add(scenarioName);
            _requiredCapabilities |= metadata.RequiredCapabilities;
            if (!_hasGameModes)
            {
                _gameModes = metadata.GameModes;
                _hasGameModes = true;
            }

            if (!_hasMaterializationSlot)
            {
                _materializationSlot = metadata.MaterializationSlot;
                _hasMaterializationSlot = true;
            }

            return this;
        }

        public PersistentSyncDomainRegistrationBuilder OwnsKspmpScenario(string scenarioName, string scalarFieldName = null)
        {
            _scenarioName = scenarioName;
            _scalarFieldName = scalarFieldName;
            _serverScenarioBypasses.Add(scenarioName);
            if (!_hasGameModes)
            {
                _gameModes = GameMode.Sandbox | GameMode.Career | GameMode.Science;
                _hasGameModes = true;
            }

            return this;
        }

        public PersistentSyncDomainRegistrationBuilder RequiresCapability(PersistentSyncCapabilityFlags capability)
        {
            _requiredCapabilities |= capability;
            return this;
        }

        public PersistentSyncDomainRegistrationBuilder ProducerRequiresCapability(PersistentSyncCapabilityFlags capability)
        {
            _producerRequiredCapabilities |= capability;
            return this;
        }

        public PersistentSyncDomainRegistrationBuilder ProducerRequiresPartPurchaseMechanism()
        {
            return ProducerRequiresCapability(PersistentSyncCapabilityFlags.PartPurchaseMechanism);
        }

        public PersistentSyncDomainRegistrationBuilder Materializes(PersistentSyncMaterializationSlot slot)
        {
            _materializationSlot = slot;
            _hasMaterializationSlot = true;
            return this;
        }

        public PersistentSyncDomainRegistrationBuilder After(string dependency)
        {
            _afterDomains.Add(dependency);
            return this;
        }

        public PersistentSyncDomainRegistrationBuilder ProjectsFrom(string owner)
        {
            return After(owner);
        }

        public PersistentSyncDomainRegistrationBuilder UsesClientDomain<TDomain>()
        {
            _domainType = typeof(TDomain);
            return this;
        }

        public PersistentSyncDomainRegistrationBuilder UsesServerDomain<TDomain>()
        {
            _domainType = typeof(TDomain);
            return this;
        }

        internal PersistentSyncDomainDefinition Build(ushort sessionWireId)
        {
            if (!_hasGameModes)
            {
                throw new InvalidOperationException($"Persistent sync domain {_key.Name} must declare game modes or own a known stock scenario.");
            }

            return new PersistentSyncDomainDefinition(
                _key,
                _gameModes,
                _requiredCapabilities,
                _producerRequiredCapabilities,
                _hasMaterializationSlot ? _materializationSlot : PersistentSyncMaterializationSlot.None,
                _domainType,
                _afterDomains,
                _serverScenarioBypasses,
                sessionWireId,
                _scenarioName,
                _scalarFieldName);
        }
    }
}
