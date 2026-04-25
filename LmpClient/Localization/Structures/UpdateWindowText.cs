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
    }
}
