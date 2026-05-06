using LmpClient.Events;
using LmpClient.Extensions;
using LmpClient.Systems.ShareStrategy;
using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Payloads.Strategy;
using Strategies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LmpClient.Systems.PersistentSync
{
    public class StrategyPersistentSyncClientDomain : SyncClientDomain<StrategyPayload>
    {
        public static void RegisterPersistentSyncDomain(PersistentSyncClientDomainRegistrar registrar)
        {
            registrar.RegisterCurrent()
                .UsesClientDomain<StrategyPersistentSyncClientDomain>();
        }

        private Dictionary<string, StrategySnapshotInfo> _pendingStrategies;

        protected override IReadOnlyList<string> DomainsToSuppressDuringApply =>
            new[]
            {
                PersistentSyncDomainNames.Funds,
                PersistentSyncDomainNames.Science,
                PersistentSyncDomainNames.Reputation
            };

        protected override void OnDomainEnabled()
        {
            StrategyEvent.onStrategyActivated.Add(OnStrategyActivated);
            StrategyEvent.onStrategyDeactivated.Add(OnStrategyDeactivated);
        }

        protected override void OnDomainDisabled()
        {
            StrategyEvent.onStrategyActivated.Remove(OnStrategyActivated);
            StrategyEvent.onStrategyDeactivated.Remove(OnStrategyDeactivated);
        }

        private void OnStrategyActivated(Strategy strategy)
        {
            if (IgnoreLocalEvents || ShareStrategySystem.Singleton.OneTimeStrategies.Contains(strategy.Config.Name))
            {
                return;
            }

            LunaLog.Log($"Relaying strategy activation: {strategy.Config.Name} - with factor: {strategy.Factor}");
            SendStrategyPayload(strategy);
        }

        private void OnStrategyDeactivated(Strategy strategy)
        {
            if (IgnoreLocalEvents || ShareStrategySystem.Singleton.OneTimeStrategies.Contains(strategy.Config.Name))
            {
                return;
            }

            LunaLog.Log($"Relaying strategy deactivation: {strategy.Config.Name} - with factor: {strategy.Factor}");
            SendStrategyPayload(strategy);
        }

        private void SendStrategyPayload(Strategy strategy)
        {
            var configNode = ConvertStrategyToConfigNode(strategy);
            if (configNode == null)
            {
                return;
            }

            var data = configNode.Serialize();
            SendLocalPayload(
                new StrategyPayload
                {
                    Items = new[]
                    {
                        new StrategySnapshotInfo
                        {
                            Name = strategy.Config.Name,
                            Data = data
                        }
                    }
                },
                $"StrategyUpdate:{strategy.Config.Name}");
        }

        private static ConfigNode ConvertStrategyToConfigNode(Strategy strategy)
        {
            var configNode = new ConfigNode();
            try
            {
                strategy.Save(configNode);
                configNode.AddValue("isActive", strategy.IsActive);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[KSPMP]: Error while saving strategy: {e}");
                return null;
            }

            return configNode;
        }

        protected override void OnPayloadBuffered(PersistentSyncBufferedSnapshot snapshot, StrategyPayload payload)
        {
            var items = payload?.Items ?? Array.Empty<StrategySnapshotInfo>();
            _pendingStrategies = items
                .Where(strategy => strategy != null && !string.IsNullOrEmpty(strategy.Name))
                .ToDictionary(strategy => strategy.Name, strategy => strategy, StringComparer.Ordinal);
        }

        public override PersistentSyncApplyOutcome FlushPendingState()
        {
            if (_pendingStrategies == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            if (StrategySystem.Instance == null || Funding.Instance == null || ResearchAndDevelopment.Instance == null || Reputation.Instance == null)
            {
                return PersistentSyncApplyOutcome.Deferred;
            }

            StartIgnoringLocalEvents();
            try
            {
                using (PersistentSyncDomainSuppressionScope.Begin(
                    PersistentSyncEventSuppressorRegistry.Resolve(DomainsToSuppressDuringApply),
                    restoreOldValueOnDispose: true))
                {
                    foreach (var strategy in _pendingStrategies.Values.OrderBy(value => value.Name))
                    {
                        if (!ShareStrategySystem.Singleton.TryApplyStrategySnapshotMutation(strategy, "PersistentSyncSnapshotApply"))
                        {
                            return PersistentSyncApplyOutcome.Rejected;
                        }
                    }
                }
            }
            finally
            {
                StopIgnoringLocalEvents();
            }

            ShareStrategySystem.Singleton.RefreshStrategyUiAdapters("PersistentSyncSnapshotApply");
            _pendingStrategies = null;
            return PersistentSyncApplyOutcome.Applied;
        }

        protected override bool TryBuildLocalAuditPayload(out StrategyPayload payload, out string unavailableReason)
        {
            payload = new StrategyPayload { Items = Array.Empty<StrategySnapshotInfo>() };
            if (StrategySystem.Instance == null)
            {
                unavailableReason = "StrategySystem.Instance is null";
                return false;
            }

            var items = new System.Collections.Generic.List<StrategySnapshotInfo>();
            foreach (var strategy in StrategySystem.Instance.Strategies.Where(s => s != null))
            {
                var cn = ConvertStrategyToConfigNode(strategy);
                if (cn == null)
                {
                    continue;
                }

                items.Add(new StrategySnapshotInfo
                {
                    Name = strategy.Config.Name,
                    Data = cn.Serialize()
                });
            }

            items.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            payload.Items = items.ToArray();
            unavailableReason = null;
            return true;
        }
    }
}
