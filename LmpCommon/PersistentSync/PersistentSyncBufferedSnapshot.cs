using LmpCommon.Enums;

namespace LmpCommon.PersistentSync
{
    public class PersistentSyncBufferedSnapshot
    {
        public PersistentSyncDomainId DomainId;
        public long Revision;
        public PersistentAuthorityPolicy AuthorityPolicy;
        public int NumBytes;
        public byte[] Payload = new byte[0];
    }
}
