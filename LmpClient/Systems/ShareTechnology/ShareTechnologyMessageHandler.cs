using System.Collections.Concurrent;
using System.Linq;
using KSP.UI.Screens;
using LmpClient;
using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;
using LmpCommon.PersistentSync;
using LmpClient.Systems.PersistentSync;

namespace LmpClient.Systems.ShareTechnology
{
    public class ShareTechnologyMessageHandler : SubSystem<ShareTechnologySystem>, IMessageHandler
    {
        public ConcurrentQueue<IServerMessageBase> IncomingMessages { get; set; } = new ConcurrentQueue<IServerMessageBase>();

        public void HandleMessage(IServerMessageBase msg)
        {
            if (!(msg.Data is ShareProgressBaseMsgData msgData)) return;
            if (msgData.ShareProgressMessageType != ShareProgressMessageType.TechnologyUpdate) return;

            if (PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainNames.Technology))
            {
                LunaLog.LogWarning("[PersistentSync] bypass guard: ShareProgress technology update received after R&D migrated to PersistentSync snapshots. Message ignored.");
                return;
            }

            if (msgData is ShareProgressTechnologyMsgData data)
            {
                var tech = new TechNodeInfo(data.TechNode); //create a copy of the tech value so it will not change in the future.
                LunaLog.Log($"Queue TechnologyResearch with: {tech.Id}");
                System.QueueAction(() =>
                {
                    TechnologyResearch(tech);
                });
            }
        }

        private static void TechnologyResearch(TechNodeInfo tech)
        {
            System.StartIgnoringEvents();
            var node = AssetBase.RnDTechTree.GetTreeTechs().ToList().Find(n => n.techID == tech.Id);
            if (node == null)
            {
                LunaLog.LogError($"[CareerSync:e0] technology update dropped: R&D tech node not found for techId={tech.Id} (TechnologyUpdate handler path)");
                System.StopIgnoringEvents();
                return;
            }

            LunaLog.Log($"[CareerSync:e0] tech update applied from TechnologyUpdate message (single-node unlock path) techId={tech.Id}");
            ShareTechnologyMessageSender.UnlockProtoTechNodeCompat((object)node);

            System.RefreshResearchAndDevelopmentUiAdapters("LegacyTechnologyUpdate");

            System.StopIgnoringEvents();
            LunaLog.Log($"TechnologyResearch received - technology researched: {tech.Id}");
        }
    }
}
