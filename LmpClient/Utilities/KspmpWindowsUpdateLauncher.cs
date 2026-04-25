using LmpClient.Localization;
using LmpCommon;
using LmpGlobal;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace LmpClient.Utilities
{
    /// <summary>Writes a small CMD+PowerShell helper in the KSP install and launches a visible <c>cmd</c> so the user can
    /// close KSP, then download, extract, copy, and validate the client mod outside the game process (Windows only).</summary>
    public static class KspmpWindowsUpdateLauncher
    {
        public const string ExternalUpdateSubfolder = "KSPMP-Update-External";
        private const string Ps1Name = "kspmp-external-update.ps1";
        private const string RunCmdName = "Kspmp-Run-External-Update.cmd";
        private const string ParamsName = "kspmp-external-update-params.txt";

        /// <summary>Windows only. Writes scripts + params and starts an interactive command window. Returns a user message on success, or an error (English technical).</summary>
        public static void TryStartExternalUpdate(string clientZipUrl, out string userMessage, out string error)
        {
            userMessage = null;
            error = null;

            if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
            {
                error = LocalizationContainer.UpdateWindowText.InstallExternalUpdateWindowsOnly;
                return;
            }

            if (string.IsNullOrEmpty(clientZipUrl))
            {
                error = "Client zip URL is missing; open the release page to update.";
                return;
            }

            if (string.IsNullOrEmpty(MainSystem.KspPath) || !Directory.Exists(MainSystem.KspPath))
            {
                error = "KSP path is not set or missing.";
                return;
            }

            var ksp = Path.GetFullPath(MainSystem.KspPath);
            var dir = Path.Combine(ksp, ExternalUpdateSubfolder);
            try { Directory.CreateDirectory(dir); } catch (Exception e) { error = e.Message; return; }

            var ps1Path = Path.Combine(dir, Ps1Name);
            var paramsPath = Path.Combine(dir, ParamsName);
            var cmdPath = Path.Combine(dir, RunCmdName);

            try
            {
                var ps1 = LoadEmbeddedPs1();
                File.WriteAllText(ps1Path, ps1, new UTF8Encoding(false));

                var pfx = "KSPMPClient/GameData/" + ModLayoutConstants.GameDataModFolder + "/";
                var pLines = new StringBuilder(512);
                pLines.AppendLine("v1=1");
                pLines.AppendLine("KspRootB64=" + B64(Encoding.UTF8.GetBytes(ksp)));
                pLines.AppendLine("ZipUrlB64=" + B64(Encoding.UTF8.GetBytes(clientZipUrl)));
                pLines.AppendLine("UserAgentB64=" + B64(Encoding.UTF8.GetBytes(UpdateHandler.GitHubUserAgent)));
                pLines.AppendLine("ModFolder=" + ModLayoutConstants.GameDataModFolder);
                pLines.AppendLine("PackagePrefix=" + pfx);
                File.WriteAllText(paramsPath, pLines.ToString(), new UTF8Encoding(true));

                var relPs1 = Path.GetFileName(ps1Path);
                var relParam = Path.GetFileName(paramsPath);
                var cmd = new StringBuilder(1024);
                cmd.AppendLine("@echo off");
                cmd.AppendLine("setlocal");
                cmd.AppendLine("chcp 65001 >NUL");
                cmd.AppendLine("title KSPMP - client update");
                cmd.AppendLine("cd /d \"%~dp0\"");
                cmd.AppendLine("rem KSP path is stored base64 in " + relParam);
                cmd.AppendLine("echo.");
                cmd.AppendLine("set \"POWERSHELL=powershell.exe\"");
                cmd.AppendLine("if not exist \"%~dp0" + relPs1 + "\" (");
                cmd.AppendLine("  echo ERROR: " + relPs1 + " is missing next to this script.");
                cmd.AppendLine("  pause");
                cmd.AppendLine("  exit /b 1");
                cmd.AppendLine(")");
                cmd.AppendLine("echo Save your game if you can, then continue; this will close KSP for this install.");
                cmd.AppendLine("echo This window will try to close Kerbal Space Program for this KSP copy,");
                cmd.AppendLine("echo download the KSPMP client release zip, then copy GameData\\" + ModLayoutConstants.GameDataModFolder + " into your install.");
                cmd.AppendLine("echo Do not close this window until the script is finished.");
                cmd.AppendLine("echo.");
                cmd.AppendLine("\"%POWERSHELL%\" -NoLogo -NoProfile -ExecutionPolicy Bypass -File \"%~dp0" + relPs1 + "\" \"%~dp0" + relParam + "\"");
                cmd.AppendLine("set ERR=%ERRORLEVEL%");
                cmd.AppendLine("echo.");
                cmd.AppendLine("if not \"%ERR%\"==\"0\" (echo [FAILED] Exit code %ERR% & color 0C) else (echo [SUCCESS] You can start KSP when ready. & color 0A)");
                cmd.AppendLine("pause");
                cmd.AppendLine("endlocal");
                File.WriteAllText(cmdPath, cmd.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { error = e.Message; return; }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cmdPath,
                    WorkingDirectory = dir,
                    UseShellExecute = true,
                };
                Process.Start(psi);
            }
            catch (Exception e)
            {
                error = string.Format(LocalizationContainer.UpdateWindowText.InstallExternalUpdateStartFailed, e.Message);
                return;
            }

            var loc = LocalizationContainer.UpdateWindowText;
            userMessage = string.Format(loc.InstallExternalUpdateLaunched, cmdPath);
            LunaLog.Log("[KSPMP] External update launcher started. CMD: " + cmdPath);
        }

        private static string B64(byte[] b) { return Convert.ToBase64String(b); }

        private static string LoadEmbeddedPs1()
        {
            var asm = typeof(KspmpWindowsUpdateLauncher).Assembly;
            string match = null;
            foreach (var n in asm.GetManifestResourceNames())
            {
                if (n.IndexOf("kspmp-external-update", StringComparison.OrdinalIgnoreCase) < 0) continue;
                match = n; break;
            }
            if (string.IsNullOrEmpty(match)) throw new InvalidOperationException("Embedded " + Ps1Name + " not found in LmpClient assembly (build embedded resource).");
            using (var s = asm.GetManifestResourceStream(match))
            {
                if (s == null) throw new IOException("Resource stream is null: " + match);
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }
    }
}
