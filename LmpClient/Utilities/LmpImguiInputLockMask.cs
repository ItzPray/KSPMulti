using UnityEngine;

namespace LmpClient.Utilities
{
    /// <summary>
    /// Control mask for <see cref="InputLockManager"/> while the mouse is over an LMP IMGUI window.
    /// Uses <see cref="ControlTypes.All"/> minus pause and app-launcher so KSC / facility UIs and cameras
    /// do not receive wheel, drag, or clicks behind LMP windows (unlike <see cref="ControlTypes.ALLBUTCAMERAS"/>,
    /// which leaves camera controls active and felt like input pass-through).
    /// </summary>
    public static class LmpImguiInputLockMask
    {
        public static readonly ControlTypes WindowMouseCapture =
            ControlTypes.All & ~(ControlTypes.PAUSE | ControlTypes.APPLAUNCHER_BUTTONS);
    }
}
