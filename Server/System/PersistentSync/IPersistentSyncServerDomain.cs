using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using LmpCommon.Message.Data.PersistentSync;
using Server.Client;

namespace Server.System.PersistentSync
{
    public interface IPersistentSyncServerDomain
    {
        PersistentSyncDomainId DomainId { get; }
        PersistentAuthorityPolicy AuthorityPolicy { get; }
        void LoadFromPersistence(bool createdFromScratch);
        PersistentSyncDomainSnapshot GetCurrentSnapshot();
        PersistentSyncDomainApplyResult ApplyClientIntent(ClientStructure client, PersistentSyncIntentMsgData data);
        PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, int numBytes, string reason);
    }
}
