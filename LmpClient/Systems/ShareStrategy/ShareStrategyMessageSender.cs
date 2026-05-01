using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Extensions;
using LmpClient.Network;
using LmpClient.Systems.PersistentSync;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Interface;
using LmpCommon.PersistentSync;
using Strategies;
using System;

namespace LmpClient.Systems.ShareStrategy
{
    public class ShareStrategyMessageSender : SubSystem<ShareStrategySystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<ShareProgressCliMsg>(msg)));
        }

        public void SendStrategyMessage(Strategy strategy)
        {
            var configNode = ConvertStrategyToConfigNode(strategy);
            if (configNode == null) return;

            var data = configNode.Serialize();

            if (PersistentSyncSystem.IsLiveFor<StrategyPersistentSyncClientDomain>())
            {
                PersistentSyncSystem.SendIntent<StrategyPersistentSyncClientDomain, StrategyPayload>(new StrategyPayload
                {
                    Items = new[]
                    {
                        new StrategySnapshotInfo
                        {
                            Name = strategy.Config.Name,
                            Data = data
                        }
                    }
                }, $"StrategyUpdate:{strategy.Config.Name}");
            }
        }

        private static ConfigNode ConvertStrategyToConfigNode(Strategy strategy)
        {
            var configNode = new ConfigNode();
            try
            {
                strategy.Save(configNode);
                configNode.AddValue("isActive", strategy.IsActive); //Add isActive to the node because normaly it is not saved.
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[KSPMP]: Error while saving strategy: {e}");
                return null;
            }

            return configNode;
        }


    }
}

