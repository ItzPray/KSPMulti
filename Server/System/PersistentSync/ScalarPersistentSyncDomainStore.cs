using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Server.Client;
using System.Collections.Generic;
using System.Globalization;

namespace Server.System.PersistentSync
{
    /// <summary>
    /// Thin scalar shim over <see cref="ScenarioSyncDomainStore{TCanonical}"/> so existing scalar domains (Funds,
    /// Science, Reputation) inherit the Scenario Sync Domain Contract (revision, equality short-circuit, write lock)
    /// without rewriting their serializers or parsing code.
    ///
    /// Domains still only override:
    /// <list type="bullet">
    /// <item><description><see cref="GetStartingValue"/></description></item>
    /// <item><description><see cref="TryParseStoredValue"/> + <see cref="FormatStoredValue"/> (scenario field round-trip)</description></item>
    /// <item><description><see cref="ValuesAreEqual"/> (drives the equality short-circuit)</description></item>
    /// <item><description><see cref="DeserializeIntentPayload"/> + <see cref="SerializeSnapshotPayload"/> (wire)</description></item>
    /// </list>
    /// </summary>
    public abstract class ScalarPersistentSyncDomainStore<T> : ScenarioSyncDomainStore<ScalarCanonical<T>, PersistentSyncValueWithReason<T>, T>
    {
        protected abstract string ScenarioFieldName { get; }

        /// <summary>Convenience accessor used by legacy tests and subclasses. Must remain on the public scalar surface.</summary>
        protected T CurrentValue
        {
            get
            {
                var canonical = CurrentForTests;
                return canonical != null ? canonical.Value : default;
            }
        }

        /// <summary>Convenience accessor used by legacy tests and subclasses.</summary>
        protected long Revision => RevisionForTests;

        protected sealed override ScalarCanonical<T> CreateEmpty()
        {
            return new ScalarCanonical<T>(GetStartingValue());
        }

        protected sealed override ScalarCanonical<T> LoadCanonical(ConfigNode scenario, bool createdFromScratch)
        {
            var value = GetStartingValue();

            if (scenario != null)
            {
                var rawValue = scenario.GetValue(ScenarioFieldName)?.Value;
                if (!string.IsNullOrEmpty(rawValue) && TryParseStoredValue(rawValue, out var parsedValue))
                {
                    value = parsedValue;
                }
            }

            return new ScalarCanonical<T>(value);
        }

        protected sealed override ConfigNode WriteCanonical(ConfigNode scenario, ScalarCanonical<T> canonical)
        {
            if (scenario == null || canonical == null)
            {
                return scenario;
            }

            scenario.UpdateValue(ScenarioFieldName, FormatStoredValue(canonical.Value));
            return scenario;
        }

        protected sealed override T BuildSnapshotPayload(ScalarCanonical<T> canonical)
        {
            return (canonical ?? new ScalarCanonical<T>(GetStartingValue())).Value;
        }

        protected sealed override bool AreEquivalent(ScalarCanonical<T> a, ScalarCanonical<T> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return ValuesAreEqual(a.Value, b.Value);
        }

        protected sealed override ReduceResult<ScalarCanonical<T>> ReduceIntent(ClientStructure client, ScalarCanonical<T> current, PersistentSyncValueWithReason<T> intent, string reason, bool isServerMutation)
        {
            return ReduceResult<ScalarCanonical<T>>.Accept(new ScalarCanonical<T>(intent != null ? intent.Value : GetStartingValue()));
        }

        protected abstract T GetStartingValue();
        protected abstract bool TryParseStoredValue(string value, out T parsedValue);
        protected abstract string FormatStoredValue(T value);
        protected abstract bool ValuesAreEqual(T currentValue, T incomingValue);
        protected static bool TryParseDouble(string value, out double parsedValue)
        {
            return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedValue);
        }

        protected static bool TryParseFloat(string value, out float parsedValue)
        {
            return float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedValue);
        }
    }

    /// <summary>
    /// Minimal reference-type wrapper around a scalar value so it satisfies the
    /// <see cref="ScenarioSyncDomainStore{TCanonical}"/> <c>class</c> constraint while keeping scalar domains' value
    /// semantics (immutable, comparable by <c>Value</c>).
    /// </summary>
    public sealed class ScalarCanonical<T>
    {
        public ScalarCanonical(T value)
        {
            Value = value;
        }

        public T Value { get; }

        public override bool Equals(object obj)
        {
            return obj is ScalarCanonical<T> other && EqualityComparer<T>.Default.Equals(Value, other.Value);
        }

        public override int GetHashCode()
        {
            return EqualityComparer<T>.Default.GetHashCode(Value);
        }
    }
}
