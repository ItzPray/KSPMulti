using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using Server.Settings.Structures;
using System.Globalization;

namespace Server.System.PersistentSync
{
    public class FundsPersistentSyncDomainStore : ScalarPersistentSyncDomainStore<double>
    {
        public static readonly PersistentSyncDomainKey Domain = PersistentSyncDomain.Define("Funds", 0);

        public static void RegisterPersistentSyncDomain(PersistentSyncServerDomainRegistrar registrar)
        {
            registrar.Register(Domain)
                .OwnsStockScenario("Funding")
                .UsesServerDomain<FundsPersistentSyncDomainStore>();
        }

        public override PersistentSyncDomainId DomainId => Domain.LegacyId;
        protected override string ScenarioName => "Funding";
        protected override string ScenarioFieldName => "funds";
        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        protected override double GetStartingValue() => GameplaySettings.SettingsStore.StartingFunds;

        protected override bool TryParseStoredValue(string value, out double parsedValue) => TryParseDouble(value, out parsedValue);

        protected override string FormatStoredValue(double value) => value.ToString(CultureInfo.InvariantCulture);

        protected override bool ValuesAreEqual(double currentValue, double incomingValue) => currentValue.Equals(incomingValue);
    }
}
