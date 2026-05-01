using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Network;
using LmpClient.Systems.PersistentSync;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Interface;
using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;

namespace LmpClient.Systems.ShareUpgradeableFacilities
{
    public class ShareUpgradeableFacilitiesMessageSender : SubSystem<ShareUpgradeableFacilitiesSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<ShareProgressCliMsg>(msg)));
        }

        public void SendFacilityUpgradeMessage(string facilityId, int level, float normLevel)
        {
            if (!PersistentSyncSystem.IsLiveFor<UpgradeableFacilitiesPersistentSyncClientDomain>())
            {
                return;
            }

            var reason = $"Facility upgrade {facilityId} -> level {level}";
            PersistentSyncSystem.SendIntent<UpgradeableFacilitiesPersistentSyncClientDomain, UpgradeableFacilitiesPayload>(
                new UpgradeableFacilitiesPayload
                {
                    Items = new[]
                    {
                        new UpgradeableFacilityLevelPayload
                        {
                            FacilityId = facilityId,
                            Level = level
                        }
                    }
                },
                reason);
        }
    }
}
