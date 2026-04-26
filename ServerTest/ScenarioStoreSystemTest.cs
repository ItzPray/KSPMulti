using LunaConfigNode.CfgNode;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.System;
using Server.System.Scenario;
using System;
using System.IO;
using System.Threading;

namespace ServerTest
{
    [TestClass]
    public class ScenarioStoreSystemTest
    {
        [TestMethod]
        public void BackupScenarios_WaitsForConfigTreeAccessLockBeforeSerializing()
        {
            var previousPath = ScenarioSystem.ScenariosPath;
            var tempPath = Path.Combine(Path.GetTempPath(), $"KSPMultiScenarioStoreTest_{Guid.NewGuid():N}");
            var backupStarted = new ManualResetEventSlim(false);
            var backupCompleted = new ManualResetEventSlim(false);
            Exception backupException = null;

            Directory.CreateDirectory(tempPath);

            try
            {
                ScenarioSystem.ScenariosPath = tempPath;

                lock (ScenarioStoreSystem.ConfigTreeAccessLock)
                {
                    ScenarioStoreSystem.CurrentScenarios.Clear();
                    ScenarioStoreSystem.CurrentScenarios["RaceScenario"] = new ConfigNode(
                        "ScenarioModule\n{\n\tname = RaceScenario\n}\n");
                }

                Thread backupThread;
                lock (ScenarioStoreSystem.ConfigTreeAccessLock)
                {
                    backupThread = new Thread(() =>
                    {
                        backupStarted.Set();
                        try
                        {
                            ScenarioStoreSystem.BackupScenarios();
                        }
                        catch (Exception ex)
                        {
                            backupException = ex;
                        }
                        finally
                        {
                            backupCompleted.Set();
                        }
                    });

                    backupThread.Start();
                    Assert.IsTrue(backupStarted.Wait(5000), "Backup thread did not start.");
                    Assert.IsFalse(
                        backupCompleted.Wait(100),
                        "BackupScenarios serialized while another scenario tree mutation held the store lock.");

                    ScenarioStoreSystem.CurrentScenarios["RaceScenario"].CreateValue(
                        new CfgNodeValue<string, string>("writtenWhileLockHeld", "true"));
                }

                Assert.IsTrue(backupCompleted.Wait(60000), "Backup thread did not finish.");
                backupThread.Join(60000);
                if (backupException != null)
                {
                    Assert.Fail(backupException.ToString());
                }

                var backupFile = Path.Combine(tempPath, $"RaceScenario{ScenarioSystem.ScenarioFileFormat}");
                var backupText = File.ReadAllText(backupFile);
                StringAssert.Contains(backupText, "writtenWhileLockHeld = true");
            }
            finally
            {
                ScenarioSystem.ScenariosPath = previousPath;
                lock (ScenarioStoreSystem.ConfigTreeAccessLock)
                {
                    ScenarioStoreSystem.CurrentScenarios.Clear();
                }

                if (Directory.Exists(tempPath))
                {
                    Directory.Delete(tempPath, true);
                }
            }
        }

        [TestMethod]
        public void RawConfigNodeInsertOrUpdate_MakesDeployedScienceImmediatelyAvailableForLateJoinSync()
        {
            const string scenarioName = "DeployedScience";
            const string deployedScienceScenario =
                "ScenarioModule\n" +
                "{\n" +
                "\tname = DeployedScience\n" +
                "\tscene = 7, 8, 5, 6\n" +
                "\tclusterId = 42\n" +
                "\tlastScienceTime = 12345\n" +
                "\tScienceCluster\n" +
                "\t{\n" +
                "\t\tclusterID = 42\n" +
                "\t\tscience = 1.25\n" +
                "\t}\n" +
                "}\n";

            try
            {
                lock (ScenarioStoreSystem.ConfigTreeAccessLock)
                {
                    ScenarioStoreSystem.CurrentScenarios.Clear();
                }

                ScenarioDataUpdater.RawConfigNodeInsertOrUpdate(scenarioName, deployedScienceScenario);

                var storedScenario = ScenarioStoreSystem.GetScenarioInConfigNodeFormat(scenarioName);
                Assert.IsNotNull(storedScenario, "A late join scenario sync can run immediately after a DeployedScience update.");
                StringAssert.Contains(storedScenario, "name = DeployedScience");
                StringAssert.Contains(storedScenario, "clusterID = 42");
                StringAssert.Contains(storedScenario, "science = 1.25");
            }
            finally
            {
                lock (ScenarioStoreSystem.ConfigTreeAccessLock)
                {
                    ScenarioStoreSystem.CurrentScenarios.Clear();
                }
            }
        }
    }
}
