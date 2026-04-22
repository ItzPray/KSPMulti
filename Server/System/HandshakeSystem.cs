using LmpCommon;
using LmpCommon.Enums;
using LmpCommon.Message.Data.Handshake;
using LmpCommon.Message.Data.PlayerConnection;
using LmpCommon.Message.Server;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Plugin;
using Server.Server;
using Server.System.LaunchSite;

namespace Server.System
{
    public partial class HandshakeSystem
    {
        internal struct HandshakeAdmissionResult
        {
            public bool Allowed;
            public string Reason;
            public HandshakeReply Reply;
        }

        private string Reason { get; set; }

        internal HandshakeAdmissionResult EvaluateHandshakeRequest(HandshakeRequestMsgData data)
        {
            if (!SessionAdmission.TryValidateClientHandshake(data.ProtocolForkId, data.ExactClientBuild, out var admissionReason, out var admissionReply))
            {
                return new HandshakeAdmissionResult
                {
                    Allowed = false,
                    Reason = admissionReason,
                    Reply = admissionReply
                };
            }

            return new HandshakeAdmissionResult
            {
                Allowed = true,
                Reason = string.Empty,
                Reply = HandshakeReply.HandshookSuccessfully
            };
        }

        public void HandleHandshakeRequest(ClientStructure client, HandshakeRequestMsgData data)
        {
            var admission = EvaluateHandshakeRequest(data);
            if (!admission.Allowed)
            {
                LunaLog.Normal($"Client {data.PlayerName} rejected before authentication: {admission.Reason}");
                HandshakeSystemSender.SendHandshakeReply(client, admission.Reply, admission.Reason);
                client.DisconnectClient = true;
                ClientConnectionHandler.DisconnectClient(client, admission.Reason);
                return;
            }

            var valid = CheckServerFull(client);
            valid &= valid && CheckUsernameLength(client, data.PlayerName);
            valid &= valid && CheckUsernameCharacters(client, data.PlayerName);
            valid &= valid && CheckPlayerIsAlreadyConnected(client, data.PlayerName);
            valid &= valid && CheckUsernameIsReserved(client, data.PlayerName);
            valid &= valid && CheckPlayerIsBanned(client, data.UniqueIdentifier);

            if (!valid)
            {
                LunaLog.Normal($"Client {data.PlayerName} ({data.UniqueIdentifier}) failed to handshake: {Reason}. Disconnecting");
                client.DisconnectClient = true;
                ClientConnectionHandler.DisconnectClient(client, Reason);
            }
            else
            {
                client.PlayerName = data.PlayerName;
                client.UniqueIdentifier = data.UniqueIdentifier;
                client.Authenticated = true;

                LmpPluginHandler.FireOnClientAuthenticated(client);

                LunaLog.Normal($"Client {data.PlayerName} ({data.UniqueIdentifier}) handshake successfully, fork: {SessionAdmission.LocalProtocolForkId}, build: {SessionAdmission.LocalExactBuild}");

                HandshakeSystemSender.SendHandshakeReply(client, HandshakeReply.HandshookSuccessfully, "success");

                var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<PlayerConnectionJoinMsgData>();
                msgData.PlayerName = client.PlayerName;
                MessageQueuer.RelayMessage<PlayerConnectionSrvMsg>(client, msgData);

                LunaLog.Debug($"Online Players: {ServerContext.PlayerCount}, connected: {ClientRetriever.GetClients().Length}");

                LaunchSiteOccupancyService.SendSnapshotToClient(client);
            }
        }
    }
}
