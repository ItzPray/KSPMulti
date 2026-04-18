using System.Collections.Generic;

namespace LmpCommon
{
    public class IgnoredScenarios
    {
        public static List<string> IgnoreReceive { get; } = new List<string>
        {
            "ScenarioDiscoverableObjects", //Asteroids have their own system
            "ScenarioCustomWaypoints", //Don't sync this
            //Owned by the PersistentSync Technology + PartPurchases domains; if the server ever relays
            //R&D through generic scenario sync the client's ProtoScenarioModule would carry stale
            //techStates/partsPurchased and KSP would rebuild R&D.Instance from it, silently wiping
            //the authoritative state PersistentSync had already applied.
            "ResearchAndDevelopment",
        };

        public static List<string> IgnoreSend { get; } = new List<string>
        {
            "ScenarioNewGameIntro", //Do not send this scenario as it just contains true/false in case we accepted the tutorial
            "ScenarioDiscoverableObjects", //Asteroids have their own system
            "ScenarioCustomWaypoints",//Don't sync this
            "ContractSystem", //This scenario has its own handling system
            "Funding",//This scenario has its own handling system
            "ProgressTracking",//This scenario has its own handling system
            "Reputation",//This scenario has its own handling system
            "ResearchAndDevelopment",//This scenario has its own handling system
            "ScenarioDestructibles",//This scenario has its own handling system
            "ScenarioUpgradeableFacilities",//This scenario has its own handling system
            "StrategySystem"//This scenario has its own handling system
        };
    }
}
