using Lidgren.Network;
using LmpCommon;
using LmpCommon.Message;
using Server.Client;
using Server.Log;
using Server.Server;
using Server.Settings.Structures;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;

namespace Server.Context
{
    public static class ServerContext
    {
        public static int PlayerCount => ClientRetriever.GetActiveClientCount();
        public static readonly ConcurrentDictionary<IPEndPoint, ClientStructure> Clients = new ConcurrentDictionary<IPEndPoint, ClientStructure>();

        public static volatile bool ServerRunning;
        public static volatile bool ServerStarting;
        public static volatile int Day;

        public static string Players => ClientRetriever.GetActivePlayerNames();
        public static bool UsePassword => !string.IsNullOrEmpty(GeneralSettings.SettingsStore.Password);

        public static Stopwatch ServerClock = new Stopwatch();
        public static string UniverseDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Universe");
        public static string ConfigDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
        public static string ModFilePath = Path.Combine(ConfigDirectory, ModLayoutConstants.ModControlFileName);
        public static string LegacyModFilePath = Path.Combine(ConfigDirectory, ModLayoutConstants.LegacyModControlFileName);
        public static string OldModFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ModLayoutConstants.LegacyModControlFileName);

        // Configuration object
        public static NetPeerConfiguration Config { get; } = new NetPeerConfiguration("KSPMP")
        {
            SendBufferSize = 1500000, //500kb
            ReceiveBufferSize = 1500000, //500kb
            DefaultOutgoingMessageCapacity = 500000, //500kb
            SuppressUnreliableUnorderedAcks = true,
        };

        public static MasterServerMessageFactory MasterServerMessageFactory { get; } = new MasterServerMessageFactory();
        public static ServerMessageFactory ServerMessageFactory { get; } = new ServerMessageFactory();
        public static ClientMessageFactory ClientMessageFactory { get; } = new ClientMessageFactory();

        public static void Shutdown(string reason)
        {
            LunaLog.Normal($"Shutdown: {reason}");
            MessageQueuer.SendConnectionEndToAll(reason);
            Thread.Sleep(250);
            ServerStarting = false;
            ServerRunning = false;
        }
    }
}
