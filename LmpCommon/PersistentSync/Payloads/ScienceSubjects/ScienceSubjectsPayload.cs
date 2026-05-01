using System;
using LmpCommon.PersistentSync;

namespace LmpCommon.PersistentSync.Payloads.ScienceSubjects
{
    public sealed class ScienceSubjectsPayload
    {
        public ScienceSubjectSnapshotInfo[] Items = Array.Empty<ScienceSubjectSnapshotInfo>();
    }

    public sealed class ScienceSubjectsPayloadCodecRegistrar : IPersistentSyncPayloadCodecRegistrar
    {
        public void Register(PersistentSyncPayloadCodecRegistry registry)
        {
            registry.RegisterCustom<ScienceSubjectsPayload>(
                reader => new ScienceSubjectsPayload
                {
                    Items = (ScienceSubjectSnapshotInfo[])PersistentSyncPayloadSerializer.ReadConventionRoot(reader, typeof(ScienceSubjectSnapshotInfo[]))
                },
                (writer, p) => PersistentSyncPayloadSerializer.WriteConventionRoot(
                    writer,
                    typeof(ScienceSubjectSnapshotInfo[]),
                    p?.Items ?? Array.Empty<ScienceSubjectSnapshotInfo>()));
        }
    }
}
