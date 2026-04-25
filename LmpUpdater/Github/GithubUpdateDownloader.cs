using LmpUpdater.Github.Contracts;
using System;
using System.Linq;

namespace LmpUpdater.Github
{
    public class GithubUpdateDownloader
    {
        /// <summary>
        /// Resolves a release asset on <see cref="GithubUpdateChecker.LatestRelease"/>. Names must match CI
        /// artifacts, e.g. <c>KSPMultiplayer-Client-Release.zip</c>.
        /// </summary>
        public static string GetZipFileUrl(GithubProduct product, bool debugVersion = false)
        {
            var name = GetExpectedAssetFileName(product, debugVersion);
            var list = GithubUpdateChecker.LatestRelease?.Assets;
            if (list == null || list.Count == 0) return null;
            return list
                .FirstOrDefault(a => a.Name != null && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?
                .BrowserDownloadUrl;
        }

        /// <summary>File name to match against <see cref="GitHubAsset.Name"/>, e.g. <c>KSPMultiplayer-Server-Debug.zip</c>.</summary>
        public static string GetExpectedAssetFileName(GithubProduct product, bool debugVersion)
        {
            var config = debugVersion ? "Debug" : "Release";
            switch (product)
            {
                case GithubProduct.Client: return $"KSPMultiplayer-Client-{config}.zip";
                case GithubProduct.Server: return $"KSPMultiplayer-Server-{config}.zip";
                default: throw new ArgumentOutOfRangeException(nameof(product), product, null);
            }
        }
    }
}
