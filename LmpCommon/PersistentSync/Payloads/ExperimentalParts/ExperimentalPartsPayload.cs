using System;
using LmpCommon.PersistentSync;

namespace LmpCommon.PersistentSync.Payloads.ExperimentalParts
{
    public sealed class ExperimentalPartsPayload
    {
        public ExperimentalPartSnapshotInfo[] Items = Array.Empty<ExperimentalPartSnapshotInfo>();
    }

    public sealed class ExperimentalPartsPayloadCodecRegistrar : IPersistentSyncPayloadCodecRegistrar
    {
        public void Register(PersistentSyncPayloadCodecRegistry registry)
        {
            registry.RegisterCustom<ExperimentalPartsPayload>(
                reader => new ExperimentalPartsPayload
                {
                    Items = (ExperimentalPartSnapshotInfo[])PersistentSyncPayloadSerializer.ReadConventionRoot(reader, typeof(ExperimentalPartSnapshotInfo[]))
                },
                (writer, p) => PersistentSyncPayloadSerializer.WriteConventionRoot(
                    writer,
                    typeof(ExperimentalPartSnapshotInfo[]),
                    p?.Items ?? Array.Empty<ExperimentalPartSnapshotInfo>()));
        }
    }
}
