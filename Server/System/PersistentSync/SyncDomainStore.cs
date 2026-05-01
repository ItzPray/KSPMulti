using LmpCommon.Message.Data.PersistentSync;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Server.Client;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Server.System.PersistentSync
{
    public abstract class SyncDomainStore<TPayload> : ScenarioSyncDomainStore<SyncDomainStore<TPayload>.PayloadBox>
    {
        private readonly PersistentSyncStockScenarioAttribute _stockScenario;
        private readonly PersistentSyncOwnedScenarioAttribute _ownedScenario;

        protected SyncDomainStore()
        {
            var type = GetType();
            _stockScenario = (PersistentSyncStockScenarioAttribute)Attribute.GetCustomAttribute(type, typeof(PersistentSyncStockScenarioAttribute));
            _ownedScenario = (PersistentSyncOwnedScenarioAttribute)Attribute.GetCustomAttribute(type, typeof(PersistentSyncOwnedScenarioAttribute));
        }

        public sealed override PersistentSyncDomainId DomainId => PersistentSyncDomainCatalog.TryGetByName(DomainName, out var definition)
            ? definition.DomainId
            : (PersistentSyncDomainId)PersistentSyncDomainNaming.GetKnownWireId(DomainName);

        protected PersistentSyncDomainDefinition Definition => PersistentSyncDomainCatalog.GetByName(DomainName);
        protected string DomainName => PersistentSyncDomainNaming.InferDomainName(GetType());
        protected sealed override string ScenarioName => _stockScenario?.ScenarioName ?? _ownedScenario?.ScenarioName;
        protected string ScenarioFieldName => _stockScenario?.ScalarField ?? _ownedScenario?.ScalarField;

        public override bool AuthorizeIntent(ClientStructure client, byte[] payload, int numBytes) => AuthorizeByPolicy(client);

        protected virtual TPayload CreateDefaultPayload()
        {
            return default;
        }

        protected virtual TPayload LoadPayload(ConfigNode scenario, bool createdFromScratch)
        {
            if (string.IsNullOrEmpty(ScenarioFieldName))
            {
                return CreateDefaultPayload();
            }

            var raw = scenario?.GetValue(ScenarioFieldName)?.Value;
            return TryParseScalar(raw, out var value) ? value : CreateDefaultPayload();
        }

        protected virtual ConfigNode WritePayload(ConfigNode scenario, TPayload payload)
        {
            if (scenario == null || string.IsNullOrEmpty(ScenarioFieldName))
            {
                return scenario;
            }

            scenario.UpdateValue(ScenarioFieldName, FormatScalar(payload));
            return scenario;
        }

        protected virtual ReduceResult<TPayload> ReducePayload(
            ClientStructure client,
            TPayload current,
            TPayload incoming,
            string reason,
            bool isServerMutation)
        {
            return ReduceResult<TPayload>.Accept(incoming);
        }

        protected virtual bool PayloadsAreEqual(TPayload left, TPayload right)
        {
            return EqualityComparer<TPayload>.Default.Equals(left, right);
        }

        protected sealed override PayloadBox CreateEmpty()
        {
            return new PayloadBox(CreateDefaultPayload());
        }

        protected sealed override PayloadBox LoadCanonical(ConfigNode scenario, bool createdFromScratch)
        {
            return new PayloadBox(LoadPayload(scenario, createdFromScratch));
        }

        protected sealed override ConfigNode WriteCanonical(ConfigNode scenario, PayloadBox canonical)
        {
            return WritePayload(scenario, canonical != null ? canonical.Payload : CreateDefaultPayload());
        }

        protected sealed override ReduceResult<PayloadBox> ReduceIntent(
            ClientStructure client,
            PayloadBox current,
            byte[] payload,
            int numBytes,
            string reason,
            bool isServerMutation)
        {
            var incoming = PersistentSyncPayloadSerializer.Deserialize<TPayload>(payload ?? Array.Empty<byte>(), numBytes);
            var reduced = ReducePayload(client, current != null ? current.Payload : CreateDefaultPayload(), incoming, reason, isServerMutation);
            if (reduced == null || !reduced.Accepted)
            {
                return ReduceResult<PayloadBox>.Reject();
            }

            return ReduceResult<PayloadBox>.Accept(
                new PayloadBox(reduced.NextState),
                reduced.ForceReplyToOriginClient,
                reduced.ReplyToProducerClient);
        }

        protected sealed override byte[] SerializeSnapshot(PayloadBox canonical)
        {
            return PersistentSyncPayloadSerializer.Serialize(canonical != null ? canonical.Payload : CreateDefaultPayload());
        }

        protected sealed override bool AreEquivalent(PayloadBox a, PayloadBox b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return PayloadsAreEqual(a.Payload, b.Payload);
        }

        private static bool TryParseScalar(string raw, out TPayload value)
        {
            value = default;
            if (raw == null)
            {
                return false;
            }

            var type = typeof(TPayload);
            object parsed;
            if (type == typeof(string)) parsed = raw;
            else if (type == typeof(double) && double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var d)) parsed = d;
            else if (type == typeof(float) && float.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var f)) parsed = f;
            else if (type == typeof(int) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) parsed = i;
            else if (type == typeof(uint) && uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u)) parsed = u;
            else if (type == typeof(bool) && bool.TryParse(raw, out var b)) parsed = b;
            else if (type == typeof(Guid) && Guid.TryParse(raw, out var g)) parsed = g;
            else return false;

            value = (TPayload)parsed;
            return true;
        }

        private static string FormatScalar(TPayload value)
        {
            if (value == null) return string.Empty;
            if (value is IFormattable formattable)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            }

            return value.ToString();
        }

        public sealed class PayloadBox
        {
            public PayloadBox(TPayload payload)
            {
                Payload = payload;
            }

            public TPayload Payload { get; }
        }
    }
}
