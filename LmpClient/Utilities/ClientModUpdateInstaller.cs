using LmpClient.Localization;
using LmpCommon;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Networking;

namespace LmpClient.Utilities
{
    /// <summary>Downloads the GitHub client release zip and overwrites <c>GameData/KSPMultiplayer</c> (does not write <c>000_Harmony</c> from the package).</summary>
    public static class ClientModUpdateInstaller
    {
        private const byte PeMagicM = 0x4D;
        private const byte PeMagicZ = 0x5A;
        private const int Win32ErrorSharingViolation = 32;
        private const int Win32ErrorUserMappedFile = 1224;

        private static readonly string[] InPlaceWriteProbeRelPaths =
        {
            Path.Combine("Plugins", "LmpClient.dll"),
            Path.Combine("Plugins", "CachedQuickLz.dll"),
        };

        private static string KspmpFolderEntryPrefix => "KSPMPClient/GameData/" + ModLayoutConstants.GameDataModFolder + "/";

        private static void StatusWorking(Action<string> onStatus, string detail)
        {
            var t = LocalizationContainer.UpdateWindowText;
            onStatus?.Invoke(string.Format(t.InstallWorking, detail));
        }

        /// <summary>True if mod DLLs look loaded/locked, so in-place copy would fail; use “save zip to KSP folder” instead.</summary>
        private static bool InPlaceInstallLikelyToHitUserMappedFile(string modRoot)
        {
            if (string.IsNullOrEmpty(modRoot) || !Directory.Exists(modRoot)) return false;
            foreach (var rel in InPlaceWriteProbeRelPaths)
            {
                var p = Path.Combine(modRoot, rel);
                if (!File.Exists(p)) continue;
                if (!PathAllowsExclusiveOpenForWrite(p)) return true;
            }
            return false;
        }

        private static bool PathAllowsExclusiveOpenForWrite(string fullPath)
        {
            try
            {
                using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Write, FileShare.None)) { }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsWin32InUseError(Exception e)
        {
            for (var ex = e; ex != null; ex = ex.InnerException)
            {
                if (ex is IOException ioe) { if (HresultSuggestsFileInUseOrMapped(ioe.HResult)) return true; }
                if (ex is UnauthorizedAccessException) return true;
                var m = ex.Message;
                if (!string.IsNullOrEmpty(m))
                {
                    if (m.IndexOf(Win32ErrorUserMappedFile.ToString(), StringComparison.Ordinal) >= 0) return true;
                    if (m.IndexOf(Win32ErrorSharingViolation.ToString(), StringComparison.Ordinal) >= 0) return true;
                }
            }
            return false;
        }

        private static bool HresultSuggestsFileInUseOrMapped(int hResult)
        {
            var code = (uint)hResult & 0xFFFFU;
            if (code == (uint)Win32ErrorUserMappedFile || code == (uint)Win32ErrorSharingViolation) return true;
            return hResult == unchecked((int)0x80070020) || hResult == unchecked((int)0x80070021);
        }

        private static string RelativeToModRoot(string modRoot, string fullPath)
        {
            if (string.IsNullOrEmpty(modRoot) || string.IsNullOrEmpty(fullPath) ||
                !fullPath.StartsWith(modRoot, StringComparison.OrdinalIgnoreCase)) return fullPath;
            return fullPath.Substring(modRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsDllName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        }

        private static void VerifyOnDiskMatchesZipAndModules(string modRoot, IReadOnlyDictionary<string, long> expectedByPath)
        {
            var loc = LocalizationContainer.UpdateWindowText;
            foreach (var kv in expectedByPath)
            {
                var fullPath = kv.Key;
                var expected = kv.Value;
                var rel = RelativeToModRoot(modRoot, fullPath);
                if (!File.Exists(fullPath))
                    throw new IOException(string.Format(loc.InstallValidationMissingFile, rel));
                long actual;
                using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try { actual = fs.Length; }
                    catch (Exception e) { throw new IOException("Could not read: " + rel, e); }
                    if (actual != expected)
                        throw new IOException(string.Format(loc.InstallValidationSizeMismatch, rel, expected, actual));
                    if (IsDllName(fullPath) && expected > 0L)
                    {
                        if (actual < 2L) throw new IOException(string.Format(loc.InstallValidationInvalidModule, rel));
                        int b0 = fs.ReadByte(), b1 = fs.ReadByte();
                        if (b0 != PeMagicM || b1 != PeMagicZ) throw new IOException(string.Format(loc.InstallValidationInvalidModule, rel));
                    }
                }
            }
        }

