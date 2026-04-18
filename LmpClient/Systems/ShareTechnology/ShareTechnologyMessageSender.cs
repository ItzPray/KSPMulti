using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Extensions;
using LmpClient.Network;
using LmpClient.Systems.PersistentSync;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Interface;
using LmpCommon.PersistentSync;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LmpClient.Systems.ShareTechnology
{
    public class ShareTechnologyMessageSender : SubSystem<ShareTechnologySystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<ShareProgressCliMsg>(msg)));
        }

        public void SendTechnologyMessage(RDTech tech)
        {
            if (PersistentSyncSystem.Singleton != null && PersistentSyncSystem.Singleton.Enabled)
            {
                var technologies = CreateCurrentTechnologySnapshot();
                if (technologies.Count == 0)
                {
                    return;
                }

                var reason = $"TechnologyUnlock:{tech.techID}";
                PersistentSyncSystem.Singleton.MessageSender.SendTechnologyIntent(technologies.ToArray(), reason);
                return;
            }

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<ShareProgressTechnologyMsgData>();
            msgData.TechNode.Id = tech.techID;

            var configNode = ConvertTechNodeToConfigNode(tech);
            if (configNode == null) return;

            var data = configNode.Serialize();
            var numBytes = data.Length;

            msgData.TechNode.NumBytes = numBytes;
            if (msgData.TechNode.Data.Length < numBytes)
                msgData.TechNode.Data = new byte[numBytes];

            Array.Copy(data, msgData.TechNode.Data, numBytes);

            SendMessage(msgData);
        }

        private static ConfigNode ConvertTechNodeToConfigNode(RDTech techNode)
        {
            var configNode = new ConfigNode();
            try
            {
                configNode.AddValue("id", techNode.techID);
                configNode.AddValue("state", techNode.state);
                configNode.AddValue("cost", techNode.scienceCost);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: Error while saving tech node: {e}");
                return null;
            }

            return configNode;
        }

        private static List<TechnologySnapshotInfo> CreateCurrentTechnologySnapshot()
        {
            var technologies = new List<TechnologySnapshotInfo>();
            if (ResearchAndDevelopment.Instance == null || AssetBase.RnDTechTree == null)
            {
                return technologies;
            }

            var stateCounts = new Dictionary<RDTech.State, int>();
            var availableIds = new List<string>();
            var treeTechCount = 0;
            var nullStateCount = 0;

            foreach (var tech in AssetBase.RnDTechTree.GetTreeTechs().Where(t => t != null))
            {
                treeTechCount++;
                var techState = ResearchAndDevelopment.Instance.GetTechState(tech.techID);
                if (techState == null)
                {
                    nullStateCount++;
                    continue;
                }

                if (!stateCounts.ContainsKey(techState.state))
                {
                    stateCounts[techState.state] = 0;
                }
                stateCounts[techState.state]++;

                if (techState.state == RDTech.State.Unavailable)
                {
                    continue;
                }

                availableIds.Add(techState.techID);

                var configNode = new ConfigNode();
                configNode.AddValue("id", techState.techID);
                configNode.AddValue("state", techState.state);
                configNode.AddValue("cost", techState.scienceCost);

                var data = configNode.Serialize();
                technologies.Add(new TechnologySnapshotInfo
                {
                    TechId = techState.techID,
                    NumBytes = data.Length,
                    Data = data
                });
            }

            var stateSummary = string.Join(",", stateCounts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
            LunaLog.Log($"[PersistentSync] CreateCurrentTechnologySnapshot treeTechs={treeTechCount} nullState={nullStateCount} stateCounts=[{stateSummary}] sending={technologies.Count} availableIds=[{string.Join(",", availableIds.OrderBy(id => id))}]");

            return technologies;
        }
    }
}
