namespace LmpCommon.Enums
{
    public enum PersistentAuthorityPolicy : byte
    {
        ServerDerived = 0,
        AnyClientIntent = 1,
        LockOwnerIntent = 2,
        DesignatedProducer = 3
    }
}
