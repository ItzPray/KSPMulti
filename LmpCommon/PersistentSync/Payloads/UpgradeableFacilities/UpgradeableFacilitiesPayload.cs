using System;
using LmpCommon.PersistentSync;

namespace LmpCommon.PersistentSync.Payloads.UpgradeableFacilities
{
    /// <summary>Domain envelope for upgradeable facility levels (wire-identical to legacy root array).</summary>
    public sealed class UpgradeableFacilitiesPayload
    {
        public UpgradeableFacilityLevelPayload[] Items = Array.Empty<UpgradeableFacilityLevelPayload>();
    }

    public sealed class UpgradeableFacilitiesPayloadCodecRegistrar : IPersistentSyncPayloadCodecRegistrar
    {
        public void Register(PersistentSyncPayloadCodecRegistry registry)
        {
            registry.RegisterCustom<UpgradeableFacilitiesPayload>(
                reader => new UpgradeableFacilitiesPayload
                {
                    Items = (UpgradeableFacilityLevelPayload[])PersistentSyncPayloadSerializer.ReadConventionRoot(reader, typeof(UpgradeableFacilityLevelPayload[]))
                },
                (writer, p) => PersistentSyncPayloadSerializer.WriteConventionRoot(
                    writer,
                    typeof(UpgradeableFacilityLevelPayload[]),
                    p?.Items ?? Array.Empty<UpgradeableFacilityLevelPayload>()));
        }
    }
}
