using System.Collections.Generic;

namespace LmpCommon
{
    public class IgnoredScenarios
    {
        public static List<string> IgnoreReceive { get; } = new List<string>
        {
            "ScenarioDiscoverableObjects", //Asteroids have their own system
            "ScenarioCustomWaypoints", //Don't sync this
            //Owned by PersistentSync Technology + PartPurchases. Generic scenario receive for R&D can
            //overwrite mid-session with a stale blob. The client still needs the in-game proto to match
            //live R&D.Instance after PersistentSync applies — see ResearchAndDevelopmentProtoMirror on the client.
            "ResearchAndDevelopment",
            //Owned by PersistentSync Contracts + ShareContracts (same pattern as R&D). The server scenario bundle
            //can carry an empty or stale ContractSystem proto; applying it after PersistentSync.ReplaceContractsFromSnapshot
            //wipes live offers and Mission Control stays empty on first join / reconnect.
            "ContractSystem",
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
