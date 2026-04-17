using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using LmpCommon.Message.Data.PersistentSync;

namespace Server.System.PersistentSync
{
    public interface IPersistentSyncServerDomain
    {
        PersistentSyncDomainId DomainId { get; }
        PersistentAuthorityPolicy AuthorityPolicy { get; }
        void LoadFromPersistence(bool createdFromScratch);
        PersistentSyncDomainSnapshot GetCurrentSnapshot();
        PersistentSyncDomainApplyResult ApplyClientIntent(PersistentSyncIntentMsgData data);
        PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, int numBytes, string reason);
    }
}
