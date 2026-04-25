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
        public const string GitHubUserAgent = "KSPMP-Client (Kerbal Space Program; +https://github.com/ItzPray/KSPMulti)";

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
                    UpdateWindow.ClientZipDownloadUrl = null;
                    LunaLog.Log($"Could not check for latest version. Error: {www.error} (code {www.responseCode})");
                    yield break;
                }

                var responseCode = www.responseCode;
                if (responseCode < 200 || responseCode > 299)
                {
                    UpdateWindow.ClientZipDownloadUrl = null;
                    LunaLog.Log($"Could not check for latest version. HTTP {responseCode}");
                    yield break;
                }

                var text = www.downloadHandler.text;
                if (!(Json.Deserialize(text) is Dictionary<string, object> data))
                {
                    UpdateWindow.ClientZipDownloadUrl = null;
                    yield break;
                }

                if (!data.ContainsKey("tag_name") || data["tag_name"] == null)
                {
                    UpdateWindow.ClientZipDownloadUrl = null;
                    yield break;
                }

                var tagStr = data["tag_name"].ToString();
                if (!TryParseVersionFromTag(tagStr, out var latestVersion))
                {
                    UpdateWindow.ClientZipDownloadUrl = null;
                    LunaLog.Log($"Could not parse version from release tag: {tagStr}");
                    yield break;
                }

                // GitHub /releases/latest is the newest *published* non-prerelease release. Drafts do not count.
                LunaLog.Log($"Latest published release (tag {tagStr}): {latestVersion}; this client: {LmpVersioning.CurrentVersion}.");
                if (latestVersion > LmpVersioning.CurrentVersion)
                {
                    var body = data.ContainsKey("body") && data["body"] != null ? data["body"].ToString() : string.Empty;
                    UpdateWindow.LatestVersion = latestVersion;
                    UpdateWindow.Changelog = body;
                    UpdateWindow.ClientZipDownloadUrl = TryGetClientReleaseZipUrl(data);
                    UpdateWindow.ClearInstallState();
                    UpdateWindow.Singleton.Display = true;
                }
                else
                {
                    UpdateWindow.ClientZipDownloadUrl = null;
                    if (latestVersion < LmpVersioning.CurrentVersion)
                    {
                        LunaLog.Log(
                            "[KSPMP] No update window: the published 'latest' release is older than this build. " +
                            "If you are testing, publish a non-draft release with a HIGHER tag, or the draft you created is not visible to /releases/latest. " +
                            "Or keep this message if the client is intentionally newer (dev build).");
                    }
                    else
                    {
                        LunaLog.Log("[KSPMP] No update window: client is already at or matches the published latest release version.");
                    }
                }
            }
        }

        /// <summary>Asset <c>browser_download_url</c> for the client zip, or null to fall back to the browser only.</summary>
        private static string TryGetClientReleaseZipUrl(Dictionary<string, object> data)
        {
            var name = "KSPMultiplayer-Client-Release.zip";
#if DEBUG
            name = "KSPMultiplayer-Client-Debug.zip";
#endif
            if (!data.ContainsKey("assets") || data["assets"] == null) return null;
            if (!(data["assets"] is System.Collections.IList alist)) return null;
            foreach (var a in alist)
            {
                if (!(a is Dictionary<string, object> ad)) continue;
                if (!ad.TryGetValue("name", out var nameObj) || nameObj == null) continue;
                if (!string.Equals(name, nameObj.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
                if (ad.TryGetValue("browser_download_url", out var u) && u != null) return u.ToString();
            }

            // Fallback: first asset whose name matches CI convention (KSPMP zip layout).
            foreach (var a in alist)
            {
                if (!(a is Dictionary<string, object> ad)) continue;
                if (!ad.TryGetValue("name", out var n) || n == null) continue;
                var s = n.ToString();
                if (s.IndexOf("KSPMultiplayer-Client", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (ad.TryGetValue("browser_download_url", out var u) && u != null) return u.ToString();
            }

            return null;
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
