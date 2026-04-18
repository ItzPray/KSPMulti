using System.Collections.Generic;
using System.Linq;
using LmpCommon.PersistentSync;
using Strategies;

namespace LmpClient.Systems.PersistentSync
{
    /// <summary>
    /// After PersistentSync domains apply to live KSP objects, pushes that authoritative state into
    /// the persistence inputs stock reload paths use (<see cref="ProtoScenarioModule"/>,
    /// <see cref="ScenarioUpgradeableFacilities.protoUpgradeables"/>, etc.).
    /// </summary>
    public static class PersistentSyncGamePersistenceMaterializer
    {
        /// <summary>
        /// Materializes each distinct <see cref="PersistentSyncMaterializationSlot"/> at most once for the
        /// given dirty domains (typically one reconciler <c>FlushPendingState</c> pass).
        /// </summary>
        public static void Materialize(IEnumerable<PersistentSyncDomainId> dirtyDomains, string reason)
        {
            if (dirtyDomains == null)
            {
                return;
            }

            if (HighLogic.CurrentGame?.scenarios == null)
            {
                return;
            }

            var slots = new HashSet<PersistentSyncMaterializationSlot>();
            foreach (var domainId in dirtyDomains)
            {
                var slot = PersistentSyncMaterializationDomainMap.GetSlot(domainId);
                if (slot != PersistentSyncMaterializationSlot.None)
                {
                    slots.Add(slot);
                }
            }

            if (slots.Count == 0)
            {
                return;
            }

            foreach (var slot in slots.OrderBy(s => (byte)s))
            {
                switch (slot)
                {
                    case PersistentSyncMaterializationSlot.Funding:
                        if (Funding.Instance != null)
                        {
                            PersistentSyncScenarioProtoMaterializer.TryMirrorScenarioModule(Funding.Instance, "Funding", reason);
                        }

                        break;
                    case PersistentSyncMaterializationSlot.Reputation:
                        if (Reputation.Instance != null)
                        {
                            PersistentSyncScenarioProtoMaterializer.TryMirrorScenarioModule(Reputation.Instance, "Reputation", reason);
                        }

                        break;
                    case PersistentSyncMaterializationSlot.StrategySystem:
                        if (StrategySystem.Instance != null)
                        {
                            PersistentSyncScenarioProtoMaterializer.TryMirrorScenarioModule(StrategySystem.Instance, "StrategySystem", reason);
                        }

                        break;
                    case PersistentSyncMaterializationSlot.ResearchAndDevelopment:
                        ResearchAndDevelopmentProtoMirror.TrySyncLiveInstanceToGameProto(reason);
                        break;
                    case PersistentSyncMaterializationSlot.UpgradeableFacilities:
                        UpgradeableFacilitiesPersistentSyncClientDomain.MaterializeUpgradeableProtosFromLiveScene(reason);
                        break;
                }
            }
        }
    }
}
