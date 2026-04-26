using LunaConfigNode.CfgNode;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.System;
using System;
using System.Linq;

namespace ServerTest
{
    [TestClass]
    public class ContractVesselReferenceSystemTest
    {
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
    }
}
