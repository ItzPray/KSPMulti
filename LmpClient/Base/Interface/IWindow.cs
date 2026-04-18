using UnityEngine;

namespace LmpClient.Base.Interface
{
    public interface IWindow
    {
        string WindowName { get; }

        void Update();
        void OnGui();

        void RemoveWindowLock();
        void CheckWindowLock();

        void SetStyles();

        /// <summary>
        /// When any visible LMP IMGUI window covers facility uGUI (R&amp;D tree, etc.), returns its screen-space
        /// union rectangle in the same coordinates as <see cref="GUI"/> / <c>WindowRect</c> (Y down from top).
        /// </summary>
        bool TryGetImguiOverlayRect(out Rect rect);
    }
}
