using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using Server.Settings.Structures;
using System.Globalization;

namespace Server.System.PersistentSync
{
    public class SciencePersistentSyncDomainStore : ScalarPersistentSyncDomainStore<float>
    {
        public static readonly PersistentSyncDomainKey Domain = PersistentSyncDomain.Define("Science", 1);

        public static void RegisterPersistentSyncDomain(PersistentSyncServerDomainRegistrar registrar)
        {
            registrar.Register(Domain)
                .OwnsStockScenario("ResearchAndDevelopment")
                .UsesServerDomain<SciencePersistentSyncDomainStore>();
        }

        public override PersistentSyncDomainId DomainId => Domain.LegacyId;
        protected override string ScenarioName => "ResearchAndDevelopment";
        protected override string ScenarioFieldName => "sci";
        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        protected override float GetStartingValue() => GameplaySettings.SettingsStore.StartingScience;

        protected override bool TryParseStoredValue(string value, out float parsedValue) => TryParseFloat(value, out parsedValue);

        protected override string FormatStoredValue(float value) => value.ToString(CultureInfo.InvariantCulture);

        protected override bool ValuesAreEqual(float currentValue, float incomingValue) => currentValue.Equals(incomingValue);
    }
}
