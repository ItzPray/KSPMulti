using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;
using Server.Client;
using Server.Log;
using Server.Message.Base;
using Server.System;
using Server.System.PersistentSync;

namespace Server.Message
{
    public class ShareProgressMsgReader : ReaderBase
    {
        public override void HandleMessage(ClientStructure client, IClientMessageBase message)
        {
            var data = (ShareProgressBaseMsgData)message.Data;
            switch (data.ShareProgressMessageType)
            {
                case ShareProgressMessageType.ScienceSubjectUpdate:
                    ShareScienceSubjectSystem.ScienceSubjectReceived(client, (ShareProgressScienceSubjectMsgData)data);
                    break;
                case ShareProgressMessageType.TechnologyUpdate:
                    ShareTechnologySystem.TechnologyReceived(client, (ShareProgressTechnologyMsgData)data);
                    break;
                case ShareProgressMessageType.ContractsUpdate:
                    if (PersistentSyncRegistry.IsPersistentSyncInitialized)
                    {
                        LunaLog.Warning($"[PersistentSync] ShareProgress contract fallback received from {client.PlayerName}; routing through canonical contract authority instead of peer truth.");
                    }

                    ShareContractsSystem.ContractsReceived(client, (ShareProgressContractsMsgData)data);
                    break;
                case ShareProgressMessageType.AchievementsUpdate:
                    ShareAchievementsSystem.AchievementsReceived(client, (ShareProgressAchievementsMsgData)data);
                    break;
                case ShareProgressMessageType.StrategyUpdate:
                    ShareStrategySystem.StrategyReceived(client, (ShareProgressStrategyMsgData)data);
                    break;
                case ShareProgressMessageType.FacilityUpgrade:
                    if (PersistentSyncRegistry.IsPersistentSyncInitialized)
                    {
                        LunaLog.Error("[PersistentSync] bypass guard: ShareProgress facility path received; UpgradeableFacilities now converge via PersistentSync snapshots. Message ignored.");
                    }
                    else
                    {
                        ShareUpgradeableFacilitiesSystem.UpgradeReceived(client, (ShareProgressFacilityUpgradeMsgData)data);
                    }

                    break;
                case ShareProgressMessageType.PartPurchase:
                    SharePartPurchaseSystem.PurchaseReceived(client, (ShareProgressPartPurchaseMsgData)data);
                    break;
                case ShareProgressMessageType.ExperimentalPart:
                    ShareExperimentalPartSystem.ExperimentalPartReceived(client, (ShareProgressExperimentalPartMsgData)data);
                    break;
                case ShareProgressMessageType.FundsUpdate:
                case ShareProgressMessageType.ScienceUpdate:
                case ShareProgressMessageType.ReputationUpdate:
                    if (PersistentSyncRegistry.IsPersistentSyncInitialized)
                    {
                        LunaLog.Error($"[PersistentSync] bypass guard: ShareProgress scalar path {data.ShareProgressMessageType} received; Funds/Science/Reputation use PersistentSync. Message ignored.");
                    }

                    break;
            }
        }
    }
}
