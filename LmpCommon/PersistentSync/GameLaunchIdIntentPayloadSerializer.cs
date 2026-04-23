using System.IO;
using System.Text;

namespace LmpCommon.PersistentSync
{
    /// <summary>Wire intent for <see cref="PersistentSyncDomainId.GameLaunchId"/> (uint + UTF8 reason).</summary>
    public static class GameLaunchIdIntentPayloadSerializer
    {
        public static byte[] Serialize(uint launchId, string reason)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(launchId);
                writer.Write(reason ?? string.Empty);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static void Deserialize(byte[] payload, int numBytes, out uint launchId, out string reason)
        {
            using (var stream = new MemoryStream(payload, 0, numBytes))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                launchId = reader.ReadUInt32();
                reason = reader.ReadString();
            }
        }
    }
}
