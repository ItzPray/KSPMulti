using KSP.UI.Screens;
using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Extensions;
using LmpClient.Systems.PersistentSync;
using LmpClient.Systems.ShareFunds;
using LmpClient.Systems.ShareReputation;
using LmpClient.Systems.ShareScience;
using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;
using LmpCommon.PersistentSync;
using System;
using System.Collections.Concurrent;
using System.Globalization;

namespace LmpClient.Systems.ShareStrategy
{
    public class ShareStrategyMessageHandler : SubSystem<ShareStrategySystem>, IMessageHandler
    {
        public ConcurrentQueue<IServerMessageBase> IncomingMessages { get; set; } = new ConcurrentQueue<IServerMessageBase>();

        public void HandleMessage(IServerMessageBase msg)
        {
            if (!(msg.Data is ShareProgressBaseMsgData msgData)) return;
            if (msgData.ShareProgressMessageType != ShareProgressMessageType.StrategyUpdate) return;
            if (PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainId.Strategy))
            {
                LunaLog.LogWarning("[KSPMP] Ignoring legacy StrategyUpdate because persistent sync owns strategy convergence.");
                return;
            }

            if (msgData is ShareProgressStrategyMsgData data)
            {
                var strategy = new StrategyInfo(data.Strategy); //create a copy of the strategyInfo object so it will not change in the future.
                LunaLog.Log($"Queue StrategyUpdate with: {strategy.Name}");
                System.QueueAction(() =>
                {
                    ApplyStrategySnapshot(strategy.Name, strategy.Data, strategy.Data.Length, "LegacyShareProgressFallback", true);
                });
            }
        }

        public static bool ApplyStrategySnapshot(string strategyName, byte[] data, int numBytes, string source, bool refreshUi)
        {
            return ShareStrategySystem.Singleton.ApplyStrategySnapshot(new LmpCommon.PersistentSync.StrategySnapshotInfo
            {
                Name = strategyName,
                Data = data
            }, source, refreshUi);
        }

        /// <summary>
        /// Convert a byte array to a ConfigNode.
        /// If anything goes wrong it will return null.
        /// </summary>
        public static ConfigNode ConvertByteArrayToConfigNode(byte[] data, int numBytes)
        {
            ConfigNode node;
            try
            {
                node = data.DeserializeToConfigNode(numBytes);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[KSPMP]: Error while deserializing strategy configNode: {e}");
                return null;
            }

            if (node == null)
            {
                LunaLog.LogError("[KSPMP]: Error, the strategy configNode was null.");
                return null;
            }

            if (!node.HasValue("isActive"))
            {
                LunaLog.LogError("[KSPMP]: Error, the strategy configNode is invalid (isActive missing).");
                return null;
            }

            return node;
        }
    }
}

