using LmpCommon.Enums;
using LmpCommon.PersistentSync;

namespace Server.System.PersistentSync
{
    public class PersistentSyncDomainSnapshot
    {
        public string DomainId;
        public long Revision;
        public PersistentAuthorityPolicy AuthorityPolicy;
        public byte[] Payload = new byte[0];
        public int NumBytes;
    }
}
