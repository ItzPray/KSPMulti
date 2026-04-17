using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using Server.Settings.Structures;
using System.Globalization;

namespace Server.System.PersistentSync
{
    public class FundsPersistentSyncDomainStore : ScalarPersistentSyncDomainStore<double>
    {
        public override PersistentSyncDomainId DomainId => PersistentSyncDomainId.Funds;
        protected override string ScenarioName => "Funding";
        protected override string ScenarioFieldName => "funds";
        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        protected override double GetStartingValue() => GameplaySettings.SettingsStore.StartingFunds;

        protected override bool TryParseStoredValue(string value, out double parsedValue) => TryParseDouble(value, out parsedValue);

        protected override string FormatStoredValue(double value) => value.ToString(CultureInfo.InvariantCulture);

        protected override bool ValuesAreEqual(double currentValue, double incomingValue) => currentValue.Equals(incomingValue);

        protected override double DeserializeIntentPayload(byte[] payload, int numBytes, out string reason)
        {
            FundsIntentPayloadSerializer.Deserialize(payload, numBytes, out var funds, out reason);
            return funds;
        }

        protected override byte[] SerializeSnapshotPayload(double value) => FundsSnapshotPayloadSerializer.Serialize(value);
    }
}
