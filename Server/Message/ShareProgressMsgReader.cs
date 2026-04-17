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
                        LunaLog.Warning($"[PersistentSync] ShareProgress science subject fallback received from {client.PlayerName}; routing through canonical science-subject snapshot store instead of one-off relay truth.");
                    }

                    ShareScienceSubjectSystem.ScienceSubjectReceived(client, (ShareProgressScienceSubjectMsgData)data);
                    break;
                case ShareProgressMessageType.TechnologyUpdate:
                    if (PersistentSyncRegistry.IsPersistentSyncInitialized)
                    {
                        LunaLog.Warning($"[PersistentSync] ShareProgress technology fallback received from {client.PlayerName}; routing through canonical R&D snapshot store instead of one-off peer unlock truth.");
                    }

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
                    if (PersistentSyncRegistry.IsPersistentSyncInitialized)
                    {
                        LunaLog.Warning($"[PersistentSync] ShareProgress achievements fallback received from {client.PlayerName}; routing through canonical achievements snapshot store instead of one-off relay truth.");
                    }

                    ShareAchievementsSystem.AchievementsReceived(client, (ShareProgressAchievementsMsgData)data);
                    break;
                case ShareProgressMessageType.StrategyUpdate:
                    if (PersistentSyncRegistry.IsPersistentSyncInitialized)
                    {
                        LunaLog.Warning($"[PersistentSync] ShareProgress strategy fallback received from {client.PlayerName}; routing through canonical strategy snapshot store instead of one-off relay truth.");
                    }

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
                    if (PersistentSyncRegistry.IsPersistentSyncInitialized)
                    {
                        LunaLog.Warning($"[PersistentSync] ShareProgress part purchase fallback received from {client.PlayerName}; routing through canonical part-purchase snapshot store instead of event relay truth.");
                    }

                    SharePartPurchaseSystem.PurchaseReceived(client, (ShareProgressPartPurchaseMsgData)data);
                    break;
                case ShareProgressMessageType.ExperimentalPart:
                    if (PersistentSyncRegistry.IsPersistentSyncInitialized)
                    {
                        LunaLog.Warning($"[PersistentSync] ShareProgress experimental-part fallback received from {client.PlayerName}; routing through canonical experimental-parts snapshot store instead of event relay truth.");
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
