using LmpClient.Base;

namespace LmpClient.Systems.ShareReputation
{
    public class ShareReputationEvents : SubSystem<ShareReputationSystem>
    {
        public void ReputationChanged(float reputation, TransactionReasons reason)
        {
            if (System.IgnoreEvents)
            {
                // Targeted diagnostic: a client freeze was observed mid-ContractReward where this branch
                // silently dropped a legitimate Reputation change. Logging the suppressed branch makes it
                // possible to distinguish "suppression scope leaked past its owner" (log line present) from
                // "main thread froze inside the unsuppressed branch" (log line absent) in future reports.
                LunaLog.Log($"[KSPMP] Reputation event suppressed (IgnoreEvents=true) reputation={reputation} reason={reason}");
                return;
            }

            LunaLog.Log($"Reputation changed to: {reputation} reason: {reason}");
            System.MessageSender.SendReputationMsg(reputation, reason.ToString());
        }

        public void RevertingDetected()
        {
            System.Reverting = true;
            System.StartIgnoringEvents();
        }

        public void RevertingToEditorDetected(EditorFacility data)
        {
            System.Reverting = true;
            System.StartIgnoringEvents();
        }

        public void LevelLoaded(GameScenes data)
        {
            if (System.Reverting)
            {
                System.Reverting = false;
                System.StopIgnoringEvents(true);
            }
        }
    }
}
