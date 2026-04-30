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

        public IReadOnlyList<PersistentSyncDomainDefinition> BuildDefinitions()
        {
            var definitions = _builders.Select(b => b.Build()).ToArray();
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
                if (!byName.TryGetValue(dependency.Name, out var dependencyDefinition))
                {
                    throw new InvalidOperationException($"Persistent sync domain {definition.Key.Name} depends on missing domain {dependency.Name}.");
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
        private readonly List<PersistentSyncDomainKey> _afterDomains = new List<PersistentSyncDomainKey>();
        private readonly List<string> _serverScenarioBypasses = new List<string>();
        private Type _domainType;
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
            var metadata = PersistentSyncStockScenarioMetadata.Get(scenarioName);
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

        public PersistentSyncDomainRegistrationBuilder After(PersistentSyncDomainKey dependency)
        {
            _afterDomains.Add(dependency);
            return this;
        }

        public PersistentSyncDomainRegistrationBuilder ProjectsFrom(PersistentSyncDomainKey owner)
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

        internal PersistentSyncDomainDefinition Build()
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
                _serverScenarioBypasses);
        }
    }
}