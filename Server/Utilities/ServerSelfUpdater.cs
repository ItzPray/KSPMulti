using LmpCommon;
using LmpGlobal;
using LmpUpdater.Github;
using LmpUpdater.Github.Contracts;
using Server.Log;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;

namespace Server.Utilities
{
    /// <summary>
    /// Checks GitHub for a newer server build, then merges a downloaded <c>KSPMPServer\</c> tree into this
    /// install (preserving <c>Universe</c>, <c>Config</c>, <c>logs</c>, user <c>Plugins</c>, etc.).
    /// On Windows, loaded assemblies cannot be overwritten in-process; a helper .cmd (robocopy) runs after this process exits.
    /// </summary>
    public static class ServerSelfUpdater
    {
        private const string KspMpserverFolder = "KSPMPServer";
        private const string StagingSubfolder = "KSPMP-Update-Staging";
        private const string ApplyUpdateCmd = "Kspmp-Apply-Server-Update.cmd";

        private static readonly HashSet<string> PreservedTopLevelDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            "Universe", "Universe_Backup", "Config", "logs", "Backup", "Plugins"
        };

        private static readonly HashSet<string> PreservedRootFileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "LMPPlayerBans.txt"
        };

        public static bool PendingApplyAfterShutdown { get; set; }
        private static string _pendingZipUrl;

        public static void TryOfferUpdateOnStartup()
        {
            if (!Environment.UserInteractive || Console.IsInputRedirected || !RepoConstants.GithubReleaseUpdateChecksEnabled)
                return;
            if (!IsRemoteNewerThanCurrent(out var latest))
                return;
            if (!TryResolveServerZipUrl(out var zipUrl, out var note))
            {
                if (!string.IsNullOrEmpty(note))
                    LunaLog.Debug("[KSPMP] Server update check: " + note);
                return;
            }

            Console.WriteLine();
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine(" A newer KSPMP dedicated server is available: " + latest);
            Console.WriteLine(" You are on: " + LmpVersioning.CurrentVersion);
            Console.WriteLine(" (Universe, Config, logs, Plugins, and your data are not overwritten by this step.)");
            Console.WriteLine("--------------------------------------------------");
            Console.Write("Update now? [y/N] ");
            if (!ReadYes())
            {
                Console.WriteLine("Continuing with the current build.");
                return;
            }

            try
            {
                if (!ApplyFromZipUrl(zipUrl))
                {
                    Console.WriteLine("Update was not applied (see log for details).");
                    return;
                }
                // Windows success path: ApplyFromZipUrl launches helper and Environment.Exit(0) — we never return here.
                // Non-Windows: merged in process; need to relaunch.
                RelaunchAfterInProcessUpdate("KSPMP server files were updated. A new process was started — this window will close.");
            }
            catch (Exception e)
            {
                LunaLog.Error("[KSPMP] Server self-update failed: " + e);
                Console.WriteLine("Update failed: " + e.Message);
            }
        }

        public static void TryOfferUpdateBeforeExit()
        {
            if (!Environment.UserInteractive || Console.IsInputRedirected || !RepoConstants.GithubReleaseUpdateChecksEnabled)
                return;
            if (!IsRemoteNewerThanCurrent(out var latest) || !TryResolveServerZipUrl(out var zipUrl, out _))
                return;

            Console.WriteLine();
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine(" A newer KSPMP server is available: " + latest);
            Console.WriteLine(" Your install: " + LmpVersioning.CurrentVersion);
            Console.WriteLine("--------------------------------------------------");
            Console.Write("After this shutdown, download and install that build, then start the server again? [y/N] ");
            if (ReadYes())
            {
                PendingApplyAfterShutdown = true;
                _pendingZipUrl = zipUrl;
            }
        }

        public static void RunPendingShutdownUpdateIfAny()
        {
            if (!PendingApplyAfterShutdown) return;
            var url = _pendingZipUrl;
            if (string.IsNullOrEmpty(url) && !TryResolveServerZipUrl(out url, out var err))
            {
                try { Console.WriteLine("[KSPMP] Update after shutdown: " + (err ?? "no download URL.")); } catch { }
                ClearPending();
                return;
            }
            ClearPending();
            try
            {
                if (!ApplyFromZipUrl(url))
                {
                    try { Console.WriteLine("[KSPMP] Server self-update (after shutdown) did not complete."); } catch { }
                    return;
                }
                // Windows: process exits; non-Windows: in-process then relaunch
                RelaunchAfterInProcessUpdate("KSPMP server was updated. Starting a new process — this window will close.");
            }
            catch (Exception e)
            {
                try { Console.WriteLine("[KSPMP] Update after shutdown failed: " + e); } catch { }
            }
        }

        public static void ClearPending()
        {
            PendingApplyAfterShutdown = false;
            _pendingZipUrl = null;
        }

        private static void RelaunchAfterInProcessUpdate(string message)
        {
            if (!Common.PlatformIsWindows())
            {
                if (!string.IsNullOrEmpty(message))
                {
                    try { Console.WriteLine(message); } catch { }
                    try { LunaLog.Info("[KSPMP] " + message); } catch { }
                }
                var exe = GetCurrentExePath();
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                {
                    try { Console.WriteLine("[KSPMP] Could not find executable; restart the server from this install folder by hand."); } catch { }
                    return;
                }
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exe,
                        WorkingDirectory = AppContext.BaseDirectory,
                        UseShellExecute = true,
                    });
                }
                catch (Exception e)
                {
                    try { Console.WriteLine("[KSPMP] Could not start new process: " + e.Message); } catch { }
                    return;
                }
                Environment.Exit(0);
            }
        }

        private static string GetCurrentExePath()
        {
            try
            {
                var m = Process.GetCurrentProcess().MainModule;
                if (!string.IsNullOrEmpty(m?.FileName) && File.Exists(m.FileName)) return m.FileName;
            }
            catch
            { }
            var a = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(a) && File.Exists(a)) return a;
            if (AppContext.BaseDirectory is { Length: > 0 } b)
            {
                var p = Path.Combine(b, "Server.exe");
                if (File.Exists(p)) return p;
            }
            return a ?? string.Empty;
        }

        private static bool ReadYes()
        {
            try
            {
                var key = Console.ReadKey(intercept: true);
                try { Console.WriteLine(); } catch { }
                if (key.Key == ConsoleKey.Y) return true;
            }
            catch
            { }
            return false;
        }

        public static bool IsRemoteNewerThanCurrent(out Version latest)
        {
            latest = new Version(0, 0, 0);
            if (!RepoConstants.GithubReleaseUpdateChecksEnabled) return false;
            try
            {
                if (GithubUpdateChecker.LatestRelease == null) return false;
                latest = GithubUpdateChecker.GetLatestVersion();
                if (latest <= LmpVersioning.CurrentVersion) return false;
            }
            catch
            {
                return false;
            }
            return true;
        }

        public static bool TryResolveServerZipUrl(out string url, out string errorNote)
        {
            url = null;
            errorNote = null;
            var isDebug = false;
#if DEBUG
            isDebug = true;
#endif
            url = GithubUpdateDownloader.GetZipFileUrl(GithubProduct.Server, isDebug);
            if (string.IsNullOrEmpty(url))
            {
                url = GithubUpdateDownloader.GetZipFileUrl(GithubProduct.Server, false);
                if (isDebug) errorNote = "Using server Release asset (no Debug server zip on this release).";
            }
            if (string.IsNullOrEmpty(url))
            {
                try
                {
                    if (GithubUpdateChecker.LatestRelease is GitHubRelease r)
                        errorNote = "No matching KSPMultiplayer-Server-*.zip on the latest release. Open " + (r.HtmlUrl ?? RepoConstants.LatestGithubReleaseUrl) + " to download manually.";
                }
                catch { }
                return false;
            }
            return true;
        }

        /// <summary>Windows: may call <see cref="Environment.Exit"/> on success. Non-Windows: merges in this process, returns true/false.</summary>
        public static bool ApplyFromZipUrl(string zipUrl)
        {
            if (string.IsNullOrEmpty(zipUrl)) return false;
            var baseDir = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(baseDir)) return false;
            if (Common.PlatformIsWindows())
            {
                if (TryWindowsDeferredUpdate(zipUrl, baseDir, out var err))
                {
                    // Only returns true when helper was started; this process is about to exit.
                    Environment.Exit(0);
                }
                if (!string.IsNullOrEmpty(err))
                {
                    LunaLog.Error("[KSPMP] Server self-update: " + err);
                    try { Console.WriteLine(err); } catch { }
                }
                return false;
            }
            return TryInProcessUpdateFromUrl(zipUrl, baseDir, out _);
        }

        /// <summary>Windows: download+extract to install staging, write .cmd with robocopy+start, run helper, return true to signal caller to System.Exit(0) immediately.</summary>
        private static bool TryWindowsDeferredUpdate(string zipUrl, string baseDir, out string error)
        {
            error = null;
            var stageRoot = Path.Combine(baseDir, StagingSubfolder);
            try
            {
                if (Directory.Exists(stageRoot))
                {
                    try { Directory.Delete(stageRoot, true); } catch (Exception e) { error = "Could not clear staging: " + e.Message; return false; }
                }
                Directory.CreateDirectory(stageRoot);
                var localZip = Path.Combine(stageRoot, "server.zip");
                if (!DownloadToFile(zipUrl, localZip))
                {
                    error = "Download failed or empty file.";
                    return false;
                }
                var ext = Path.Combine(stageRoot, "ext");
                ZipFile.ExtractToDirectory(localZip, ext, true);
                var kspMps = Path.Combine(ext, KspMpserverFolder);
                if (!Directory.Exists(kspMps))
                {
                    error = "Package did not contain " + KspMpserverFolder + "\\ (invalid zip).";
                    return false;
                }
                // Prefer the published apphost; MainModule can be "dotnet.exe" if the user started via "dotnet Server.dll".
                var apphost = Path.Combine(baseDir, "Server.exe");
                string exeName;
                if (File.Exists(apphost))
                    exeName = "Server.exe";
                else
                {
                    exeName = Path.GetFileName(GetCurrentExePath() ?? string.Empty);
                    if (string.IsNullOrEmpty(exeName) || !exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        exeName = "Server.exe";
                }
                var cmdPath = Path.Combine(baseDir, ApplyUpdateCmd);
                var cmd = BuildWindowsApplyCommand(baseDir, exeName);
                File.WriteAllText(cmdPath, cmd, new UTF8Encoding(false));
                var psi = new ProcessStartInfo
                {
                    FileName = cmdPath,
                    WorkingDirectory = baseDir,
                    UseShellExecute = true,
                };
                try { Process.Start(psi); } catch (Exception e) { error = "Could not start update script: " + e.Message; return false; }
                try { Console.WriteLine("[KSPMP] A separate window will run robocopy, then start the new server. This process exits so DLLs are unlocked; watch that window for progress."); } catch { }
                try { LunaLog.Info("[KSPMP] Deferring file copy to " + cmdPath + "; exiting."); } catch { }
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private static string BuildWindowsApplyCommand(string baseDir, string exeName)
        {
            var xDir = string.Join(" ", PreservedTopLevelDirs.Select(QuoteForRobocopy));
            var xFiles = string.Join(" ", PreservedRootFileNames.Select(QuoteForRobocopy));
            var b = new StringBuilder(3500);
            b.AppendLine("@echo off");
            b.AppendLine("setlocal");
            b.AppendLine("chcp 65001 >NUL");
            b.AppendLine("cd /d \"%~dp0\"");
            b.AppendLine("set \"KSPMP_RELAUNCH=" + BatEscapeValue(exeName) + "\"");
            b.AppendLine("title KSPMP - applying server update");
            b.AppendLine("color 0A");
            b.AppendLine("echo.");
            b.AppendLine("echo ============================================================");
            b.AppendLine("echo  KSPMP - server self-update (robocopy to install folder)");
            b.AppendLine("echo  Target: %~dp0");
            b.AppendLine("echo ============================================================");
            b.AppendLine("echo.");
            b.AppendLine("echo [1/4] Waiting a few seconds so the old server can exit and release file locks on DLLs ^(Lidgren, Server, ...^).");
            b.AppendLine("timeout /t 8 /nobreak >nul 2>&1");
            b.AppendLine("if errorlevel 1 ping 127.0.0.1 -n 9 >nul 2>&1");
            b.AppendLine("echo     Done waiting.");
            b.AppendLine("echo.");
            b.AppendLine("set \"SRC=%~dp0" + StagingSubfolder + "\\ext\\" + KspMpserverFolder + "\"");
            b.AppendLine("if not exist \"%SRC%\\*\" (");
            b.AppendLine("  color 0C");
            b.AppendLine("  echo [ERROR] Staging not found.");
            b.AppendLine("  echo   Expect:  \"%~dp0" + StagingSubfolder + "\\ext\\" + KspMpserverFolder + "\"");
            b.AppendLine("  pause");
            b.AppendLine("  exit /b 1");
            b.AppendLine(")");
            b.AppendLine("echo [2/4] Staged build folder:");
            b.AppendLine("echo         \"%SRC%\"");
            b.AppendLine("set \"KSPMP_STAGED_DLL=%SRC%\\Server.dll\"");
            b.AppendLine("echo.");
            b.AppendLine("echo --- Staged package: Server.dll assembly version ^(what you are about to install^) ---");
            b.AppendLine("if exist \"%KSPMP_STAGED_DLL%\" (");
            b.AppendLine("  powershell -NoProfile -Command \"try { $a=[System.Reflection.AssemblyName]::GetAssemblyName($env:KSPMP_STAGED_DLL); Write-Host ('  ' + $a.Version) } catch { Write-Host $_.Exception.Message; exit 1 }\"");
            b.AppendLine(") else (");
            b.AppendLine("  color 0C");
            b.AppendLine("  echo [ERROR] Missing \"%KSPMP_STAGED_DLL%\"");
            b.AppendLine("  pause");
            b.AppendLine("  exit /b 1");
            b.AppendLine(")");
            b.AppendLine("echo.");
            b.AppendLine("echo [3/4] Merging new files. Skipping user data: Universe, Config, logs, Plugins, Backup, ... ^(robocopy /XD, /XF bans file^).");
            b.AppendLine("echo        You should see a file list and a summary. Exit codes 0-7 = OK for robocopy.");
            b.AppendLine("echo.");
            b.AppendLine("robocopy \"%SRC%\" \"%~dp0.\" /E /R:2 /W:1 " + (xDir.Length > 0 ? "/XD " + xDir + " " : "") + (xFiles.Length > 0 ? " /XF " + xFiles : "") + " /IS /IT /NDL /NP");
            b.AppendLine("if errorlevel 8 (");
            b.AppendLine("  color 0C");
            b.AppendLine("  echo.");
            b.AppendLine("  echo [ERROR] Robocopy failed ^(if errorlevel 8 means 8 or higher; see robocopy docs^).");
            b.AppendLine("  pause");
            b.AppendLine("  exit /b 1");
            b.AppendLine(")");
            b.AppendLine("color 0A");
            b.AppendLine("set \"KSPMP_INST_DLL=%~dp0Server.dll\"");
            b.AppendLine("echo.");
            b.AppendLine("echo --- Install folder: Server.dll assembly version after merge ^(should match staged if copy succeeded^) ---");
            b.AppendLine("if exist \"%KSPMP_INST_DLL%\" (");
            b.AppendLine("  powershell -NoProfile -Command \"try { $a=[System.Reflection.AssemblyName]::GetAssemblyName($env:KSPMP_INST_DLL); Write-Host ('  ' + $a.Version) } catch { Write-Host $_.Exception.Message; exit 1 }\"");
            b.AppendLine(") else (");
            b.AppendLine("  color 0C");
            b.AppendLine("  echo [ERROR] Server.dll not found at \"%KSPMP_INST_DLL%\"");
            b.AppendLine("  pause");
            b.AppendLine("  exit /b 1");
            b.AppendLine(")");
            b.AppendLine("echo.");
            b.AppendLine("echo [4/4] Cleaning staging folder, then starting the new server process...");
            b.AppendLine("rmdir /S /Q \"%~dp0" + StagingSubfolder + "\" 2>NUL");
            b.AppendLine("if exist \"%~dp0%KSPMP_RELAUNCH%\" (");
            b.AppendLine("  echo Starting:  \"%~dp0%KSPMP_RELAUNCH%\"");
            b.AppendLine("  start \"KSPMP Server\" /D \"%~dp0\" \"%~dp0%KSPMP_RELAUNCH%\"");
            b.AppendLine(") else (");
            b.AppendLine("  color 0C");
            b.AppendLine("  echo [ERROR] Exe not found: %KSPMP_RELAUNCH%");
            b.AppendLine("  pause");
            b.AppendLine("  exit /b 1");
            b.AppendLine(")");
            b.AppendLine("echo.");
            b.AppendLine("echo Update finished. This window stays open so you can read the output above.");
            b.AppendLine("pause");
            b.AppendLine("endlocal");
            b.AppendLine("exit /b 0");
            return b.ToString();
        }

        private static string QuoteForRobocopy(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            if (s.IndexOf(' ', StringComparison.Ordinal) >= 0) return "\"" + s + "\"";
            return s;
        }

        private static string BatEscapeValue(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("^", "^^");
        }

        private static bool TryInProcessUpdateFromUrl(string zipUrl, string baseDir, out string error)
        {
            error = null;
            var tmpRoot = Path.Combine(Path.GetTempPath(), "KSPMPServerUpdate-" + Guid.NewGuid().ToString("N"));
            var tmpZip = Path.Combine(tmpRoot, "server.zip");
            Directory.CreateDirectory(tmpRoot);
            try
            {
                if (!DownloadToFile(zipUrl, tmpZip))
                {
                    error = "Download failed or empty file.";
                    return false;
                }
                var ext = Path.Combine(tmpRoot, "ext");
                ZipFile.ExtractToDirectory(tmpZip, ext, true);
                var kspMps = Path.Combine(ext, KspMpserverFolder);
                if (!Directory.Exists(kspMps))
                {
                    error = "Package did not contain " + KspMpserverFolder + " folder.";
                    return false;
                }
                MergeKspMpserverIntoBase(kspMps, baseDir);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, true);
                }
                catch
                { }
            }
        }

        private static bool DownloadToFile(string url, string dest)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(dest)) return false;
            using (var c = new HttpClient())
            {
                c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "KSPMP-Server-Updater/1.0 (+https://github.com/ItzPray/KSPMulti)");
                c.Timeout = TimeSpan.FromMinutes(20);
                using (var resp = c.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
                {
                    if (!resp.IsSuccessStatusCode) return false;
                    var dir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    using (var s = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                    using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, false))
                    {
                        s.CopyTo(fs);
                    }
                }
            }
            return new FileInfo(dest).Length > 0;
        }

        internal static void MergeKspMpserverIntoBase(string sourceKspMpserverRoot, string targetBase)
        {
            var src = Path.GetFullPath(sourceKspMpserverRoot);
            var targetBaseF = Path.GetFullPath(targetBase);
            long n = 0;
            foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, f);
                if (string.IsNullOrEmpty(rel) || rel.IndexOf("..", StringComparison.Ordinal) >= 0) continue;
                rel = rel.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                if (ShouldSkipMergingPath(rel)) continue;
                var dest = Path.GetFullPath(Path.Combine(targetBaseF, rel));
                if (!IsPathWithinDirectory(targetBaseF, dest)) continue;
                var pdir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(pdir) && !Directory.Exists(pdir))
                    Directory.CreateDirectory(pdir);
                File.Copy(f, dest, overwrite: true);
                n++;
            }
            LunaLog.Info("[KSPMP] Server self-update: merged " + n + " file(s) from package into " + targetBaseF);
        }

        private static bool IsPathWithinDirectory(string baseDir, string childPath)
        {
            var b = Path.GetFullPath(baseDir);
            if (b.Length > 0 && b[^1] != Path.DirectorySeparatorChar && b[^1] != Path.AltDirectorySeparatorChar)
                b += Path.DirectorySeparatorChar;
            var c = Path.GetFullPath(childPath);
            return c.StartsWith(b, StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldSkipMergingPath(string relativeInZip)
        {
            if (string.IsNullOrEmpty(relativeInZip)) return true;
            relativeInZip = relativeInZip.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (!relativeInZip.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return PreservedRootFileNames.Contains(relativeInZip);
            var i = relativeInZip.IndexOf(Path.DirectorySeparatorChar);
            var first = i >= 0 ? relativeInZip.Substring(0, i) : relativeInZip;
            return PreservedTopLevelDirs.Contains(first);
        }
    }
}
