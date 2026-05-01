using LmpCommon.PersistentSync.Payloads.UpgradeableFacilities;
using LmpCommon.PersistentSync.Payloads.Technology;
using LmpCommon.PersistentSync.Payloads.Strategy;
using LmpCommon.PersistentSync.Payloads.ScienceSubjects;
using LmpCommon.PersistentSync.Payloads.PartPurchases;
using LmpCommon.PersistentSync.Payloads.ExperimentalParts;
using LmpCommon.PersistentSync.Payloads.Contracts;
using LmpCommon.PersistentSync.Payloads.Achievements;
using KSP.UI.Screens;
using LmpClient;
using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Extensions;
using LmpClient.Network;
using LmpClient.Systems.PersistentSync;
using LmpClient.Utilities;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Interface;
using LmpCommon.PersistentSync;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LmpClient.Systems.ShareTechnology
{
    public class ShareTechnologyMessageSender : SubSystem<ShareTechnologySystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<ShareProgressCliMsg>(msg)));
        }

        public void SendTechnologyMessage(RDTech tech)
        {
            if (PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainNames.Technology))
            {
                // Do not call UnlockProtoTechNode here: OnTechnologyResearched already ran after stock applied
                // the unlock. Re-invoking stock unlock can reset or corrupt local tech state on some KSP builds,
                // which then yields empty/wrong technology snapshots and breaks R&D. We only push authoritative
                // state to the server and sync the RnD tree from the singleton for UI.
                System.StartIgnoringEvents();
                try
                {
                    if (ResearchAndDevelopment.Instance != null && tech != null)
                    {
                        TechnologyPersistentSyncClientDomain.SyncRnDTechTreeFromResearchInstance();
                        TechnologyPersistentSyncClientDomain.EnsureImplicitPurchasedPartsForAvailableTechsIfNeeded($"TechnologyUnlock:{tech.techID}");
                    }

                    var technologies = CreateCurrentTechnologySnapshot();
                    if (technologies.Count == 0)
                    {
                        return;
                    }

                    var reason = $"TechnologyUnlock:{tech.techID}";
                    PersistentSyncSystem.Singleton.MessageSender.SendTechnologyIntent(technologies.ToArray(), reason);
                    SendPartPurchasesIntentForUnlockedTech(tech?.techID, reason);
                }
                finally
                {
                    System.StopIgnoringEvents();
                }

                // Stock clears graph selection and leaves purchase affordances stale until the node is clicked again.
                if (tech != null && RDController.Instance != null)
                {
                    ShareTechnologySystem.RegisterLocalResearchTechForSidePanelReselect(tech.techID);
                    System.RefreshResearchAndDevelopmentPurchasesOnly($"TechnologyUnlockUi:{tech.techID}");
                    var techId = tech.techID;
                    CoroutineUtil.StartFrameDelayedRoutine(
                        nameof(ShareTechnologySystem) + ".TechnologyUnlockUiTail2",
                        () =>
                        {
                            if (RDController.Instance != null)
                            {
                                ShareTechnologySystem.TryReselectRnDTechOnControllerForSidePanel(techId);
                                ShareTechnologySystem.Singleton?.RefreshResearchAndDevelopmentPurchasesOnly(
                                    $"TechnologyUnlockUi+2f:{techId}");
                            }
                        },
                        2);
                    CoroutineUtil.StartFrameDelayedRoutine(
                        nameof(ShareTechnologySystem) + ".TechnologyUnlockUiTail5",
                        () =>
                        {
                            if (RDController.Instance != null)
                            {
                                ShareTechnologySystem.TryReselectRnDTechOnControllerForSidePanel(techId);
                                ShareTechnologySystem.Singleton?.RefreshResearchAndDevelopmentPurchasesOnly(
                                    $"TechnologyUnlockUi+5f:{techId}");
                            }
                        },
                        5);
                }

                return;
            }

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<ShareProgressTechnologyMsgData>();
            msgData.TechNode.Id = tech.techID;

            var configNode = ConvertTechNodeToConfigNode(tech);
            if (configNode == null) return;

            var data = configNode.Serialize();
            var numBytes = data.Length;

            msgData.TechNode.NumBytes = numBytes;
            if (msgData.TechNode.Data.Length < numBytes)
                msgData.TechNode.Data = new byte[numBytes];

            Array.Copy(data, msgData.TechNode.Data, numBytes);

            SendMessage(msgData);
        }

        private static ConfigNode ConvertTechNodeToConfigNode(RDTech techNode)
        {
            var configNode = new ConfigNode();
            try
            {
                configNode.AddValue("id", techNode.techID);
                configNode.AddValue("state", techNode.state);
                configNode.AddValue("cost", techNode.scienceCost);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[KSPMP]: Error while saving tech node: {e}");
                return null;
            }

            return configNode;
        }

        private static List<TechnologySnapshotInfo> CreateCurrentTechnologySnapshot()
        {
            var technologies = new List<TechnologySnapshotInfo>();
            if (ResearchAndDevelopment.Instance == null || AssetBase.RnDTechTree == null)
            {
                return technologies;
            }

            var stateCounts = new Dictionary<RDTech.State, int>();
            var availableIds = new List<string>();
            var treeTechCount = 0;
            var nullStateCount = 0;

            foreach (var tech in AssetBase.RnDTechTree.GetTreeTechs().Where(t => t != null))
            {
                treeTechCount++;
                var techState = ResearchAndDevelopment.Instance.GetTechState(tech.techID);
                if (techState == null)
                {
                    nullStateCount++;
                    continue;
                }

                if (!stateCounts.ContainsKey(techState.state))
                {
                    stateCounts[techState.state] = 0;
                }
                stateCounts[techState.state]++;

                if (techState.state == RDTech.State.Unavailable)
                {
                    continue;
                }

                availableIds.Add(techState.techID);

                var configNode = new ConfigNode();
                configNode.AddValue("id", techState.techID);
                configNode.AddValue("state", techState.state);
                configNode.AddValue("cost", techState.scienceCost);

                var data = configNode.Serialize();
                technologies.Add(new TechnologySnapshotInfo
                {
                    TechId = techState.techID,
                    Data = data
                });
            }

            var stateSummary = string.Join(",", stateCounts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
            LunaLog.Log($"[PersistentSync] CreateCurrentTechnologySnapshot treeTechs={treeTechCount} nullState={nullStateCount} stateCounts=[{stateSummary}] sending={technologies.Count} availableIds=[{string.Join(",", availableIds.OrderBy(id => id))}]");

            return technologies;
        }

        /// <summary>
        /// Invokes whichever <see cref="ResearchAndDevelopment.UnlockProtoTechNode"/> overload exists in the
        /// referenced KSP assemblies. RnD tree nodes may be <see cref="RDTech"/> or <see cref="ProtoTechNode"/>
        /// depending on game version; pass the tree reference as <see cref="object"/>.
        /// </summary>
        internal static void UnlockProtoTechNodeCompat(object rdTreeOrProto)
        {
            if (ResearchAndDevelopment.Instance == null || rdTreeOrProto == null)
            {
                return;
            }

            var rd = ResearchAndDevelopment.Instance;
            var rdType = typeof(ResearchAndDevelopment);
            var unlockRdt = rdType.GetMethod("UnlockProtoTechNode", new[] { typeof(RDTech) });
            var unlockProto = rdType.GetMethod("UnlockProtoTechNode", new[] { typeof(ProtoTechNode) });

            if (rdTreeOrProto is RDTech rdt)
            {
                if (unlockRdt != null)
                {
                    unlockRdt.Invoke(rd, new object[] { rdt });
                    return;
                }

                if (unlockProto != null)
                {
                    var proto = rd.GetTechState(rdt.techID) ?? ProtoTechNodeFromRnDTech(rdt);
                    if (proto != null)
                    {
                        unlockProto.Invoke(rd, new object[] { proto });
                    }

                    return;
                }
            }

            if (rdTreeOrProto is ProtoTechNode protoNode)
            {
                if (unlockProto != null)
                {
                    unlockProto.Invoke(rd, new object[] { protoNode });
                    return;
                }

                if (unlockRdt != null && AssetBase.RnDTechTree != null)
                {
                    // GetTreeTechs() may be typed as ProtoTechNode; cast via object so `is RDTech` is legal.
                    foreach (var candidate in AssetBase.RnDTechTree.GetTreeTechs().Where(x => x != null))
                    {
                        if ((object)candidate is RDTech treeRdt && treeRdt.techID == protoNode.techID)
                        {
                            unlockRdt.Invoke(rd, new object[] { treeRdt });
                            return;
                        }
                    }
                }
            }

            LunaLog.LogWarning("[KSPMP] ResearchAndDevelopment.UnlockProtoTechNode has no compatible overload or tree node type; stock unlock skipped.");
        }

        private static ProtoTechNode ProtoTechNodeFromRnDTech(RDTech tech)
        {
            if (tech == null)
            {
                return null;
            }

            return new ProtoTechNode
            {
                techID = tech.techID,
                scienceCost = tech.scienceCost,
                state = tech.state,
                partsPurchased = new List<AvailablePart>((tech.partsPurchased ?? new List<AvailablePart>()).Where(part => part != null))
            };
        }

        private static void SendPartPurchasesIntentForUnlockedTech(string techId, string technologyReason)
        {
            if (string.IsNullOrEmpty(techId) || !PersistentSyncSystem.IsLiveForDomain(PersistentSyncDomainNames.PartPurchases))
            {
                return;
            }

            var rd = ResearchAndDevelopment.Instance;
            if (rd == null)
            {
                return;
            }

            var state = rd.GetTechState(techId);
            if (state == null || state.state != RDTech.State.Available)
            {
                return;
            }

            var names = (state.partsPurchased ?? new List<AvailablePart>())
                .Where(part => part != null)
                .Select(part => part.name)
                .Distinct()
                .ToArray();

            if (names.Length == 0)
            {
                var treeTech = AssetBase.RnDTechTree?.GetTreeTechs()?.FirstOrDefault(t => t != null && t.techID == techId);
                if (treeTech?.partsPurchased == null || treeTech.partsPurchased.Count == 0)
                {
                    LunaLog.LogWarning($"[PersistentSync] Technology unlock had no purchasable part names for techId={techId}; PartPurchases intent skipped ({technologyReason})");
                    return;
                }

                names = treeTech.partsPurchased
                    .Where(part => part != null)
                    .Select(part => part.name)
                    .Distinct()
                    .ToArray();
            }

            if (names.Length == 0)
            {
                return;
            }

            PersistentSyncSystem.Singleton.MessageSender.SendPartPurchasesIntent(new[]
            {
                new PartPurchaseSnapshotInfo
                {
                    TechId = techId,
                    PartNames = names
                }
            }, $"{technologyReason}:parts");
        }
    }
}

