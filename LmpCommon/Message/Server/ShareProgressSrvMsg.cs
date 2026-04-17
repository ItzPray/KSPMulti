using Lidgren.Network;
using LmpCommon.Enums;
using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Server.Base;
using LmpCommon.Message.Types;
using System;
using System.Collections.Generic;

namespace LmpCommon.Message.Server
{
    public class ShareProgressSrvMsg : SrvMsgBase<ShareProgressBaseMsgData>
    {
        /// <inheritdoc />
        internal ShareProgressSrvMsg() { }

        /// <inheritdoc />
        public override string ClassName { get; } = nameof(ShareProgressSrvMsg);

        /// <inheritdoc />
        protected override Dictionary<ushort, Type> SubTypeDictionary { get; } = new Dictionary<ushort, Type>
        {
            [(ushort)ShareProgressMessageType.ScienceSubjectUpdate] = typeof(ShareProgressScienceSubjectMsgData),
            [(ushort)ShareProgressMessageType.TechnologyUpdate] = typeof(ShareProgressTechnologyMsgData),
            [(ushort)ShareProgressMessageType.ContractsUpdate] = typeof(ShareProgressContractsMsgData),
            [(ushort)ShareProgressMessageType.AchievementsUpdate] = typeof(ShareProgressAchievementsMsgData),
            [(ushort)ShareProgressMessageType.StrategyUpdate] = typeof(ShareProgressStrategyMsgData),
            [(ushort)ShareProgressMessageType.FacilityUpgrade] = typeof(ShareProgressFacilityUpgradeMsgData),
            [(ushort)ShareProgressMessageType.PartPurchase] = typeof(ShareProgressPartPurchaseMsgData),
            [(ushort)ShareProgressMessageType.ExperimentalPart] = typeof(ShareProgressExperimentalPartMsgData),
        };

        public override ServerMessageType MessageType => ServerMessageType.ShareProgress;

        protected override int DefaultChannel => 21;

        public override NetDeliveryMethod NetDeliveryMethod => NetDeliveryMethod.ReliableOrdered;
    }
}
