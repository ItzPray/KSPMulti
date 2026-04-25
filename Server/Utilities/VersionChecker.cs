using LmpCommon;
using LmpGlobal;
using LmpUpdater.Github;
using Server.Context;
using Server.Log;
using System;
using System.Threading.Tasks;

namespace Server.Utilities
{
    public class VersionChecker
    {
        public static Version LatestReleaseVersion { get; private set; } = new Version(0, 0, 0);

        public static async void RefreshLatestVersion()
        {
            if (!RepoConstants.GithubReleaseUpdateChecksEnabled)
                return;

            while (ServerContext.ServerRunning)
            {
                try
                {
                    LatestReleaseVersion = GithubUpdateChecker.GetLatestVersion();
                }
                catch
                { }

                //Sleep for 30 minutes...
                await Task.Delay(30 * 60 * 1000);
            }
        }
    }
}
