namespace LmpGlobal
{
    /// <summary>
    /// Default public URLs and CI endpoints. The upstream project historically used the LunaMultiplayer
    /// GitHub org; replace these with your fork, AppVeyor project slug, and own update feeds when you ship KSPMP builds.
    /// </summary>
    public static class RepoConstants
    {
        public static bool GithubReleaseUpdateChecksEnabled => false;
        public static string OfficialWebsite => "https://lunamultiplayer.com";
        public static string RepoUrl => "https://github.com/LunaMultiplayer/LunaMultiplayer/";
        public static string LatestGithubReleaseUrl => "https://github.com/LunaMultiplayer/LunaMultiplayer/releases/latest";
        public static string MasterServersListShortUrl => "https://goo.gl/jgUgU9";
        public static string MasterServersListUrl => "https://raw.githubusercontent.com/LunaMultiplayer/LunaMultiplayer/master/MasterServersList/MasterServersList.txt";
        public static string DedicatedServersListUrl => "https://raw.githubusercontent.com/LunaMultiplayer/LunaMultiplayer/master/MasterServersList/DedicatedServersList.txt";
        public static string BannedIpListUrl => "https://raw.githubusercontent.com/LunaMultiplayer/LunaMultiplayer/master/MasterServersList/BannedIpList.txt";
        public static string ApiLatestGithubReleaseUrl => "https://api.github.com/repos/LunaMultiplayer/LunaMultiplayer/releases/latest";
        public static string AppveyorUrl => "https://ci.appveyor.com/api/projects/gavazquez/lunamultiplayer";
    }
}
