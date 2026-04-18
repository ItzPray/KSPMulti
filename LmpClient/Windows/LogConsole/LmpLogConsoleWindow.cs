using LmpClient.Base;
using LmpClient.Systems.SettingsSys;
using LmpClient.Utilities;
using LmpClient.Windows.Status;
using LmpCommon.Enums;
using UnityEngine;

namespace LmpClient.Windows.LogConsole
{
    /// <summary>
    /// Dedicated LunaLog viewer docked under the main LMP status panel. Not the Unity Alt+F12 console.
    /// </summary>
    public partial class LmpLogConsoleWindow : Window<LmpLogConsoleWindow>
    {
        private const int WindowControlId = 6708;

        private static bool _showConsole;
        private static Vector2 _logScroll;
        private static bool _autoScrollToEnd = true;
        private bool _initialDockLayoutDone;
        private static int _lastRenderedLineCount;

        /// <summary>
        /// Cached rich log body; refreshed when <see cref="LunaLog.GetHistoryRevision"/> changes so drag-select
        /// works without rebuilding the string every IMGUI frame.
        /// </summary>
        private static string _logRichDisplayBuffer = string.Empty;

        private static int _logRichDisplayRevision = -1;

        private static GUIStyle _logBodyStyle;
        private static GUIStyle _toolbarLabelStyle;

        public override bool Display
        {
            get => base.Display && _showConsole && SettingsSystem.CurrentSettings.DisclaimerAccepted &&
                   MainSystem.ToolbarShowGui && MainSystem.NetworkState >= ClientState.Running &&
                   HighLogic.LoadedScene >= GameScenes.SPACECENTER;
            set => base.Display = _showConsole = value;
        }

        public override void OnDisplay()
        {
            base.OnDisplay();
            _initialDockLayoutDone = false;
            _lastRenderedLineCount = 0;
            _logRichDisplayRevision = -1;
            _pendingLogAutoscroll = true;
        }

        protected override bool Resizable => true;

        public override void Update()
        {
            base.Update();
            if (!Display)
            {
                return;
            }

            DockBelowStatusPanel();
        }

        private void DockBelowStatusPanel()
        {
            var status = StatusWindow.Singleton;
            if (status == null || !status.Display)
            {
                return;
            }

            var sr = status.GetStatusPanelScreenRect();
            const float gap = 8f;
            const float minWidth = 440f;

            if (!_initialDockLayoutDone)
            {
                if (WindowRect.height < 200f)
                {
                    WindowRect.height = 320f;
                }

                _initialDockLayoutDone = true;
            }

            WindowRect.width = Mathf.Max(minWidth, sr.width);
            WindowRect.x = sr.xMax - WindowRect.width;
            WindowRect.y = sr.yMax + gap;
        }

        protected override void DrawGui()
        {
            GUI.skin = DefaultSkin;
            WindowRect = FixWindowPos(GUILayout.Window(WindowControlId + MainSystem.WindowOffset, WindowRect, DrawContent,
                "LMP Console", LayoutOptions));
        }

        public override void SetStyles()
        {
            WindowRect = new Rect(Screen.width * 0.5f - 260f, Screen.height * 0.42f, 520f, 320f);
            MoveRect = new Rect(0, 0, int.MaxValue, TitleHeight);

            LayoutOptions = new GUILayoutOption[]
            {
                GUILayout.MinWidth(360f),
                GUILayout.MaxWidth(8000f),
                GUILayout.MinHeight(200f),
                GUILayout.MaxHeight(8000f)
            };

            _logBodyStyle = new GUIStyle(GUI.skin.textArea)
            {
                fontSize = 12,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(10, 10, 10, 10)
            };
            _logBodyStyle.normal.textColor = new Color(0.93f, 0.94f, 0.96f);
            _logBodyStyle.hover.textColor = _logBodyStyle.normal.textColor;
            _logBodyStyle.active.textColor = _logBodyStyle.normal.textColor;
            _logBodyStyle.focused.textColor = _logBodyStyle.normal.textColor;
            _logBodyStyle.richText = true;

            _toolbarLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft
            };
            _toolbarLabelStyle.normal.textColor = new Color(0.72f, 0.74f, 0.78f);
        }

        public override void RemoveWindowLock()
        {
            if (IsWindowLocked)
            {
                IsWindowLocked = false;
                InputLockManager.RemoveControlLock("LMP_LogConsoleLock");
            }
        }

        public override void CheckWindowLock()
        {
            if (Display)
            {
                if (MainSystem.NetworkState < ClientState.Running || HighLogic.LoadedSceneIsFlight)
                {
                    RemoveWindowLock();
                    return;
                }

                var mousePos = Input.mousePosition;
                mousePos.y = Screen.height - mousePos.y;

                var shouldLock = GetConsoleDockHitRect().Contains(mousePos);

                if (shouldLock && !IsWindowLocked)
                {
                    InputLockManager.SetControlLock(LmpImguiInputLockMask.WindowMouseCapture, "LMP_LogConsoleLock");
                    IsWindowLocked = true;
                }
                else if (!shouldLock && IsWindowLocked)
                {
                    RemoveWindowLock();
                }
            }
            else if (IsWindowLocked)
            {
                RemoveWindowLock();
            }
        }

        /// <summary>
        /// Screen-space bounds of the log console window for hit-testing (same coordinate space as <see cref="StatusWindow.GetStatusPanelScreenRect"/>).
        /// </summary>
        public Rect GetConsoleScreenRect()
        {
            return WindowRect;
        }

        /// <summary>
        /// Hit test for input lock: console plus the main LMP status strip above it (covers the dock gap).
        /// </summary>
        private Rect GetConsoleDockHitRect()
        {
            var r = WindowRect;
            var st = StatusWindow.Singleton;
            if (st != null && st.Display)
            {
                var s = st.GetStatusPanelScreenRect();
                r.xMin = Mathf.Min(r.xMin, s.xMin);
                r.yMin = Mathf.Min(r.yMin, s.yMin);
                r.xMax = Mathf.Max(r.xMax, s.xMax);
                r.yMax = Mathf.Max(r.yMax, s.yMax);
            }

            return r;
        }
    }
}