        public static IEnumerator CoDownloadAndInstallKspmpFolder(
            string zipUrl,
            Action<string> onStatus,
            Action<string> onSuccessWithUserMessage,
            Action<Exception> onError)
        {
            if (string.IsNullOrEmpty(MainSystem.KspPath))
            {
                onError?.Invoke(new InvalidOperationException("KSP path is not set; try again from the main menu."));
                yield break;
            }

            var ksp = MainSystem.KspPath;
            var modRoot = CommonUtil.CombinePaths(ksp, "GameData", ModLayoutConstants.GameDataModFolder);
            var loc = LocalizationContainer.UpdateWindowText;

            if (InPlaceInstallLikelyToHitUserMappedFile(modRoot))
            {
                var savePath = Path.Combine(ksp, "KSPMP-Client-Update.zip");
                try
                {
                    if (File.Exists(savePath)) File.Delete(savePath);
                }
                catch
                { /* if locked, web request may still overwrite */
                }

                StatusWorking(onStatus, loc.InstallFallingBackSaveZip);
                long rCode0;
                string wErr0;
                using (var w = new UnityWebRequest(zipUrl, UnityWebRequest.kHttpVerbGET, new DownloadHandlerFile(savePath), null))
                {
                    w.SetRequestHeader("User-Agent", UpdateHandler.GitHubUserAgent);
                    w.SetRequestHeader("Accept", "application/octet-stream");
                    yield return w.SendWebRequest();
                    wErr0 = w.error;
                    try { rCode0 = w.responseCode; } catch { rCode0 = 0; }
                }
                if (!string.IsNullOrEmpty(wErr0) || !File.Exists(savePath) || new FileInfo(savePath).Length < 8L)
                {
                    try { if (File.Exists(savePath)) File.Delete(savePath); } catch { }
                    onError?.Invoke(new Exception("Download failed: " + (wErr0 ?? "unknown") + " (code " + rCode0 + ")"));
                    yield break;
                }
                onSuccessWithUserMessage?.Invoke(string.Format(loc.InstallSuccessZipSaved, savePath));
                yield break;
            }

            StatusWorking(onStatus, loc.InstallPhaseDownloading);
            var tmp = Path.Combine(Path.GetTempPath(), "Kspmp-Client-Update-" + DateTime.Now.Ticks + ".zip");
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch
            { /* will fail download if we cannot clobber; rare */
            }

            long responseCode;
            string wwwError;
            using (var w2 = new UnityWebRequest(zipUrl, UnityWebRequest.kHttpVerbGET, new DownloadHandlerFile(tmp), null))
            {
                w2.SetRequestHeader("User-Agent", UpdateHandler.GitHubUserAgent);
                w2.SetRequestHeader("Accept", "application/octet-stream");
                yield return w2.SendWebRequest();
                wwwError = w2.error;
                try { responseCode = w2.responseCode; } catch { responseCode = 0; }
            }

            if (!string.IsNullOrEmpty(wwwError) || !File.Exists(tmp) || new FileInfo(tmp).Length < 8L)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                onError?.Invoke(new Exception("Download failed: " + (wwwError ?? "unknown") + " (code " + responseCode + ")"));
                yield break;
            }

            try
            {
                StatusWorking(onStatus, string.Format(loc.InstallPhaseExtracting, ModLayoutConstants.GameDataModFolder));
                var fileSizes = ExtractKspmpFromClientReleaseZipFile(tmp, modRoot);
                var fileCount = fileSizes.Count;
                StatusWorking(onStatus, string.Format(loc.InstallFilesWritten, fileCount));
                StatusWorking(onStatus, string.Format(loc.InstallPhaseVerifying, fileCount));
                VerifyOnDiskMatchesZipAndModules(modRoot, fileSizes);
            }
            catch (Exception e)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                onError?.Invoke(IsWin32InUseError(e) ? new Exception(loc.InstallErrorFilesInUseKsp, e) : e);
                yield break;
            }

            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            onSuccessWithUserMessage?.Invoke(loc.InstallSuccessRestartKsp);
        }

        private static Dictionary<string, long> ExtractKspmpFromClientReleaseZipFile(string tempZip, string modRoot)
        {
            if (!File.Exists(tempZip)) throw new FileNotFoundException("Update zip is missing: " + tempZip);
            if (!Directory.Exists(modRoot)) Directory.CreateDirectory(modRoot);

            var written = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var prefix = KspmpFolderEntryPrefix;
            using (var z = ZipFile.OpenRead(tempZip))
            {
                foreach (var e in z.Entries)
                {
                    if (e == null) continue;
                    var name = (e.FullName ?? "").Replace("\\", "/");
                    if (name.IndexOf("..", StringComparison.Ordinal) >= 0) continue;
                    if (name.Length < prefix.Length) continue;
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (e.Length < 0) continue;
                    if (name.EndsWith("/", StringComparison.Ordinal))
                    {
                        var d = Path.Combine(modRoot, name.Substring(prefix.Length).TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
                        if (d.Length > 0) Directory.CreateDirectory(d);
                        continue;
                    }
                    if (e.Length == 0 && (name.EndsWith("/") || string.IsNullOrEmpty(e.Name)))
                    {
                        continue;
                    }

                    var rel = name.Substring(prefix.Length);
                    if (string.IsNullOrEmpty(rel)) continue;
                    var target = Path.Combine(modRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    var dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    var expected = e.Length;
                    using (var s = e.Open()) using (var fs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None)) s.CopyTo(fs);
                    written[target] = expected;
                }
            }

            if (written.Count < 1)
            {
                throw new Exception("The zip has no " + prefix + " entries. Use a client zip built the same way as AppVeyor/Scripts (KSPMPClient/GameData/KSPMultiplayer/…).");
            }

            return written;
        }
    }
}
