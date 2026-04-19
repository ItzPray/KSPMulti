using Contracts;
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

namespace LmpClient.Systems.ShareContracts
{
    public class ShareContractsMessageSender : SubSystem<ShareContractsSystem>, IMessageSender
    {
        private const string LmpOfferTitleFieldName = "lmpOfferTitle";

        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<ShareProgressCliMsg>(msg)));
        }

        public void SendContractMessage(Contract[] contracts)
        {
            var contractSnapshots = CreateCanonicalContractSnapshots(contracts);
            if (contractSnapshots.Count == 0)
            {
                return;
            }

            if (PersistentSyncSystem.Singleton != null && PersistentSyncSystem.Singleton.Enabled)
            {
                var reason = $"ContractProducer:{string.Join(",", contractSnapshots.Select(c => c.ContractGuid.ToString("N")))}";
                PersistentSyncSystem.Singleton.MessageSender.SendContractsIntent(contractSnapshots.ToArray(), reason);
                return;
            }

            LunaLog.LogWarning("[PersistentSync] ShareContractsMessageSender using legacy ShareProgress fallback because PersistentSync is not enabled yet.");

            // Build the legacy packet only as a transport fallback before PersistentSync is ready.
            var contractInfos = contractSnapshots.Select(contract => new ContractInfo
            {
                ContractGuid = contract.ContractGuid,
                Data = contract.Data,
                NumBytes = contract.NumBytes
            }).ToArray();
            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<ShareProgressContractsMsgData>();
            msgData.Contracts = contractInfos;
            msgData.ContractCount = contractInfos.Length;
            System.MessageSender.SendMessage(msgData);
        }

        public void SendContractMessage(Contract contract)
        {
            SendContractMessage(new[] { contract });
        }

        private static ConfigNode ConvertContractToConfigNode(Contract contract)
        {
            var configNode = new ConfigNode();
            try
            {
                contract.Save(configNode);
                WriteSyntheticOfferMetadata(configNode, contract);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: Error while saving contract: {e}");
                return null;
            }

            return configNode;
        }

        private static void WriteSyntheticOfferMetadata(ConfigNode configNode, Contract contract)
        {
            if (configNode == null || contract == null)
            {
                return;
            }

            var title = ShareContractsSystem.NormalizeOfferTitleForDedupe(contract.Title);
            if (string.IsNullOrEmpty(title))
            {
                return;
            }

            configNode.RemoveValues(LmpOfferTitleFieldName);
            configNode.AddValue(LmpOfferTitleFieldName, title);
        }

        private static List<ContractSnapshotInfo> CreateCanonicalContractSnapshots(IEnumerable<Contract> contracts)
        {
            var snapshots = new List<ContractSnapshotInfo>();
            foreach (var contract in contracts ?? Enumerable.Empty<Contract>())
            {
                if (contract == null)
                {
                    continue;
                }

                var configNode = ConvertContractToConfigNode(contract);
                if (configNode == null)
                {
                    continue;
                }

                var data = configNode.Serialize();
                snapshots.Add(new ContractSnapshotInfo
                {
                    ContractGuid = contract.ContractGuid,
                    ContractState = contract.ContractState.ToString(),
                    Placement = DeterminePlacement(contract),
                    Order = -1,
                    Data = data,
                    NumBytes = data.Length
                });
            }

            return snapshots;
        }

        private static ContractSnapshotPlacement DeterminePlacement(Contract contract)
        {
            switch (contract?.ContractState)
            {
                case Contract.State.Active:
                    return ContractSnapshotPlacement.Active;
                case Contract.State.Completed:
                case Contract.State.DeadlineExpired:
                case Contract.State.Failed:
                case Contract.State.Cancelled:
                    return ContractSnapshotPlacement.Finished;
                default:
                    return ContractSnapshotPlacement.Current;
            }
        }
    }
}
