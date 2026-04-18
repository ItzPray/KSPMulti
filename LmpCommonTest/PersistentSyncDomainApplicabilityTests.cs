using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace LmpCommonTest
{
    [TestClass]
    public class PersistentSyncDomainApplicabilityTests
    {
        [TestMethod]
        public void CareerOptimisticIncludesAllProgressionDomains()
        {
            var caps = PersistentSyncSessionCapabilities.OptimisticForServerGameMode(GameMode.Career);
            var domains = PersistentSyncDomainApplicability
                .GetRequiredDomainsForInitialSync(GameMode.Career, caps)
                .ToArray();

            var expected = new[]
            {
                PersistentSyncDomainId.Funds,
                PersistentSyncDomainId.Science,
                PersistentSyncDomainId.Reputation,
                PersistentSyncDomainId.Strategy,
                PersistentSyncDomainId.Achievements,
                PersistentSyncDomainId.ScienceSubjects,
                PersistentSyncDomainId.Technology,
                PersistentSyncDomainId.ExperimentalParts,
                PersistentSyncDomainId.PartPurchases,
                PersistentSyncDomainId.UpgradeableFacilities,
                PersistentSyncDomainId.Contracts
            };

            CollectionAssert.AreEqual(expected, domains);
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
                    PersistentSyncDomainId.Science,
                    PersistentSyncDomainId.Achievements,
                    PersistentSyncDomainId.ScienceSubjects,
                    PersistentSyncDomainId.Technology,
                    PersistentSyncDomainId.ExperimentalParts,
                    PersistentSyncDomainId.PartPurchases
                },
                domains);
        }

        [TestMethod]
        public void SandboxYieldsNoDomains()
        {
            var caps = PersistentSyncSessionCapabilities.OptimisticForServerGameMode(GameMode.Sandbox);
            var domains = PersistentSyncDomainApplicability
                .GetRequiredDomainsForInitialSync(GameMode.Sandbox, caps)
                .ToArray();

            Assert.AreEqual(0, domains.Length);
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

            Assert.IsFalse(domains.Contains(PersistentSyncDomainId.Science));
            Assert.IsFalse(domains.Contains(PersistentSyncDomainId.ScienceSubjects));
            Assert.IsTrue(domains.Contains(PersistentSyncDomainId.Funds));
        }

        [TestMethod]
        public void CareerShareProducersMatchCareerScenarioFlags()
        {
            var caps = PersistentSyncSessionCapabilities.OptimisticForServerGameMode(GameMode.Career);
            Assert.IsTrue(PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainId.Funds, GameMode.Career, in caps));
            Assert.IsTrue(PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainId.Reputation, GameMode.Career, in caps));
            Assert.IsTrue(PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainId.Strategy, GameMode.Career, in caps));
            Assert.IsTrue(PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainId.UpgradeableFacilities, GameMode.Career, in caps));
            Assert.IsTrue(PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainId.Contracts, GameMode.Career, in caps));
        }

        [TestMethod]
        public void ScienceModeShareProducerRejectsCareerOnlyFunds()
        {
            var caps = PersistentSyncSessionCapabilities.OptimisticForServerGameMode(GameMode.Science);
            Assert.IsFalse(PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainId.Funds, GameMode.Science, in caps));
            Assert.IsFalse(PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainId.Strategy, GameMode.Science, in caps));
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
                PersistentSyncDomainId.PartPurchases,
                GameMode.Career,
                in caps));

            Assert.IsFalse(PersistentSyncDomainApplicability.IsDomainApplicableForShareProducer(
                PersistentSyncDomainId.PartPurchases,
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
