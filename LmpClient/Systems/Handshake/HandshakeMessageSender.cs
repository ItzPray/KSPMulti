using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Network;
using LmpClient.Systems.SettingsSys;
using LmpCommon;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.Handshake;
using LmpCommon.Message.Interface;

namespace LmpClient.Systems.Handshake
{
    public class HandshakeMessageSender : SubSystem<HandshakeSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<HandshakeCliMsg>(msg)));
        }

        public void SendHandshakeRequest()
        {
            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<HandshakeRequestMsgData>();
            msgData.PlayerName = SettingsSystem.CurrentSettings.PlayerName;
            msgData.UniqueIdentifier = MainSystem.UniqueIdentifier;
            msgData.ProtocolForkId = SessionAdmission.LocalProtocolForkId;
            msgData.ExactClientBuild = SessionAdmission.LocalExactBuild;

            SendMessage(msgData);
        }
    }
}
