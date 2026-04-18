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
                    if (PersistentSyncRegistry.IsPersistentSyncInitialized)
                    {
                        LunaLog.Error($"[PersistentSync] bypass guard: ShareProgress science-subject path received from {client.PlayerName}; ScienceSubjects now converge only via PersistentSync snapshots. Message ignored.");
                        break;
                    }

                    ShareScienceSubjectSystem.ScienceSubjectReceived(client, (ShareProgressScienceSubjectMsgData)data);
                    break;
                case ShareProgressMessageType.TechnologyUpdate:
                    if (PersistentSyncRegistry.IsPersistentSyncInitialized)
                    {
                        LunaLog.Error($"[PersistentSync] bypass guard: ShareProgress technology path received from {client.PlayerName}; Technology now converges only via PersistentSync intents/snapshots. Message ignored.");
                        break;
                    }

                    ShareTechnologySystem.TechnologyReceived(client, (ShareProgressTechnologyMsgData)data);
                    break;
                case ShareProgressMessageType.ContractsUpdate:
                    if (PersistentSyncRegistry.IsPersistentSyncInitialized)
                    {
                        LunaLog.Error($"[PersistentSync] bypass guard: ShareProgress contracts path received from {client.PlayerName}; Contracts now converge only via PersistentSync intents/snapshots. Message ignored.");
                        break;
                    }

                    ShareContractsSystem.ContractsReceived(client, (ShareProgressContractsMsgData)data);
                    break;
                case ShareProgressMessageType.AchievementsUpdate:
                    if (PersistentSyncRegistry.IsPersistentSyncInitialized)
                    {
                        LunaLog.Error($"[PersistentSync] bypass guard: ShareProgress achievements path received from {client.PlayerName}; Achievements now converge only via PersistentSync snapshots. Message ignored.");
                        break;
                    }

                    ShareAchievementsSystem.AchievementsReceived(client, (ShareProgressAchievementsMsgData)data);
                    break;
                case ShareProgressMessageType.StrategyUpdate:
                    if (PersistentSyncRegistry.IsPersistentSyncInitialized)
                    {
                        LunaLog.Error($"[PersistentSync] bypass guard: ShareProgress strategy path received from {client.PlayerName}; Strategy now converge only via PersistentSync snapshots. Message ignored.");
                        break;
                    }

                    ShareStrategySystem.StrategyReceived(client, (ShareProgressStrategyMsgData)data);
                    break;
                case ShareProgressMessageType.FacilityUpgrade:
                    if (PersistentSyncRegistry.IsPersistentSyncInitialized)
                    {
                        LunaLog.Error($"[PersistentSync] bypass guard: ShareProgress facility path received from {client.PlayerName}; UpgradeableFacilities now converge only via PersistentSync. Message ignored.");
                        break;
                    }

                    ShareUpgradeableFacilitiesSystem.UpgradeReceived(client, (ShareProgressFacilityUpgradeMsgData)data);
                    break;
                case ShareProgressMessageType.PartPurchase:
                    if (PersistentSyncRegistry.IsPersistentSyncInitialized)
                    {
                        LunaLog.Error($"[PersistentSync] bypass guard: ShareProgress part-purchase path received from {client.PlayerName}; PartPurchases now converge only via PersistentSync snapshots. Message ignored.");
                        break;
                    }

                    SharePartPurchaseSystem.PurchaseReceived(client, (ShareProgressPartPurchaseMsgData)data);
                    break;
                case ShareProgressMessageType.ExperimentalPart:
                    if (PersistentSyncRegistry.IsPersistentSyncInitialized)
                    {
                        LunaLog.Error($"[PersistentSync] bypass guard: ShareProgress experimental-part path received from {client.PlayerName}; ExperimentalParts now converge only via PersistentSync snapshots. Message ignored.");
                        break;
                    }

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
