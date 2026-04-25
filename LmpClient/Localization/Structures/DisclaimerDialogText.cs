namespace LmpClient.Localization.Structures
{
    public class DisclaimerDialogText
    {
        public string Text { get; set; } = "KSP Multiplayer (KSPMP) shares the following personally identifiable information with the master server and any server you connect to.\n"
                                        + "a) Your player name you connect with.\n"
                                        + "b) Your player token (A randomly generated string to authenticate you).\n"
                                        + "c) Your IP address is logged on the server console.\n"
                                        + "\n"
                                        + "KSP Multiplayer does not contact any other computer than the server you are connecting to and the master server.\n"
                                        + "In order to use KSP Multiplayer, you must allow it to use this info\n"
                                        + "\n"
                                        + "For more information - see the KSP addon rules\n";

        public string Title { get; set; } = "KSP Multiplayer - Disclaimer";
        public string Accept { get; set; } = "Accept";
        public string Decline { get; set; } = "Decline";
    }
}
