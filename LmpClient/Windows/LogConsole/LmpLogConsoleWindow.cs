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

        /// <summary>IMGUI focus name for the log <see cref="GUI.TextArea"/> (stable selection vs. LunaLog buffer sync).</summary>
        private const string LogConsoleBodyFocusName = "LmpLogConsoleBody";

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

        /// <summary>Mutable backing string for <see cref="GUI.TextArea"/> so IMGUI can keep cursor/selection state.</summary>
        private static string _logTextAreaState = string.Empty;

        /// <summary>Cached plain slice passed to IMGUI (see <see cref="GetPlainTextForImguiTextArea"/>).</summary>
        private static string _logPlainForImguiCache;

        private static int _logPlainForImguiCacheRevision = int.MinValue;

        private static GUIStyle _logBodyStyle;
        private static GUIStyle _toolbarLabelStyle;

        /// <summary>1×1 fill drawn behind the log TextArea (style backgrounds stay null so selection is not covered).</summary>
        private static Texture2D _logConsoleFieldBackground;

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
            _logScroll = Vector2.zero;
            _logTextAreaState = string.Empty;
            _logPlainForImguiCache = null;
            _logPlainForImguiCacheRevision = int.MinValue;
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
            // TextArea selection tint uses GUI.skin.settings; keep visible colors for the whole window so Repaint-time
            // selection is not restored to KSP's near-invisible defaults mid-frame.
            var skinSettings = GUI.skin.settings;
            var prevSelectionColor = skinSettings.selectionColor;
            var prevCursorColor = skinSettings.cursorColor;
            skinSettings.selectionColor = new Color(0.20f, 0.45f, 0.92f, 0.85f);
            skinSettings.cursorColor = new Color(0.96f, 0.97f, 0.99f, 1f);
            try
            {
                WindowRect = FixWindowPos(GUILayout.Window(WindowControlId + MainSystem.WindowOffset, WindowRect, DrawContent,
                    "LMP Console", LayoutOptions));
            }
            finally
            {
                skinSettings.selectionColor = prevSelectionColor;
                skinSettings.cursorColor = prevCursorColor;
            }
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
                padding = new RectOffset(10, 10, 10, 10),
                border = new RectOffset(0, 0, 0, 0)
            };
            _logBodyStyle.normal.textColor = new Color(0.93f, 0.94f, 0.96f);
            _logBodyStyle.hover.textColor = _logBodyStyle.normal.textColor;
            _logBodyStyle.active.textColor = _logBodyStyle.normal.textColor;
            _logBodyStyle.focused.textColor = _logBodyStyle.normal.textColor;
            // Rich text on TextArea prevents reliable drag-to-select; body uses LunaLog.GetRecentLogPlainTextTailForDisplay.
            _logBodyStyle.richText = false;

            if (_logConsoleFieldBackground == null)
            {
                _logConsoleFieldBackground = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave
                };
                _logConsoleFieldBackground.SetPixel(0, 0, new Color(0.11f, 0.12f, 0.14f, 1f));
                _logConsoleFieldBackground.Apply(false, true);
            }

            // Do not assign opaque backgrounds on the TextArea style: inside a scroll view Unity can repaint those
            // fills over the selection band so the highlight looks vertically "wrong". The log drawer paints
            // _logConsoleFieldBackground behind the text instead.
            _logBodyStyle.normal.background = null;
            _logBodyStyle.hover.background = null;
            _logBodyStyle.active.background = null;
            _logBodyStyle.focused.background = null;
            _logBodyStyle.onNormal.background = null;
            _logBodyStyle.onHover.background = null;
            _logBodyStyle.onActive.background = null;
            _logBodyStyle.onFocused.background = null;

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
