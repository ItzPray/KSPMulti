namespace LmpGlobal
{
    /// <summary>
    /// Public URLs, GitHub update feeds, and (optional) master-list raw files. Point these at this fork; AppVeyor
    /// project API slug is separate from the GitHub repo name and must be updated if you re-home CI.
    /// </summary>
    public static class RepoConstants
    {
        /// <summary>
        /// When true, the client and server can query the GitHub Releases API for a newer KSPMP build. Requires a
        /// <b>published</b> (non-draft) "latest" release; draft uploads are invisible to
        /// <c>/repos/.../releases/latest</c>.
        /// </summary>
        public static bool GithubReleaseUpdateChecksEnabled => true;
        public static string OfficialWebsite => "https://lunamultiplayer.com";
        public static string RepoUrl => "https://github.com/ItzPray/KSPMulti/";
        public static string LatestGithubReleaseUrl => "https://github.com/ItzPray/KSPMulti/releases/latest";
        public static string MasterServersListShortUrl => "https://goo.gl/jgUgU9";
        public static string MasterServersListUrl => "https://raw.githubusercontent.com/ItzPray/KSPMulti/master/MasterServersList/MasterServersList.txt";
        public static string DedicatedServersListUrl => "https://raw.githubusercontent.com/ItzPray/KSPMulti/master/MasterServersList/DedicatedServersList.txt";
        public static string BannedIpListUrl => "https://raw.githubusercontent.com/ItzPray/KSPMulti/master/MasterServersList/BannedIpList.txt";
        /// <summary>GitHub "releases/latest": the newest published (non-prerelease) release. Drafts are not included — a draft does not
        /// become "latest" until you publish the release, so the client may not show an update until then.</summary>
        public static string ApiLatestGithubReleaseUrl => "https://api.github.com/repos/ItzPray/KSPMulti/releases/latest";
        public static string AppveyorUrl => "https://ci.appveyor.com/api/projects/gavazquez/lunamultiplayer";
    }
}
