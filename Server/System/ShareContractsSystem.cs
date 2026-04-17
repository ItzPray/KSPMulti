using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Server;
using LmpCommon.PersistentSync;
using LunaConfigNode.CfgNode;
using Server.Client;
using Server.Log;
using Server.Server;
using Server.System.PersistentSync;
using Server.System.Scenario;
using System;
using System.Linq;
using System.Text;

namespace Server.System
{
    public static class ShareContractsSystem
    {
        public static void ContractsReceived(ClientStructure client, ShareProgressContractsMsgData data)
        {
            LunaLog.Debug("Contract data received:");

            foreach (var item in data.Contracts)
            {
                LunaLog.Debug(item.ContractGuid.ToString());
            }

            if (PersistentSyncRegistry.IsPersistentSyncInitialized)
            {
                var payloadContracts = data.Contracts
                    .Take(data.ContractCount)
                    .Select(BuildSnapshotInfo)
                    .Where(info => info != null)
                    .ToArray();

                var payload = ContractSnapshotPayloadSerializer.Serialize(payloadContracts);
                PersistentSyncRegistry.HandleLegacyContractFallbackIntent(client, payload, payload.Length, "LegacyShareContracts");
                return;
            }

            //send the contract update to all other clients
            MessageQueuer.RelayMessage<ShareProgressSrvMsg>(client, data);
            ScenarioDataUpdater.WriteContractDataToFile(data);
        }

        private static ContractSnapshotInfo BuildSnapshotInfo(ContractInfo contractInfo)
        {
            try
            {
                var contractNode = new ConfigNode(Encoding.UTF8.GetString(contractInfo.Data, 0, contractInfo.NumBytes));
                var contractState = contractNode.GetValue("state")?.Value ?? string.Empty;
                return new ContractSnapshotInfo
                {
                    ContractGuid = contractInfo.ContractGuid,
                    ContractState = contractState,
                    Placement = DeterminePlacement(contractState),
                    Order = -1,
                    NumBytes = contractInfo.NumBytes,
                    Data = contractInfo.Data.Take(contractInfo.NumBytes).ToArray()
                };
            }
            catch (Exception e)
            {
                LunaLog.Error($"[PersistentSync] failed to translate legacy contract payload guid={contractInfo.ContractGuid}: {e}");
                return null;
            }
        }

        private static ContractSnapshotPlacement DeterminePlacement(string contractState)
        {
            switch (contractState)
            {
                case "Active":
                    return ContractSnapshotPlacement.Active;
                case "Completed":
                case "DeadlineExpired":
                case "Failed":
                case "Cancelled":
                    return ContractSnapshotPlacement.Finished;
                default:
                    return ContractSnapshotPlacement.Current;
            }
        }
    }
}
