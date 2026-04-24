using LmpClient.Events.Base;
using LmpCommon.Locks;

// ReSharper disable All
#pragma warning disable IDE1006

namespace LmpClient.Events
{
    public class LockEvent : LmpBaseEvent
    {
        public static EventData<LockDefinition> onLockAcquire;
        public static EventData<LockDefinition> onLockRelease;

        /// <summary>
        /// Fired once after the server lock list snapshot replaces the local store (no per-lock acquire events).
        /// </summary>
        public static EventVoid onLockListApplied;
    }
}
