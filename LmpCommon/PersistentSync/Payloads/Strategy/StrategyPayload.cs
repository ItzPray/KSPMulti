using System;
using LmpCommon.PersistentSync;

namespace LmpCommon.PersistentSync.Payloads.Strategy
{
    /// <summary>Domain-facing envelope for Strategy persistent sync (wire-identical to legacy root array).</summary>
    public sealed class StrategyPayload
    {
        public StrategySnapshotInfo[] Items = Array.Empty<StrategySnapshotInfo>();
    }

    public sealed class StrategyPayloadCodecRegistrar : IPersistentSyncPayloadCodecRegistrar
    {
        public void Register(PersistentSyncPayloadCodecRegistry registry)
        {
            registry.RegisterCustom<StrategyPayload>(
                reader => new StrategyPayload
                {
                    Items = (StrategySnapshotInfo[])PersistentSyncPayloadSerializer.ReadConventionRoot(reader, typeof(StrategySnapshotInfo[]))
                },
                (writer, p) => PersistentSyncPayloadSerializer.WriteConventionRoot(
                    writer,
                    typeof(StrategySnapshotInfo[]),
                    p?.Items ?? Array.Empty<StrategySnapshotInfo>()));
        }
    }
}
