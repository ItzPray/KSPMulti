using Lidgren.Network;
using LmpCommon.Message.Base;
using System;

namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Single row of the persistent-sync domain catalog on <see cref="LmpCommon.Message.Data.Settings.PersistentSyncCatalogMsgData"/>.
    /// </summary>
    public struct PersistentSyncCatalogRowWire
    {
        public ushort WireId;
        public string DomainName;
        public byte AuthorityPolicy;
        public byte MaterializationSlot;
        public uint RequiredCapabilities;
        public uint ProducerRequiredCapabilities;
        public ushort InitialSyncGameModes;
    }

    /// <summary>
    /// Versioned serialization for the persistent-sync catalog on <see cref="LmpCommon.Message.Data.Settings.PersistentSyncCatalogMsgData"/>.
    /// </summary>
    public static class PersistentSyncCatalogWire
    {
        public const byte CurrentVersion = 1;

        public static void WriteCatalog(NetOutgoingMessage msg, byte version, PersistentSyncCatalogRowWire[] rows)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            var safeRows = rows ?? Array.Empty<PersistentSyncCatalogRowWire>();
            msg.Write(version);
            msg.Write((ushort)safeRows.Length);
            foreach (var row in safeRows)
            {
                msg.Write(row.WireId);
                msg.Write(row.DomainName ?? string.Empty);
                msg.Write(row.AuthorityPolicy);
                msg.Write(row.MaterializationSlot);
                msg.Write(row.RequiredCapabilities);
                msg.Write(row.ProducerRequiredCapabilities);
                msg.Write(row.InitialSyncGameModes);
            }
        }

        public static void ReadCatalog(NetIncomingMessage msg, out byte version, out PersistentSyncCatalogRowWire[] rows)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            version = msg.ReadByte();
            var count = msg.ReadUInt16();
            if (count == 0)
            {
                rows = Array.Empty<PersistentSyncCatalogRowWire>();
                return;
            }

            rows = new PersistentSyncCatalogRowWire[count];
            for (var i = 0; i < count; i++)
            {
                rows[i] = new PersistentSyncCatalogRowWire
                {
                    WireId = msg.ReadUInt16(),
                    DomainName = msg.ReadString(),
                    AuthorityPolicy = msg.ReadByte(),
                    MaterializationSlot = msg.ReadByte(),
                    RequiredCapabilities = msg.ReadUInt32(),
                    ProducerRequiredCapabilities = msg.ReadUInt32(),
                    InitialSyncGameModes = msg.ReadUInt16()
                };
            }
        }

        public static int GetCatalogByteCount(PersistentSyncCatalogRowWire[] rows)
        {
            var safeRows = rows ?? Array.Empty<PersistentSyncCatalogRowWire>();
            var n = sizeof(byte) + sizeof(ushort);
            foreach (var row in safeRows)
            {
                n += sizeof(ushort)
                     + (row.DomainName ?? string.Empty).GetByteCount()
                     + sizeof(byte) * 2
                     + sizeof(uint) * 2
                     + sizeof(ushort);
            }

            return n;
        }
    }
}
