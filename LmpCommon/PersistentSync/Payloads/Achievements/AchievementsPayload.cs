using System;
using LmpCommon.PersistentSync;

namespace LmpCommon.PersistentSync.Payloads.Achievements
{
    public sealed class AchievementsPayload
    {
        public AchievementSnapshotInfo[] Items = Array.Empty<AchievementSnapshotInfo>();
    }

    public sealed class AchievementsPayloadCodecRegistrar : IPersistentSyncPayloadCodecRegistrar
    {
        public void Register(PersistentSyncPayloadCodecRegistry registry)
        {
            registry.RegisterCustom<AchievementsPayload>(
                reader => new AchievementsPayload
                {
                    Items = (AchievementSnapshotInfo[])PersistentSyncPayloadSerializer.ReadConventionRoot(reader, typeof(AchievementSnapshotInfo[]))
                },
                (writer, p) => PersistentSyncPayloadSerializer.WriteConventionRoot(
                    writer,
                    typeof(AchievementSnapshotInfo[]),
                    p?.Items ?? Array.Empty<AchievementSnapshotInfo>()));
        }
    }
}
