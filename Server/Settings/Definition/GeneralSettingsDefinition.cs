using LmpCommon.Enums;
using LmpCommon.Xml;
using System;

namespace Server.Settings.Definition
{
    [Serializable]
    public class GeneralSettingsDefinition
    {
        [XmlComment(Value = "Name of the server. Max 30 char")]
        public string ServerName { get; set; } = "Luna Server";

        [XmlComment(Value = "Description of the server. Max 200 char")]
        public string Description { get; set; } = "Luna Server Description";

        [XmlComment(Value = "By default this will be given by the masterserver but you can override it here if you want. Max 2 char")]
        public string CountryCode { get; set; } = "";

        [XmlComment(Value = "Website text to display (discord, website, forum, etc). Can be left empty. Max 15 char")]
        public string WebsiteText { get; set; } = "LMP";

        [XmlComment(Value = "Actual website URL. Can be left empty. Max 60 char")]
        public string Website { get; set; } = "lunamultiplayer.com";

        [XmlComment(Value = "Password for the server. Leave it empty if you want to make a public server. Max 30 chars")]
        public string Password { get; set; } = "";

        [XmlComment(Value = "Admin password for the server. Leave it empty if you don't want to allow server administration from KSP. Max 30 chars")]
        public string AdminPassword { get; set; } = "";

        [XmlComment(Value = "Specify the server's MOTD (message of the day). 255 chars max")]
        public string ServerMotd { get; set; } = "Hi %Name%!\nWelcome to %ServerName%.\nOnline players: %PlayerCount%";

        [XmlComment(Value = "Writes the server's MOTD (message of the day) in the chat of the user who joins")]
        public bool PrintMotdInChat { get; set; } = false;

        [XmlComment(Value = "Maximum amount of players that can join the server.")]
        public int MaxPlayers { get; set; } = 20;

        [XmlComment(Value = "Maximum length of a username.")]
        public int MaxUsernameLength { get; set; } = 15;

        [XmlComment(Value = "Specify in minutes how often /dekessler automatically runs. 0 = Disabled")]
        public float AutoDekessler { get; set; } = 0.5f;

        [XmlComment(Value = "Specify in minutes how often /nukeksc automatically runs. 0 = Disabled")]
        public float AutoNuke { get; set; } = 0.0f;

        [XmlComment(Value = "Enable use of Cheats in-game.")]
        public bool Cheats { get; set; } = true;

        [XmlComment(Value = "Allow players to sack kerbals")]
        public bool AllowSackKerbals { get; set; } = false;

        [XmlComment(Value = "Specify the Name that will appear when you send a message using the server's console.")]
        public string ConsoleIdentifier { get; set; } = "Server";

        [XmlComment(Value = "Specify the gameplay difficulty of the server. Values: Easy, Normal, Moderate, Hard, Custom")]
        public GameDifficulty GameDifficulty { get; set; } = GameDifficulty.Normal;

        [XmlComment(Value = "Specify the game Type. Values: Sandbox, Career, Science")]
        public GameMode GameMode { get; set; } = GameMode.Sandbox;

        [XmlComment(Value = "Enable mod control. WARNING: Only consider turning off mod control for private servers. " +
                            "The game will constantly complain about missing parts if there are missing mods. " +
                            "Read this wiki page: https://github.com/LunaMultiplayer/LunaMultiplayer/wiki/Mod-file to understand how it works")]
        public bool ModControl { get; set; } = true;

        [XmlComment(Value = "How many untracked asteroids to spawn into the universe. 0 = Disabled")]
        public int NumberOfAsteroids { get; set; } = 5;

        [XmlComment(Value = "How many untracked comets to spawn into the universe. 0 = Disabled")]
        public int NumberOfComets { get; set; } = 5;

        [XmlComment(Value = "Terrain quality. All clients will need to have this setting in their KSP to avoid terrain differences. Values: Low, Default, High, Ignore. Using 'Ignore' might create bugs")]
        public TerrainQuality TerrainQuality { get; set; } = TerrainQuality.High;

        [XmlComment(Value = "Radius (meters) around each registered launch/runway spawn point where OTHER players' vessels " +
                            "skip Vessel.Load (invisible on pad). 0 = disabled so everyone sees each other on the pad, " +
                            "including multi-launch-pad mods that register sites in PSystemSetup. Older LMP default was 100m; " +
                            "raise only if you need pad isolation.")]
        public float SafetyBubbleDistance { get; set; } = 0.0f;

        [XmlComment(Value = "Max number of parts that a vessel can have when spawning")]
        public int MaxVesselParts { get; set; } = 200;

        [XmlComment(Value = "Max outbound messages queued per client before oldest are dropped. " +
                            "Protects server RAM if sends fall behind (e.g. snapshot storms). 0 = unlimited (legacy). Default 65536.")]
        public int MaxOutboundSendQueuePerClient { get; set; } = 65536;

        [XmlComment(Value = "Launch pad coordination for multi-pad setups (e.g. KSC Enhanced). Off = legacy LMP behaviour.")]
        public LaunchPadCoordinationMode LaunchPadCoordinationMode { get; set; } = LaunchPadCoordinationMode.Off;

        [XmlComment(Value = "When LaunchPadCoordinationMode=LockAndOverflowBubble and all concurrent slots are in use, " +
                            "clients use at least this bubble radius (meters) so overlapping pad spawns are less lethal.")]
        public float LaunchPadOverflowBubbleDistance { get; set; } = 200f;

        [XmlComment(Value = "How many distinct PRELAUNCH pad slots are expected before overflow-bubble activates (e.g. 2 for dual pads).")]
        public int LaunchPadConcurrentSlots { get; set; } = 2;

        [XmlComment(Value = "Seconds without vessel proto/update before a PRELAUNCH vessel stops counting toward pad occupancy (lease expiry). 0 = disabled.")]
        public int LaunchPadLeaseTimeoutSeconds { get; set; } = 120;

        [XmlComment(Value = "How long a client→server pad reservation lasts without a matching PRELAUNCH proto (Plan B).")]
        public int LaunchPadReservationDurationSeconds { get; set; } = 180;

        [XmlComment(Value = "When true and optional DLL fields are set, clients must have that DLL with matching SHA256 (from mod list) while coordination is enabled.")]
        public bool LaunchPadKsceEnforceOptionalDllMatch { get; set; } = false;

        [XmlComment(Value = "GameData-relative path to optional KSCE/KK plugin DLL for SHA/version check, e.g. 'kerbalkonstructs/plugins/kerbalkonstructs.dll'. Lowercase.")]
        public string LaunchPadKsceOptionalDllRelativePath { get; set; } = "";

        [XmlComment(Value = "Expected SHA256 of that DLL when enforcement is on (hex, same format as LMP mod control). Empty = skip SHA check.")]
        public string LaunchPadKsceOptionalDllSha256 { get; set; } = "";

        [XmlComment(Value = "Minimum FileVersion of the optional DLL (from Windows file version). Empty = any.")]
        public string LaunchPadKsceMinPluginFileVersion { get; set; } = "";

        [XmlComment(Value = "Maximum FileVersion of the optional DLL. Empty = any.")]
        public string LaunchPadKsceMaxPluginFileVersion { get; set; } = "";
    }
}
