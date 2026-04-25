namespace LmpClient.Localization.Structures
{
    public class UpdateWindowText
    {
        public string Title { get; set; } = "New update available";
        public string Text { get; set; } = "There is a new version of KSP Multiplayer available for download";
        public string StillCompatible { get; set; } = "Your current KSP Multiplayer version is compatible with the latest version";
        public string NotCompatible { get; set; } = "Your current KSP Multiplayer version is not compatible with the latest version";
        public string Changelog { get; set; } = "Changelog";
        public string CurrentVersion { get; set; } = "Your current version:";
        public string LatestVersion { get; set; } = "Latest version:";
        public string DownloadAndInstallKspmp { get; set; } = "Download and install";
        public string OpenReleasesInBrowser { get; set; } = "Open release page in browser";
        public string InstallWorking { get; set; } = "Working: {0}";
        public string InstallPhaseDownloading { get; set; } = "Downloading…";
        public string InstallPhaseExtracting { get; set; } = "Extracting to GameData\\{0}…";
        public string InstallFilesWritten { get; set; } = "Installed {0} file(s).";
        public string InstallPhaseVerifying { get; set; } = "Verifying {0} file(s)…";
        public string InstallSuccessRestartKsp { get; set; } = "KSPMP files were updated. Quit KSP completely and start it again to load the new KSPMP plugins.";
        public string InstallFailed { get; set; } = "Update failed: {0}";
        public string InstallValidationMissingFile { get; set; } = "Missing: {0}";
        public string InstallValidationSizeMismatch { get; set; } = "Size mismatch for {0} (expected {1} B, on disk {2} B).";
        public string InstallValidationInvalidModule { get; set; } = "Invalid or truncated DLL: {0}";
        public string InstallFallingBackSaveZip { get; set; } = "KSP is using the mod, so the folder cannot be patched. Saving the client package to your KSP install…";
        public string InstallSuccessZipSaved { get; set; } = "Client package saved to: {0}. Fully quit KSP, then open that zip, copy the GameData\\KSPMultiplayer folder (from the KSPMPClient/GameData path) into your GameData, or extract with merge/replace, then start the game again.";
        public string InstallErrorFilesInUseKsp { get; set; } = "A mod file was in use and could not be replaced (KSP is still loading it). This usually means the game was not fully quit. If this persists after a full exit, use Open release page to download the client zip, quit KSP, and copy the KSPMultiplayer folder into GameData by hand.";
        public string InstallExternalUpdateLaunched { get; set; } = "A command window was started. KSP will be closed, then the client package is downloaded and installed. If it does not start, run: {0}";
        public string InstallExternalUpdateStartFailed { get; set; } = "Could not start the update command window: {0}";
        public string InstallExternalUpdateWindowsOnly { get; set; } = "The command-window update helper is only available on Windows.";
    }
}
