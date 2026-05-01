using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace LmpCommonTest
{
    [TestClass]
    public class PersistentSyncDomainApplicabilityTests
    {
        [TestInitialize]
        public void Setup()
        {
            PersistentSyncTestDomainCatalog.Configure();
        }

        [TestMethod]
        public void CareerOptimisticIncludesAllProgressionDomains()
        {
            var caps = PersistentSyncSessionCapabilities.OptimisticForServerGameMode(GameMode.Career);
            var domains = PersistentSyncDomainApplicability
                .GetRequiredDomainsForInitialSync(GameMode.Career, caps)
                .ToArray();

            var expected = new[]
            {
                PersistentSyncDomainNames.Funds,
                PersistentSyncDomainNames.Science,
                PersistentSyncDomainNames.Reputation,
                PersistentSyncDomainNames.Strategy,
                PersistentSyncDomainNames.Achievements,
                PersistentSyncDomainNames.ScienceSubjects,
                PersistentSyncDomainNames.Technology,
                PersistentSyncDomainNames.ExperimentalParts,
                PersistentSyncDomainNames.PartPurchases,
                PersistentSyncDomainNames.UpgradeableFacilities,
                PersistentSyncDomainNames.Contracts
            };

            CollectionAssert.AreEquivalent(expected, domains);
        }

        [TestMethod]
        public void ScienceOptimisticMatchesRnDAdjacentBundle()
        {
            var caps = PersistentSyncSessionCapabilities.OptimisticForServerGameMode(GameMode.Science);
            var domains = PersistentSyncDomainApplicability
                .GetRequiredDomainsForInitialSync(GameMode.Science, caps)
                .ToArray();

            CollectionAssert.AreEquivalent(
                new[]
                {
                    PersistentSyncDomainNames.Science,
                    PersistentSyncDomainNames.Achievements,
                    PersistentSyncDomainNames.ScienceSubjects,
                    PersistentSyncDomainNames.Technology,
                    PersistentSyncDomainNames.ExperimentalParts,
                    PersistentSyncDomainNames.PartPurchases
                },
                domains);
        }

        [TestMethod]
        public void SandboxYieldsOnlyGameLaunchId()
        {
            var caps = PersistentSyncSessionCapabilities.OptimisticForServerGameMode(GameMode.Sandbox);
            var domains = PersistentSyncDomainApplicability
                .GetRequiredDomainsForInitialSync(GameMode.Sandbox, caps)
                .ToArray();

            CollectionAssert.AreEqual(new[] { PersistentSyncDomainNames.GameLaunchId }, domains);
        }

        [TestMethod]
        public void MissingRnDScenarioExcludesRnDBackedDomainsEvenInCareer()
        {
            var caps = new PersistentSyncSessionCapabilities
            {
                HasFundingScenario = true,
                HasReputationScenario = true,
                HasResearchAndDevelopmentScenario = false,
                HasProgressTrackingScenario = true,
                HasContractSystemScenario = true,
                HasUpgradeableFacilitiesScenario = true,
                HasStrategySystemScenario = true,
                PartPurchaseMechanismEnabled = true
            };

            var domains = PersistentSyncDomainApplicability
                .GetRequiredDomainsForInitialSync(GameMode.Career, caps)
                .ToArray();

            Assert.IsFalse(domains.Contains(PersistentSyncDomainNames.Science));
            Assert.IsFalse(domains.Contains(PersistentSyncDomainNames.ScienceSubjects));
            Assert.IsTrue(domains.Contains(PersistentSyncDomainNames.Funds));
        }

        [TestMethod]
        public void CareerShareProducersMatchCareerScenarioFlags()
        {
            var caps = PersistentSyncSessionCapabilities.OptimisticForServerGameMode(GameMode.Career);
            Assert.IsTrue(PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainNames.Funds, GameMode.Career, in caps));
            Assert.IsTrue(PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainNames.Reputation, GameMode.Career, in caps));
            Assert.IsTrue(PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainNames.Strategy, GameMode.Career, in caps));
            Assert.IsTrue(PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainNames.UpgradeableFacilities, GameMode.Career, in caps));
            Assert.IsTrue(PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainNames.Contracts, GameMode.Career, in caps));
        }

        [TestMethod]
        public void ScienceModeShareProducerRejectsCareerOnlyFunds()
        {
            var caps = PersistentSyncSessionCapabilities.OptimisticForServerGameMode(GameMode.Science);
            Assert.IsFalse(PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainNames.Funds, GameMode.Science, in caps));
            Assert.IsFalse(PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainNames.Strategy, GameMode.Science, in caps));
        }

        [TestMethod]
        public void PartPurchaseProducerDisabledWhenDifficultyBypassesPurchases()
        {
            var caps = new PersistentSyncSessionCapabilities
            {
                HasFundingScenario = true,
                HasReputationScenario = true,
                HasResearchAndDevelopmentScenario = true,
                HasProgressTrackingScenario = true,
                HasContractSystemScenario = true,
                HasUpgradeableFacilitiesScenario = true,
                HasStrategySystemScenario = true,
                PartPurchaseMechanismEnabled = false
            };

            Assert.IsTrue(PersistentSyncDomainApplicability.IsDomainApplicableForInitialSync(
                PersistentSyncDomainNames.PartPurchases,
                GameMode.Career,
                in caps));

            Assert.IsFalse(PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainNames.PartPurchases,
                GameMode.Career,
                in caps));
        }

        [TestMethod]
        public void InitialSyncRequiredDomainsSatisfiesReconcilerCompletionContract()
        {
            var caps = PersistentSyncSessionCapabilities.OptimisticForServerGameMode(GameMode.Science);
            var required = PersistentSyncDomainApplicability
                .GetRequiredDomainsForInitialSync(GameMode.Science, caps)
                .ToArray();

            var state = new PersistentSyncReconcilerState();
            state.Reset(required);
            Assert.IsFalse(state.AreAllInitialSnapshotsApplied());

            foreach (var d in required)
            {
                state.MarkApplied(d, 1);
            }

            Assert.IsTrue(state.AreAllInitialSnapshotsApplied());
        }
    }
}