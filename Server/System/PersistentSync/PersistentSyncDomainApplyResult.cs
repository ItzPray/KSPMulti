namespace Server.System.PersistentSync
{
    public class PersistentSyncDomainApplyResult
    {
        public bool Accepted;
        public bool Changed;
        public bool ReplyToOriginClient;
        public PersistentSyncDomainSnapshot Snapshot;
    }
}
