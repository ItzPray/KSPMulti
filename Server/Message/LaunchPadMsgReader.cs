using LmpCommon.Enums;
using LmpCommon.Message.Data.LaunchPad;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Server;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Message.Base;
using Server.Server;
using Server.Settings.Structures;
using Server.System.LaunchSite;

namespace Server.Message
{
    public class LaunchPadMsgReader : ReaderBase
    {
        public override void HandleMessage(ClientStructure client, IClientMessageBase message)
        {
            if (!(message.Data is LaunchPadReserveSiteRequestMsgData req))
            {
                message.Recycle();
                return;
            }

            var siteKey = req.SiteKey ?? string.Empty;
            message.Recycle();

            var mode = GeneralSettings.SettingsStore.LaunchPadCoordinationMode;
            if (mode == LaunchPadCoordinationMode.Off)
            {
                SendReserveReply(client, false, siteKey, "Launch pad coordination is disabled.");
                return;
            }

            if (LaunchSiteOccupancyService.IsSiteOccupiedByAnotherPlayerVesselOrReservation(siteKey, client.PlayerName))
            {
                SendReserveReply(client, false, siteKey, "That launch site is already in use or reserved.");
                LaunchSiteOccupancyService.BroadcastSmart();
                return;
            }

            if (!LaunchPadReservationRegistry.TryReserve(client.PlayerName, siteKey, out var deny))
            {
                SendReserveReply(client, false, siteKey, deny ?? "Reservation denied.");
                LaunchSiteOccupancyService.BroadcastSmart();
                return;
            }

            SendReserveReply(client, true, siteKey, string.Empty);
            LaunchSiteOccupancyService.BroadcastSmart();
        }

        private static void SendReserveReply(ClientStructure client, bool granted, string siteKey, string reason)
        {
            var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<LaunchPadReserveSiteReplyMsgData>();
            msgData.Granted = granted;
            msgData.SiteKey = siteKey ?? string.Empty;
            msgData.Reason = reason ?? string.Empty;
            MessageQueuer.SendToClient<LaunchPadSrvMsg>(client, msgData);
        }
    }
}
