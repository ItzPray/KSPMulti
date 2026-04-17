using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using LmpCommon.Message.Data.PersistentSync;
using LunaConfigNode.CfgNode;
using Server.System;
using System.Collections.Concurrent;
using System.Globalization;

namespace Server.System.PersistentSync
{
    public abstract class ScalarPersistentSyncDomainStore<T> : IPersistentSyncServerDomain
    {
        private static readonly ConcurrentDictionary<string, object> ScenarioLocks = new ConcurrentDictionary<string, object>();

        public abstract PersistentSyncDomainId DomainId { get; }
        public abstract PersistentAuthorityPolicy AuthorityPolicy { get; }
        protected abstract string ScenarioName { get; }
        protected abstract string ScenarioFieldName { get; }

        protected T CurrentValue { get; private set; }
        protected long Revision { get; private set; }

        public void LoadFromPersistence(bool createdFromScratch)
        {
            CurrentValue = GetStartingValue();

            lock (ScenarioLocks.GetOrAdd(ScenarioName, _ => new object()))
            {
                if (ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
                {
                    var rawValue = scenario.GetValue(ScenarioFieldName)?.Value;
                    if (!string.IsNullOrEmpty(rawValue) && TryParseStoredValue(rawValue, out var parsedValue))
                    {
                        CurrentValue = parsedValue;
                    }
                    else if (createdFromScratch)
                    {
                        scenario.UpdateValue(ScenarioFieldName, FormatStoredValue(CurrentValue));
                    }
                }
            }
        }

        public PersistentSyncDomainSnapshot GetCurrentSnapshot()
        {
            var payload = SerializeSnapshotPayload(CurrentValue);
            return new PersistentSyncDomainSnapshot
            {
                DomainId = DomainId,
                Revision = Revision,
                AuthorityPolicy = AuthorityPolicy,
                Payload = payload,
                NumBytes = payload.Length
            };
        }

        public PersistentSyncDomainApplyResult ApplyClientIntent(PersistentSyncIntentMsgData data)
        {
            var incomingValue = DeserializeIntentPayload(data.Payload, data.NumBytes, out _);
            if (ValuesAreEqual(CurrentValue, incomingValue))
            {
                return new PersistentSyncDomainApplyResult
                {
                    Accepted = true,
                    Changed = false,
                    ReplyToOriginClient = data.ClientKnownRevision != Revision,
                    Snapshot = GetCurrentSnapshot()
                };
            }

            CurrentValue = incomingValue;
            Revision++;
            PersistCurrentValue();

            return new PersistentSyncDomainApplyResult
            {
                Accepted = true,
                Changed = true,
                ReplyToOriginClient = false,
                Snapshot = GetCurrentSnapshot()
            };
        }

        public PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, int numBytes, string reason)
        {
            var incomingValue = DeserializeIntentPayload(payload, numBytes, out _);
            if (ValuesAreEqual(CurrentValue, incomingValue))
            {
                return new PersistentSyncDomainApplyResult
                {
                    Accepted = true,
                    Changed = false,
                    ReplyToOriginClient = false,
                    Snapshot = GetCurrentSnapshot()
                };
            }

            CurrentValue = incomingValue;
            Revision++;
            PersistCurrentValue();

            return new PersistentSyncDomainApplyResult
            {
                Accepted = true,
                Changed = true,
                ReplyToOriginClient = false,
                Snapshot = GetCurrentSnapshot()
            };
        }

        protected abstract T GetStartingValue();
        protected abstract bool TryParseStoredValue(string value, out T parsedValue);
        protected abstract string FormatStoredValue(T value);
        protected abstract bool ValuesAreEqual(T currentValue, T incomingValue);
        protected abstract T DeserializeIntentPayload(byte[] payload, int numBytes, out string reason);
        protected abstract byte[] SerializeSnapshotPayload(T value);

        private void PersistCurrentValue()
        {
            lock (ScenarioLocks.GetOrAdd(ScenarioName, _ => new object()))
            {
                if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue(ScenarioName, out var scenario))
                {
                    return;
                }

                scenario.UpdateValue(ScenarioFieldName, FormatStoredValue(CurrentValue));
            }
        }

        protected static bool TryParseDouble(string value, out double parsedValue)
        {
            return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedValue);
        }

        protected static bool TryParseFloat(string value, out float parsedValue)
        {
            return float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedValue);
        }
    }
}
