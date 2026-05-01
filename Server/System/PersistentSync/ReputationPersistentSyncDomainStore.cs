using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using Server.Settings.Structures;
using System.Globalization;

namespace Server.System.PersistentSync
{
    public class ReputationPersistentSyncDomainStore : ScalarPersistentSyncDomainStore<float>
    {
        public static readonly PersistentSyncDomainKey Domain = PersistentSyncDomain.Define("Reputation", 2);

        public static void RegisterPersistentSyncDomain(PersistentSyncServerDomainRegistrar registrar)
        {
            registrar.Register(Domain)
                .OwnsStockScenario("Reputation")
                .UsesServerDomain<ReputationPersistentSyncDomainStore>();
        }

        public override PersistentSyncDomainId DomainId => Domain.LegacyId;
        protected override string ScenarioName => "Reputation";
        protected override string ScenarioFieldName => "rep";
        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        protected override float GetStartingValue() => GameplaySettings.SettingsStore.StartingReputation;

        protected override bool TryParseStoredValue(string value, out float parsedValue) => TryParseFloat(value, out parsedValue);

        protected override string FormatStoredValue(float value) => value.ToString(CultureInfo.InvariantCulture);

        protected override bool ValuesAreEqual(float currentValue, float incomingValue) => currentValue.Equals(incomingValue);
    }
}
