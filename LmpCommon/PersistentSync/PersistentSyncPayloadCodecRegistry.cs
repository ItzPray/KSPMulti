using System;

namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Thin facade passed to <see cref="IPersistentSyncPayloadCodecRegistrar"/> implementations.
    /// </summary>
    public sealed class PersistentSyncPayloadCodecRegistry
    {
        public void RegisterCustom<T>(Func<PersistentSyncPayloadReader, T> read, Action<PersistentSyncPayloadWriter, T> write)
        {
            PersistentSyncPayloadSerializer.RegisterCustomUnlocked(read, write);
        }
    }
}
