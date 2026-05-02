using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Network;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Interface;

namespace LmpClient.Systems.ShareUpgradeableFacilities
{
    public class ShareUpgradeableFacilitiesMessageSender : SubSystem<ShareUpgradeableFacilitiesSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<ShareProgressCliMsg>(msg)));
        }

        /// <summary>Obsolete: facility upgrades publish from <see cref="LmpClient.Systems.PersistentSync.UpgradeableFacilitiesPersistentSyncClientDomain"/>.</summary>
        public void SendFacilityUpgradeMessage(string facilityId, int level, float normLevel)
        {
        }
    }
}
