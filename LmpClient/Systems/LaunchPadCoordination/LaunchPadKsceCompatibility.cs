using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LmpClient;
using LmpClient.Systems.Mod;
using LmpCommon;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;

namespace LmpClient.Systems.LaunchPadCoordination
{
    /// <summary>
    /// Optional strict check for a pinned optional DLL (SHA + file version range) when launch pad coordination is enabled.
    /// Mirrors the mod-control “pin plugin hash” idea from the server settings reply.
    /// </summary>
    public static class LaunchPadKsceCompatibility
    {
        private static bool _loggedFailure;
        private static bool _loggedSuccess;

        public static bool StrictKsceDllCheckFailed { get; private set; }

        /// <summary>Kerbal Konstructs / KSCE-specific Harmony patches may run only when coordination is on and strict DLL checks pass.</summary>
        public static bool KsceHarmonyPatchesAllowed =>
            SettingsSystem.ServerSettings.LaunchPadCoordMode != LaunchPadCoordinationMode.Off &&
            !StrictKsceDllCheckFailed;

        public static void RevalidateAfterSettingsSync()
        {
            StrictKsceDllCheckFailed = false;
            _loggedFailure = false;
            _loggedSuccess = false;

            if (SettingsSystem.ServerSettings.LaunchPadCoordMode == LaunchPadCoordinationMode.Off)
                return;

            if (!SettingsSystem.ServerSettings.LaunchPadKsceEnforceOptionalDllMatch)
                return;

            var rel = (SettingsSystem.ServerSettings.LaunchPadKsceOptionalDllRelativePath ?? string.Empty).Trim()
                .Replace('\\', '/').ToLowerInvariant();
            if (string.IsNullOrEmpty(rel))
            {
                LunaLog.LogWarning("[LaunchPad][KSCE] Enforcement is on but LaunchPadKsceOptionalDllRelativePath is empty; skipping DLL pin.");
                return;
            }

            var match = ModSystem.Singleton.DllList.Keys.FirstOrDefault(k =>
                k.Replace('\\', '/').ToLowerInvariant().EndsWith(rel, StringComparison.Ordinal));
            if (match == null)
            {
                Fail($"[LaunchPad][KSCE] Pinned optional DLL not found in mod scan (expected path ending with '{rel}').");
                return;
            }

            var fullPath = Path.Combine(MainSystem.KspPath, "GameData", match.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                Fail($"[LaunchPad][KSCE] Pinned DLL path does not exist: {fullPath}");
                return;
            }

            var expectedSha = (SettingsSystem.ServerSettings.LaunchPadKsceOptionalDllSha256 ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(expectedSha))
            {
                var actual = Common.CalculateSha256FileHash(fullPath);
                if (!string.Equals(actual, expectedSha, StringComparison.OrdinalIgnoreCase))
                {
                    Fail($"[LaunchPad][KSCE] SHA256 mismatch for '{match}'. Expected {expectedSha}, got {actual}.");
                    return;
                }
            }

            var minV = (SettingsSystem.ServerSettings.LaunchPadKsceMinPluginFileVersion ?? string.Empty).Trim();
            var maxV = (SettingsSystem.ServerSettings.LaunchPadKsceMaxPluginFileVersion ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(minV) || !string.IsNullOrEmpty(maxV))
            {
                var fv = FileVersionInfo.GetVersionInfo(fullPath).FileVersion ?? "0.0.0.0";
                if (!string.IsNullOrEmpty(minV) && CompareVersion(fv, minV) < 0)
                {
                    Fail($"[LaunchPad][KSCE] FileVersion {fv} is below minimum {minV} for '{match}'.");
                    return;
                }

                if (!string.IsNullOrEmpty(maxV) && CompareVersion(fv, maxV) > 0)
                {
                    Fail($"[LaunchPad][KSCE] FileVersion {fv} is above maximum {maxV} for '{match}'.");
                    return;
                }
            }

            if (!_loggedSuccess)
            {
                _loggedSuccess = true;
                LunaLog.Log($"[LaunchPad][KSCE] Optional DLL pin OK for '{match}'.");
            }
        }

        private static void Fail(string message)
        {
            StrictKsceDllCheckFailed = true;
            if (_loggedFailure) return;
            _loggedFailure = true;
            LunaLog.LogError(message);
        }

        private static int CompareVersion(string a, string b)
        {
            try
            {
                return new Version(ParseVersion(a)).CompareTo(new Version(ParseVersion(b)));
            }
            catch
            {
                return string.CompareOrdinal(a, b);
            }
        }

        private static string ParseVersion(string v)
        {
            var parts = (v ?? "0").Split('.');
            while (parts.Length < 4)
            {
                var list = parts.ToList();
                list.Add("0");
                parts = list.ToArray();
            }

            return string.Join(".", parts.Take(4));
        }
    }
}
