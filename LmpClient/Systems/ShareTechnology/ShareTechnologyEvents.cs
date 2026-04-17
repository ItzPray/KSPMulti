using LmpClient;
using LmpClient.Base;
using LmpClient.Systems.PersistentSync;

namespace LmpClient.Systems.ShareTechnology
{
    public class ShareTechnologyEvents : SubSystem<ShareTechnologySystem>
    {
        public void TechnologyResearched(GameEvents.HostTargetAction<RDTech, RDTech.OperationResult> data)
        {
            if (System.IgnoreEvents || data.target != RDTech.OperationResult.Successful) return;

            LunaLog.Log($"[PersistentSync] local technology change techId={data.host.techID} sending canonical R&D snapshot");
            System.MessageSender.SendTechnologyMessage(data.host);
        }
    }
}
