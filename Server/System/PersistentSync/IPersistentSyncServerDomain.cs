using LmpCommon.Enums;
using LmpCommon.PersistentSync;
using LmpCommon.Message.Data.PersistentSync;
using Server.Client;

namespace Server.System.PersistentSync
{
    public interface IPersistentSyncServerDomain
    {
        string DomainId { get; }
        PersistentAuthorityPolicy AuthorityPolicy { get; }
        void LoadFromPersistence(bool createdFromScratch);
        PersistentSyncDomainSnapshot GetCurrentSnapshot();
        PersistentSyncDomainApplyResult ApplyClientIntent(ClientStructure client, PersistentSyncIntentMsgData data);
        PersistentSyncDomainApplyResult ApplyServerMutation(byte[] payload, string reason);

        /// <summary>
        /// Authoritative per-intent authorization gate. The registry calls this once per client intent before
        /// dispatching to <see cref="ApplyClientIntent"/>; a false return rejects the intent without ever
        /// touching canonical state.
        ///
        /// Every concrete domain MUST declare its gate explicitly. The sanctioned templates
        /// (<see cref="SyncDomainStoreBase{TCanonical}"/> and <see cref="ProjectionSyncDomain{TOwner}"/>)
        /// declare this method <c>abstract</c> so authority is never silently inherited from the base class;
        /// a new domain author cannot forget to choose a gate.
        ///
        /// See AGENTS.md &quot;Scenario Sync Domain Contract&quot; rule: &quot;Authority is declared once and enforced at
        /// the registry gate. No domain may do its own LockQuery check inside ReduceIntent.&quot;
        /// </summary>
        bool AuthorizeIntent(ClientStructure client, byte[] payload);
    }
}
