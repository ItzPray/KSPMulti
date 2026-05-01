namespace LmpCommon.PersistentSync.Payloads.Contracts
{
    public enum ContractIntentPayloadKind : byte
    {
        AcceptContract = 0,
        DeclineContract = 1,
        CancelContract = 2,
        RequestOfferGeneration = 3,
        OfferObserved = 4,
        ParameterProgressObserved = 5,
        ContractCompletedObserved = 6,
        ContractFailedObserved = 7,
        FullReconcile = 8
    }

    public enum ContractSnapshotPayloadMode : byte
    {
        Delta = 0,
        FullReplace = 1
    }

    public enum ContractSnapshotPlacement : byte
    {
        Current = 0,
        Active = 1,
        Finished = 2
    }
}
