using LmpCommon.Message.Interface;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Settings.Structures;
using System.Linq;

namespace Server.Server
{
    public class MessageQueuer
    {
        /// <summary>
        /// Sends a message to all the clients except the one given as parameter that are in the same subspace
        /// </summary>
        public static void RelayMessageToSubspace<T>(ClientStructure exceptClient, IMessageData data) where T : class, IServerMessageBase
        {
            if (data == null) return;

            RelayMessageToSubspace<T>(exceptClient, data, exceptClient.Subspace);
        }

        /// <summary>
        /// Sends a message to all the clients in the given subspace
        /// </summary>
        public static void SendMessageToSubspace<T>(IMessageData data, int subspace) where T : class, IServerMessageBase
        {
            if (data == null) return;

            foreach (var otherClient in ServerContext.Clients.Values.Where(c => c.Subspace == subspace))
                SendToClient(otherClient, GenerateMessage<T>(data));
        }

        /// <summary>
        /// Sends a message to all the clients except the one given as parameter that are in the subspace given as parameter
        /// </summary>
        public static void RelayMessageToSubspace<T>(ClientStructure exceptClient, IMessageData data, int subspace) where T : class, IServerMessageBase
        {
            if (data == null) return;

            foreach (var otherClient in ServerContext.Clients.Values.Where(c => !Equals(c, exceptClient) && c.Subspace == subspace))
                SendToClient(otherClient, GenerateMessage<T>(data));
        }

        /// <summary>
        /// Sends a message to all the clients except the one given as parameter
        /// </summary>
        public static void RelayMessage<T>(ClientStructure exceptClient, IMessageData data) where T : class, IServerMessageBase
        {
            if (data == null) return;

            foreach (var otherClient in ServerContext.Clients.Values.Where(c => !Equals(c, exceptClient)))
                SendToClient(otherClient, GenerateMessage<T>(data));
        }

        /// <summary>
        /// Sends a message to all the clients
        /// </summary>
        public static void SendToAllClients<T>(IMessageData data) where T : class, IServerMessageBase
        {
            if (data == null) return;

            foreach (var otherClient in ServerContext.Clients.Values)
                SendToClient(otherClient, GenerateMessage<T>(data));
        }

        /// <summary>
        /// Sends a message to the given client
        /// </summary>
        public static void SendToClient<T>(ClientStructure client, IMessageData data) where T : class, IServerMessageBase
        {
            if (data == null) return;

            SendToClient(client, GenerateMessage<T>(data));
        }

        /// <summary>
        /// Disconnects the given client
        /// </summary>
        public static void SendConnectionEnd(ClientStructure client, string reason)
        {
            ClientConnectionHandler.DisconnectClient(client, reason);
        }

        /// <summary>
        /// Disconnect all clients
        /// </summary>
        public static void SendConnectionEndToAll(string reason)
        {
            foreach (var client in ClientRetriever.GetAuthenticatedClients())
                SendConnectionEnd(client, reason);
        }

        #region Private

        private static void SendToClient(ClientStructure client, IServerMessageBase msg)
        {
            if (msg?.Data == null) return;
            if (client == null) return;

            var max = GeneralSettings.SettingsStore.MaxOutboundSendQueuePerClient;
            if (max > 0)
            {
                // ConcurrentQueue is unbounded; without a cap a slow client or send thread can grow RAM without limit.
                const int maxDrainIterations = 262144;
                var drops = 0;
                while (client.SendMessageQueue.Count >= max && drops < maxDrainIterations && client.SendMessageQueue.TryDequeue(out _))
                {
                    drops++;
                }

                if (drops > 0)
                {
                    LunaLog.Warning($"[MessageQueuer] send queue cap={max} client={client.PlayerName}: dropped {drops} oldest message(s) to bound memory");
                }

                if (client.SendMessageQueue.Count >= max)
                {
                    LunaLog.Error($"[MessageQueuer] send queue still at or above cap={max} after drain budget for client={client.PlayerName}; dropping newest message to avoid unbounded growth");
                    return;
                }
            }

            client.SendMessageQueue.Enqueue(msg);
        }

        private static T GenerateMessage<T>(IMessageData data) where T : class, IServerMessageBase
        {
            var newMessage = ServerContext.ServerMessageFactory.CreateNew<T>(data);
            return newMessage;
        }

        #endregion
    }
}