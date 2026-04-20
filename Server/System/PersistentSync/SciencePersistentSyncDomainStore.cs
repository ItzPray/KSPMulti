using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using Server.Client;
using Server.Settings.Structures;
using System.Globalization;

namespace Server.System.PersistentSync
{
    public class SciencePersistentSyncDomainStore : ScalarPersistentSyncDomainStore<float>
    {
        public override PersistentSyncDomainId DomainId => PersistentSyncDomainId.Science;
        protected override string ScenarioName => "ResearchAndDevelopment";
        protected override string ScenarioFieldName => "sci";
        public override PersistentAuthorityPolicy AuthorityPolicy => PersistentAuthorityPolicy.AnyClientIntent;

        public override bool AuthorizeIntent(ClientStructure client, byte[] payload, int numBytes) => AuthorizeByPolicy(client);

        protected override float GetStartingValue() => GameplaySettings.SettingsStore.StartingScience;

        protected override bool TryParseStoredValue(string value, out float parsedValue) => TryParseFloat(value, out parsedValue);

        protected override string FormatStoredValue(float value) => value.ToString(CultureInfo.InvariantCulture);

        protected override bool ValuesAreEqual(float currentValue, float incomingValue) => currentValue.Equals(incomingValue);

        protected override float DeserializeIntentPayload(byte[] payload, int numBytes, out string reason)
        {
            ScienceIntentPayloadSerializer.Deserialize(payload, numBytes, out var science, out reason);
            return science;
        }

        protected override byte[] SerializeSnapshotPayload(float value) => ScienceSnapshotPayloadSerializer.Serialize(value);
    }
}
