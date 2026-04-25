using Microsoft.VisualStudio.TestTools.UnitTesting;
using LmpCommon.Message.Data.Vessel;
using Server.System;
using Server.System.Vessel;
using Server.System.Vessel.Classes;
using ServerTest.Extension;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace ServerTest
{
    [TestClass]
    public class VesselTest
    {
        /// <summary>Root of <c>XmlExampleFiles</c> copied next to the test assembly (net5.0 output).</summary>
        private static readonly string XmlExampleRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
            "XmlExampleFiles");

        private static readonly string XmlExamplePath = Path.Combine(XmlExampleRoot, "Others");

        [TestMethod]
        public void TestCreateVessel()
        {
            foreach (var file in Directory.GetFiles(XmlExamplePath))
            {
                var vessel = new Vessel(File.ReadAllText(file));
                Assert.IsNotNull(vessel);
            }
        }

        [TestMethod]
        public void TestToStringVessel()
        {
            foreach (var file in Directory.GetFiles(XmlExamplePath))
            {
                var originalFile = File.ReadAllText(file).ToUnixString();
                var vessel = new Vessel(File.ReadAllText(file));
                var vesselBackToText = vessel.ToString().ToUnixString();

                Assert.AreEqual(originalFile, vesselBackToText);
            }
        }

        [TestMethod]
        public void PartAllowsDuplicateModuleTypeNames()
        {
            var path = Path.Combine(XmlExampleRoot, "Special", "duplicate-fx-modules-vessel.txt");
            var vessel = new Vessel(File.ReadAllText(path));
            var modules = vessel.GetPart(99).GetModulesNamed("FXModuleThrottleEffects").ToList();
            Assert.AreEqual(2, modules.Count);
            Assert.AreEqual("a", modules[0].GetValue("testField").Value);
            Assert.AreEqual("b", modules[1].GetValue("testField").Value);
        }

        [TestMethod]
        public void TestEditVessel()
        {
            var vessel = new Vessel(File.ReadAllText(Path.Combine(XmlExamplePath, "99969baa-2618-49fa-a197-2c0c995ad3e0.txt")));

            vessel.GetPart(1985344313).GetSingleModule("ModuleProceduralFairing").Values.Update("fsm", "st_flight_deployed");
            var extraNodes = vessel.GetPart(1985344313).GetSingleModule("ModuleProceduralFairing").Nodes;
            extraNodes.Remove("XSECTION");

            vessel.CtrlState.UpdateValue("pitch", "newPitch");
            vessel.GetPart(3631576085).Fields.GetSingle("name").Value = "newName";
            vessel.GetPart(3631576085).GetSingleModule("ModuleCommand").Values.Update("hibernation", "newHibernation");

            vessel = new Vessel(vessel.ToString());

            Assert.AreEqual("newPitch", vessel.CtrlState.GetValue("pitch").Value);
            Assert.AreEqual("newName", vessel.GetPart(3631576085).Fields.GetSingle("name").Value);
            Assert.AreEqual("newHibernation", vessel.GetPart(3631576085).GetSingleModule("ModuleCommand").Values.GetSingle("hibernation").Value);
            Assert.AreEqual("st_flight_deployed", vessel.GetPart(1985344313).GetSingleModule("ModuleProceduralFairing").Values.GetSingle("fsm").Value);
            Assert.AreEqual(0, vessel.GetPart(1985344313).GetSingleModule("ModuleProceduralFairing").Nodes.GetSeveral("XSECTION").Count);
        }

        [TestMethod]
        public void GetPart_ReturnsNullForMissingPart()
        {
            var vessel = new Vessel(File.ReadAllText(Path.Combine(XmlExamplePath, "99969baa-2618-49fa-a197-2c0c995ad3e0.txt")));

            Assert.IsNull(vessel.GetPart(uint.MaxValue));
        }

        [TestMethod]
        public void WriteResourceDataToFile_SkipsMissingPartsAndAppliesValidResources()
        {
            var vesselId = Guid.NewGuid();
            var vessel = new Vessel(File.ReadAllText(Path.Combine(XmlExamplePath, "99969baa-2618-49fa-a197-2c0c995ad3e0.txt")));
            var msgData = CreateVesselResourceMsgData();
            msgData.VesselId = vesselId;
            msgData.ResourcesCount = 2;
            msgData.Resources = new[]
            {
                new VesselResourceInfo
                {
                    PartFlightId = uint.MaxValue,
                    ResourceName = "ElectricCharge",
                    Amount = 1,
                    FlowState = true
                },
                new VesselResourceInfo
                {
                    PartFlightId = 3631576085,
                    ResourceName = "ElectricCharge",
                    Amount = 2.5,
                    FlowState = false
                }
            };

            try
            {
                VesselStoreSystem.CurrentVessels.Clear();
                VesselContext.RemovedVessels.Clear();
                VesselStoreSystem.CurrentVessels.TryAdd(vesselId, vessel);

                VesselDataUpdater.WriteResourceDataToFile(msgData);
                Thread.Sleep(250);

                var resourceNode = vessel.GetPart(3631576085).GetResourceNode("ElectricCharge");
                Assert.AreEqual("2.5", resourceNode.GetValue("amount").Value);
                Assert.AreEqual("False", resourceNode.GetValue("flowState").Value);
            }
            finally
            {
                VesselStoreSystem.CurrentVessels.Clear();
                VesselContext.RemovedVessels.Clear();
            }
        }

        [TestMethod]
        public void RawConfigNodeInsertOrUpdate_DoesNotResurrectRemovedVessel()
        {
            var vesselId = Guid.NewGuid();
            var vesselText = File.ReadAllText(Path.Combine(XmlExamplePath, "99969baa-2618-49fa-a197-2c0c995ad3e0.txt"));

            try
            {
                VesselStoreSystem.CurrentVessels.Clear();
                VesselContext.RemovedVessels.Clear();
                VesselContext.RemovedVessels.TryAdd(vesselId, 0);

                VesselDataUpdater.RawConfigNodeInsertOrUpdate(vesselId, vesselText);
                Thread.Sleep(250);

                Assert.IsFalse(VesselStoreSystem.CurrentVessels.ContainsKey(vesselId));
            }
            finally
            {
                VesselStoreSystem.CurrentVessels.Clear();
                VesselContext.RemovedVessels.Clear();
            }
        }

        private static VesselResourceMsgData CreateVesselResourceMsgData()
        {
            return (VesselResourceMsgData)typeof(VesselResourceMsgData)
                .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                .Invoke(null);
        }
    }
}
