using LunaConfigNode.CfgNode;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.Command.Command;
using Server.System;
using Server.System.Vessel.Classes;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ServerTest
{
    [TestClass]
    public class ContractVesselReferenceSystemTest
    {
        private static readonly string XmlExamplePath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
            "XmlExampleFiles",
            "Others");

        [TestMethod]
        public void ActiveRepairContractReferenceProtectsGeneratedTargetVessel()
        {
            var contractGuid = Guid.NewGuid();
            var repairTargetVesselId = Guid.NewGuid();
            var contractSystem = new ConfigNode(
                "ScenarioModule\n" +
                "{\n" +
                "\tname = ContractSystem\n" +
                "\tCONTRACTS\n" +
                "\t{\n" +
                "\t\tCONTRACT\n" +
                "\t\t{\n" +
                $"\t\t\tguid = {contractGuid}\n" +
                "\t\t\ttype = VesselRepairContract\n" +
                "\t\t\tstate = Active\n" +
                "\t\t\tPARAM\n" +
                "\t\t\t{\n" +
                "\t\t\t\tname = RepairPartParameter\n" +
                $"\t\t\t\tvesselID = {repairTargetVesselId}\n" +
                "\t\t\t}\n" +
                "\t\t}\n" +
                "\t}\n" +
                "}\n");

            var protectedVesselIds = ContractVesselReferenceSystem.GetActiveContractReferencedVessels(contractSystem);

            Assert.IsTrue(
                protectedVesselIds.Contains(repairTargetVesselId),
                "A generated repair target referenced by an active contract must survive server cleanup after restart.");
            Assert.IsFalse(
                protectedVesselIds.Contains(contractGuid),
                "The contract row GUID is not a vessel identity and must not be treated as one.");
        }

        [TestMethod]
        public void OfferedRepairContractReferenceDoesNotProtectUnacceptedTargetVessel()
        {
            var repairTargetVesselId = Guid.NewGuid();
            var contractSystem = new ConfigNode(
                "ScenarioModule\n" +
                "{\n" +
                "\tname = ContractSystem\n" +
                "\tCONTRACTS\n" +
                "\t{\n" +
                "\t\tCONTRACT\n" +
                "\t\t{\n" +
                $"\t\t\tguid = {Guid.NewGuid()}\n" +
                "\t\t\ttype = VesselRepairContract\n" +
                "\t\t\tstate = Offered\n" +
                "\t\t\tPARAM\n" +
                "\t\t\t{\n" +
                "\t\t\t\tname = RepairPartParameter\n" +
                $"\t\t\t\tvesselID = {repairTargetVesselId}\n" +
                "\t\t\t}\n" +
                "\t\t}\n" +
                "\t}\n" +
                "}\n");

            var protectedVesselIds = ContractVesselReferenceSystem.GetActiveContractReferencedVessels(contractSystem);

            Assert.IsFalse(
                protectedVesselIds.Contains(repairTargetVesselId),
                "Only active contracts should protect generated vessels from cleanup.");
        }

        [TestMethod]
        public void ActiveContractReferenceAcceptsCompactGuidFormatFromNestedValues()
        {
            var repairTargetVesselId = Guid.NewGuid();
            var contractSystem = new ConfigNode(
                "ScenarioModule\n" +
                "{\n" +
                "\tname = ContractSystem\n" +
                "\tCONTRACTS\n" +
                "\t{\n" +
                "\t\tCONTRACT\n" +
                "\t\t{\n" +
                $"\t\t\tguid = {Guid.NewGuid()}\n" +
                "\t\t\ttype = RecoverAsset\n" +
                "\t\t\tstate = Active\n" +
                "\t\t\tPARAM\n" +
                "\t\t\t{\n" +
                "\t\t\t\tname = RecoverAssetParameter\n" +
                $"\t\t\t\tvalues = target,{repairTargetVesselId:N},repair\n" +
                "\t\t\t}\n" +
                "\t\t}\n" +
                "\t}\n" +
                "}\n");

            var protectedVesselIds = ContractVesselReferenceSystem.GetActiveContractReferencedVessels(contractSystem);

            Assert.IsTrue(protectedVesselIds.Contains(repairTargetVesselId));
        }

        [TestMethod]
        public void DekesslerKeepsDamagedSatelliteReferencedByActiveRepairContract()
        {
            var damagedSatelliteId = Guid.NewGuid();
            var damagedSatelliteText = File.ReadAllText(Path.Combine(XmlExamplePath, "99969baa-2618-49fa-a197-2c0c995ad3e0.txt"))
                .Replace("type = Relay", "type = Debris");

            try
            {
                VesselStoreSystem.CurrentVessels.Clear();
                lock (ScenarioStoreSystem.ConfigTreeAccessLock)
                {
                    ScenarioStoreSystem.CurrentScenarios.Clear();
                    ScenarioStoreSystem.CurrentScenarios["ContractSystem"] = CreateRepairContractSystem(damagedSatelliteId, "Active");
                }

                VesselStoreSystem.CurrentVessels[damagedSatelliteId] = new Vessel(damagedSatelliteText);

                new DekesslerCommand().Execute(string.Empty);

                Assert.IsTrue(
                    VesselStoreSystem.CurrentVessels.ContainsKey(damagedSatelliteId),
                    "The damaged satellite spawned by an active repair contract must not be removed as generic debris after restart.");
            }
            finally
            {
                VesselStoreSystem.CurrentVessels.Clear();
                lock (ScenarioStoreSystem.ConfigTreeAccessLock)
                {
                    ScenarioStoreSystem.CurrentScenarios.Clear();
                }
            }
        }

        private static ConfigNode CreateRepairContractSystem(Guid repairTargetVesselId, string state)
        {
            return new ConfigNode(
                "ScenarioModule\n" +
                "{\n" +
                "\tname = ContractSystem\n" +
                "\tCONTRACTS\n" +
                "\t{\n" +
                "\t\tCONTRACT\n" +
                "\t\t{\n" +
                $"\t\t\tguid = {Guid.NewGuid()}\n" +
                "\t\t\ttype = VesselRepairContract\n" +
                $"\t\t\tstate = {state}\n" +
                "\t\t\tPARAM\n" +
                "\t\t\t{\n" +
                "\t\t\t\tname = RepairPartParameter\n" +
                $"\t\t\t\tvesselID = {repairTargetVesselId}\n" +
                "\t\t\t}\n" +
                "\t\t}\n" +
                "\t}\n" +
                "}\n");
        }
    }
}
