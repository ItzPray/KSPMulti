using LmpClient.Windows.Update;
using LmpCommon;
using LmpGlobal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;

namespace LmpClient.Utilities
{
    public static class UpdateHandler
    {
        private const string GitHubUserAgent = "KSPMP-Client (Kerbal Space Program; +https://github.com/ItzPray/KSPMulti)";

        public static IEnumerator CheckForUpdates()
        {
            if (!RepoConstants.GithubReleaseUpdateChecksEnabled)
                yield break;

            using (var www = UnityWebRequest.Get(RepoConstants.ApiLatestGithubReleaseUrl))
            {
                www.SetRequestHeader("User-Agent", GitHubUserAgent);
                www.SetRequestHeader("Accept", "application/vnd.github+json");
                yield return www.SendWebRequest();

                if (www.isNetworkError || www.isHttpError)
                {
                    LunaLog.Log($"Could not check for latest version. Error: {www.error} (code {www.responseCode})");
                    yield break;
                }

                var responseCode = www.responseCode;
                if (responseCode < 200 || responseCode > 299)
                {
                    LunaLog.Log($"Could not check for latest version. HTTP {responseCode}");
                    yield break;
                }

                var text = www.downloadHandler.text;
                if (!(Json.Deserialize(text) is Dictionary<string, object> data))
                    yield break;

                if (!data.ContainsKey("tag_name") || data["tag_name"] == null)
                    yield break;

                var tagStr = data["tag_name"].ToString();
                if (!TryParseVersionFromTag(tagStr, out var latestVersion))
                {
                    LunaLog.Log($"Could not parse version from release tag: {tagStr}");
                    yield break;
                }

                LunaLog.Log($"Latest release (tag {tagStr}): {latestVersion}");
                if (latestVersion > LmpVersioning.CurrentVersion)
                {
                    var body = data.ContainsKey("body") && data["body"] != null ? data["body"].ToString() : string.Empty;
                    UpdateWindow.LatestVersion = latestVersion;
                    UpdateWindow.Changelog = body;
                    UpdateWindow.Singleton.Display = true;
                }
            }
        }

        /// <summary>Maps <c>tag_name</c> to a <see cref="Version"/> from a numeric prefix, e.g. <c>v0.31.0</c> or <c>0.31.0-Draft</c> → 0.31.0.</summary>
        private static bool TryParseVersionFromTag(string tagName, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(tagName)) return false;

            var t = tagName.Trim();
            if (t.Length > 0 && (t[0] == 'v' || t[0] == 'V')) t = t.Substring(1);

            var b = new StringBuilder();
            foreach (var c in t)
            {
                if (char.IsDigit(c) || c == '.') b.Append(c);
                else break;
            }

            var s = b.ToString().TrimEnd('.');
            if (s.Length == 0) return false;
            try
            {
                version = new Version(s);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
