using LmpCommon.PersistentSync.Payloads.Contracts;
using Lidgren.Network;
using LmpCommon.Enums;
using LmpCommon.Message;
using LmpCommon.Message.Data.Settings;
using LmpCommon.Message.Server;
using LmpCommon.PersistentSync;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace LmpCommonTest
{
    [TestClass]
    public class PersistentSyncCatalogTests
    {
        private static readonly NetClient Client = new NetClient(new NetPeerConfiguration("CATALOG_WIRE_TESTS"));
        private static readonly ServerMessageFactory ServerFactory = new ServerMessageFactory();

        [TestMethod]
        public void PersistentSyncCatalogMsgData_RoundTripsViaSetingsSrvMsg()
        {
            var rows = new[]
            {
                new PersistentSyncCatalogRowWire
                {
                    WireId = 0,
                    DomainName = "Funds",
                    AuthorityPolicy = 0,
                    MaterializationSlot = 0,
                    RequiredCapabilities = 0,
                    ProducerRequiredCapabilities = 0,
                    InitialSyncGameModes = (ushort)(int)GameMode.Career
                }
            };

            var msgData = ServerFactory.CreateNewMessageData<PersistentSyncCatalogMsgData>();
            msgData.PersistentSyncCatalogWireVersion = PersistentSyncCatalogWire.CurrentVersion;
            msgData.PersistentSyncCatalogRows = rows;

            var msg = ServerFactory.CreateNew<SetingsSrvMsg>(msgData);
            var outgoing = Client.CreateMessage(msg.GetMessageSize());
            msg.Serialize(outgoing);
            var data = outgoing.ReadBytes(outgoing.LengthBytes);
            msg.Recycle();

            var incoming = Client.CreateIncomingMessage(NetIncomingMessageType.Data, data);
            incoming.LengthBytes = data.Length;

            var roundTrip = (SetingsSrvMsg)ServerFactory.Deserialize(incoming, Environment.TickCount);
            Assert.IsInstanceOfType(roundTrip.Data, typeof(PersistentSyncCatalogMsgData));
            var rt = (PersistentSyncCatalogMsgData)roundTrip.Data;
            Assert.AreEqual(PersistentSyncCatalogWire.CurrentVersion, rt.PersistentSyncCatalogWireVersion);
            Assert.AreEqual(1, rt.PersistentSyncCatalogRows.Length);
            Assert.AreEqual("Funds", rt.PersistentSyncCatalogRows[0].DomainName);
        }

        [TestMethod]
        public void PersistentSyncCatalogWire_RoundTripsRows()
        {
            var rows = new[]
            {
                new PersistentSyncCatalogRowWire
                {
                    WireId = 0,
                    DomainName = "Funds",
                    AuthorityPolicy = 0,
                    MaterializationSlot = 0,
                    RequiredCapabilities = 1,
                    ProducerRequiredCapabilities = 0,
                    InitialSyncGameModes = 2
                },
                new PersistentSyncCatalogRowWire
                {
                    WireId = 1,
                    DomainName = "Science",
                    AuthorityPolicy = 0,
                    MaterializationSlot = 0,
                    RequiredCapabilities = 4,
                    ProducerRequiredCapabilities = 0,
                    InitialSyncGameModes = 2
                }
            };

            var om = Client.CreateMessage();
            PersistentSyncCatalogWire.WriteCatalog(om, PersistentSyncCatalogWire.CurrentVersion, rows);
            var data = om.ReadBytes(om.LengthBytes);
            var im = Client.CreateIncomingMessage(NetIncomingMessageType.Data, data);
            im.LengthBytes = data.Length;
            PersistentSyncCatalogWire.ReadCatalog(im, out var version, out var roundTrip);

            Assert.AreEqual(PersistentSyncCatalogWire.CurrentVersion, version);
            Assert.AreEqual(2, roundTrip.Length);
            Assert.AreEqual("Funds", roundTrip[0].DomainName);
            Assert.AreEqual((ushort)1, roundTrip[1].WireId);
        }

        [TestMethod]
        public void PersistentSyncCatalogMerger_MatchesServerRows()
        {
            var local = new[]
            {
                new PersistentSyncDomainDefinition(
                    new PersistentSyncDomainKey("A"),
                    GameMode.Career,
                    PersistentSyncCapabilityFlags.None,
                    PersistentSyncCapabilityFlags.None,
                    PersistentSyncMaterializationSlot.None,
                    typeof(object),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    99,
                    "S1",
                    null),
                new PersistentSyncDomainDefinition(
                    new PersistentSyncDomainKey("B"),
                    GameMode.Career,
                    PersistentSyncCapabilityFlags.Funding,
                    PersistentSyncCapabilityFlags.None,
                    PersistentSyncMaterializationSlot.Funding,
                    typeof(object),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    98,
                    "S2",
                    null)
            };

            var serverRows = new[]
            {
                new PersistentSyncCatalogRowWire
                {
                    WireId = 0,
                    DomainName = "A",
                    AuthorityPolicy = 1,
                    MaterializationSlot = (byte)PersistentSyncMaterializationSlot.None,
                    RequiredCapabilities = 0,
                    ProducerRequiredCapabilities = 0,
                    InitialSyncGameModes = (ushort)(int)GameMode.Career
                },
                new PersistentSyncCatalogRowWire
                {
                    WireId = 1,
                    DomainName = "B",
                    AuthorityPolicy = 2,
                    MaterializationSlot = (byte)PersistentSyncMaterializationSlot.Funding,
                    RequiredCapabilities = (uint)PersistentSyncCapabilityFlags.Funding,
                    ProducerRequiredCapabilities = 0,
                    InitialSyncGameModes = (ushort)(int)GameMode.Career
                }
            };

            Assert.IsTrue(PersistentSyncCatalogMerger.TryMerge(local, serverRows, out var merged, out var err), err);
            Assert.AreEqual(2, merged.Length);
            Assert.AreEqual((ushort)0, merged[0].WireId);
            Assert.AreEqual("A", merged[0].Name);
            Assert.AreEqual(GameMode.Career, merged[0].InitialSyncGameModes);
            Assert.AreEqual((ushort)1, merged[1].WireId);
        }

        [TestMethod]
        public void PersistentSyncCatalogMerger_FailsOnMissingLocalDomain()
        {
            var local = new[]
            {
                new PersistentSyncDomainDefinition(
                    new PersistentSyncDomainKey("Only"),
                    GameMode.Career,
                    PersistentSyncCapabilityFlags.None,
                    PersistentSyncCapabilityFlags.None,
                    PersistentSyncMaterializationSlot.None,
                    typeof(object),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    0,
                    "S",
                    null)
            };

            var serverRows = new[]
            {
                new PersistentSyncCatalogRowWire { WireId = 0, DomainName = "NotRegisteredLocally", AuthorityPolicy = 0, MaterializationSlot = 0, RequiredCapabilities = 0, ProducerRequiredCapabilities = 0, InitialSyncGameModes = 0 }
            };

            Assert.IsFalse(PersistentSyncCatalogMerger.TryMerge(local, serverRows, out _, out var fail));
            StringAssert.Contains(fail, "missing client handler");
        }

        [TestMethod]
        public void PersistentSyncCatalogMerger_FailsOnEmptyServerCatalog()
        {
            var local = new[]
            {
                new PersistentSyncDomainDefinition(
                    new PersistentSyncDomainKey("Only"),
                    GameMode.Career,
                    PersistentSyncCapabilityFlags.None,
                    PersistentSyncCapabilityFlags.None,
                    PersistentSyncMaterializationSlot.None,
                    typeof(object),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    0,
                    "S",
                    null)
            };

            Assert.IsFalse(PersistentSyncCatalogMerger.TryMerge(local, Array.Empty<PersistentSyncCatalogRowWire>(), out _, out var fail));
            StringAssert.Contains(fail, "empty persistent sync catalog");
        }

        [TestMethod]
        public void PersistentSyncCatalogMerger_FailsOnNonContiguousWireIds()
        {
            var local = new[]
            {
                new PersistentSyncDomainDefinition(new PersistentSyncDomainKey("A"), GameMode.Career, PersistentSyncCapabilityFlags.None, PersistentSyncCapabilityFlags.None, PersistentSyncMaterializationSlot.None, typeof(object), Array.Empty<string>(), Array.Empty<string>(), 0, "S", null),
                new PersistentSyncDomainDefinition(new PersistentSyncDomainKey("B"), GameMode.Career, PersistentSyncCapabilityFlags.None, PersistentSyncCapabilityFlags.None, PersistentSyncMaterializationSlot.None, typeof(object), Array.Empty<string>(), Array.Empty<string>(), 0, "S", null)
            };

            var serverRows = new[]
            {
                new PersistentSyncCatalogRowWire { WireId = 0, DomainName = "A", AuthorityPolicy = 0, MaterializationSlot = 0, RequiredCapabilities = 0, ProducerRequiredCapabilities = 0, InitialSyncGameModes = 0 },
                new PersistentSyncCatalogRowWire { WireId = 2, DomainName = "B", AuthorityPolicy = 0, MaterializationSlot = 0, RequiredCapabilities = 0, ProducerRequiredCapabilities = 0, InitialSyncGameModes = 0 }
            };

            Assert.IsFalse(PersistentSyncCatalogMerger.TryMerge(local, serverRows, out _, out var fail));
            StringAssert.Contains(fail, "contiguous");
        }

        [TestMethod]
        public void PayloadCodecRegistrars_IncludeContractsPayloadCodecRegistrar()
        {
            var registrars = typeof(IPersistentSyncPayloadCodecRegistrar).Assembly.GetExportedTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IPersistentSyncPayloadCodecRegistrar).IsAssignableFrom(t))
                .Where(t => t.Namespace != null && t.Namespace.StartsWith("LmpCommon.PersistentSync.Payloads.", StringComparison.Ordinal))
                .ToArray();
            CollectionAssert.Contains(registrars, typeof(ContractsPayloadCodecRegistrar));
        }
    }
}
