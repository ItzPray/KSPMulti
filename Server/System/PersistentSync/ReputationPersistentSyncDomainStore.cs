using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using Server.Settings.Structures;
using System.Globalization;

namespace Server.System.PersistentSync
{
    public class ReputationPersistentSyncDomainStore : ScalarPersistentSyncDomainStore<float>
    {
        public override PersistentSyncDomainId DomainId => PersistentSyncDomainId.Reputation;
        protected override string ScenarioName => "Reputation";
        protected override string ScenarioFieldName => "rep";
        protected override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        protected override float GetStartingValue() => GameplaySettings.SettingsStore.StartingReputation;

        protected override bool TryParseStoredValue(string value, out float parsedValue) => TryParseFloat(value, out parsedValue);

        protected override string FormatStoredValue(float value) => value.ToString(CultureInfo.InvariantCulture);

        protected override bool ValuesAreEqual(float currentValue, float incomingValue) => currentValue.Equals(incomingValue);

        protected override float DeserializeIntentPayload(byte[] payload, int numBytes, out string reason)
        {
            ReputationIntentPayloadSerializer.Deserialize(payload, numBytes, out var reputation, out reason);
            return reputation;
        }

        protected override byte[] SerializeSnapshotPayload(float value) => ReputationSnapshotPayloadSerializer.Serialize(value);
    }
}
