namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Domain payload assemblies register custom codecs via discovery (see <see cref="PersistentSyncPayloadSerializer"/>).
    /// </summary>
    public interface IPersistentSyncPayloadCodecRegistrar
    {
        void Register(PersistentSyncPayloadCodecRegistry registry);
    }
}
