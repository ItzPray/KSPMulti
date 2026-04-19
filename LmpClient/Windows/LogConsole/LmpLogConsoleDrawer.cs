using LmpClient;
using System;
using UnityEngine;

namespace LmpClient.Windows.LogConsole
{
    public partial class LmpLogConsoleWindow
    {
        private static bool _pendingLogAutoscroll;

        /// <summary>
        /// Unity IMGUI text uses 16-bit mesh indices (~16383 UTF-16 code units). Longer strings break caret, hit
        /// tests, and selection painting. We show a tail slice in the TextArea; use Copy all for the full history.
        /// </summary>
        private const int ImguiTextAreaCharacterSafetyLimit = 15800;

        private static string BuildImguiSafePlainText(string full)
        {
            if (string.IsNullOrEmpty(full) || full.Length <= ImguiTextAreaCharacterSafetyLimit)
            {
                return full;
            }

            const string banner = "[... older log omitted (Unity ~16k char limit); use Copy all ...]\n";
            var budget = ImguiTextAreaCharacterSafetyLimit - banner.Length;
            if (budget < 512)
            {
                budget = 512;
            }

            return banner + full.Substring(full.Length - budget);
        }

        private static string GetPlainTextForImguiCached(string fullTailPlain, int historyRevision)
        {
            if (historyRevision == _logPlainForImguiCacheRevision && _logPlainForImguiCache != null)
            {
                return _logPlainForImguiCache;
            }

            _logPlainForImguiCacheRevision = historyRevision;
            _logPlainForImguiCache = BuildImguiSafePlainText(fullTailPlain);
            return _logPlainForImguiCache;
        }

        /// <summary>
        /// Log body must stay read-only. IMGUI TextArea still accepts typing unless we consume mutation events
        /// (including Paste/Cut from ExecuteCommand).
        /// </summary>
        private static void BlockLogConsoleBodyTextInput()
        {
            if (GUI.GetNameOfFocusedControl() != LogConsoleBodyFocusName)
            {
                return;
            }

            var e = Event.current;
            if (e.type == EventType.ValidateCommand || e.type == EventType.ExecuteCommand)
            {
                if (string.Equals(e.commandName, "Paste", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.commandName, "Cut", StringComparison.OrdinalIgnoreCase))
                {
                    e.Use();
                }

                return;
            }

            if (e.type != EventType.KeyDown)
            {
                return;
            }

            var mod = e.control || e.command;
            if (mod && e.keyCode == KeyCode.V)
            {
                e.Use();
                return;
            }

            if (mod && e.keyCode == KeyCode.X)
            {
                e.Use();
                return;
            }

            if (mod && (e.keyCode == KeyCode.C || e.keyCode == KeyCode.A))
            {
                return;
            }

            if (e.keyCode == KeyCode.Backspace || e.keyCode == KeyCode.Delete ||
                e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Tab)
            {
                e.Use();
                return;
            }

            switch (e.keyCode)
            {
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                case KeyCode.Home:
                case KeyCode.End:
                case KeyCode.PageUp:
                case KeyCode.PageDown:
                    return;
            }

            if (e.character != 0 && !mod && e.keyCode != KeyCode.Escape)
            {
                if (!char.IsControl(e.character))
                {
                    e.Use();
                }
            }
        }

        protected override void DrawWindowContent(int windowId)
        {
            // Do not call GUI.DragWindow here: the GUILayout.Window title bar already drags the window, and
            // DragWindow in the client area competes with TextArea mouse drags (focus ring / no text selection).
            DrawToolbar();
            DrawLogBody();
            ConsumeScrollWheelWhenOverWindow();
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal(GUILayout.Height(32f));
            if (GUILayout.Button("Copy all", GUILayout.Width(92f), GUILayout.Height(26f)))
            {
                GUIUtility.systemCopyBuffer = LunaLog.GetRecentLogTextForClipboard();
            }

            if (GUILayout.Button("Clear", GUILayout.Width(76f), GUILayout.Height(26f)))
            {
                LunaLog.ClearLogHistory();
                _lastRenderedLineCount = 0;
                _logScroll = Vector2.zero;
            }

            GUILayout.Space(10f);
            _autoScrollToEnd = GUILayout.Toggle(_autoScrollToEnd, " Auto-scroll", GUILayout.Height(26f));
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{LunaLog.GetLogHistoryLineCount()} lines", _toolbarLabelStyle, GUILayout.Height(26f));
            GUILayout.EndHorizontal();
        }

        private void DrawLogBody()
        {
            var historyRev = LunaLog.GetHistoryRevision();
            if (historyRev != _logRichDisplayRevision)
            {
                _logRichDisplayBuffer = LunaLog.GetRecentLogPlainTextTailForDisplay(2500);
                if (string.IsNullOrEmpty(_logRichDisplayBuffer))
                {
                    _logRichDisplayBuffer = "(no LMP log lines yet)";
                }

                _logRichDisplayRevision = historyRev;
                // New buffer length vs. old scroll max would clamp scroll.y past real content → blank view.
                _logScroll = Vector2.zero;
                _logPlainForImguiCacheRevision = int.MinValue;
            }

            var textForImgui = GetPlainTextForImguiCached(_logRichDisplayBuffer, historyRev);

            var lineCount = LunaLog.GetLogHistoryLineCount();
            if (_autoScrollToEnd && Event.current.type == EventType.Layout && lineCount != _lastRenderedLineCount)
            {
                _lastRenderedLineCount = lineCount;
                _pendingLogAutoscroll = true;
            }

            // No GUI.skin.box wrapper here: extra layout controls can steal drags from the TextEditor.
            GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            var viewport = GUILayoutUtility.GetRect(8f, 140f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            const float padX = 4f;
            var scrollViewport = new Rect(viewport.x + padX, viewport.y, Mathf.Max(32f, viewport.width - padX * 2f),
                viewport.height);

            var vsb = GUI.skin.verticalScrollbar;
            var content = new GUIContent(textForImgui);
            float clientWidth = scrollViewport.width;
            var measuredTextHeight = _logBodyStyle.CalcHeight(content, clientWidth);
            const float textEditorVerticalSlop = 8f;
            var contentHeight = Mathf.Max(measuredTextHeight + textEditorVerticalSlop, scrollViewport.height);

            if (contentHeight > scrollViewport.height + 0.5f)
            {
                var vBarWidth = vsb.fixedWidth + vsb.margin.left;
                if (vBarWidth < 2f)
                {
                    vBarWidth = 15f;
                }

                clientWidth = Mathf.Max(32f, scrollViewport.width - vBarWidth);
                measuredTextHeight = _logBodyStyle.CalcHeight(content, clientWidth);
                contentHeight = Mathf.Max(measuredTextHeight + textEditorVerticalSlop, scrollViewport.height);
            }

            var innerWidth = clientWidth;
            var evt = Event.current;
            var maxScroll = Mathf.Max(0f, contentHeight - scrollViewport.height);
            if (_pendingLogAutoscroll && evt.type == EventType.Layout)
            {
                _logScroll.y = maxScroll;
                _pendingLogAutoscroll = false;
            }

            _logScroll.y = Mathf.Clamp(_logScroll.y, 0f, maxScroll);

            var viewRect = new Rect(0f, 0f, innerWidth, contentHeight);
            _logScroll = GUI.BeginScrollView(scrollViewport, _logScroll, viewRect, false, false);

            var prevBgMul = GUI.backgroundColor;
            var prevGuiColor = GUI.color;
            GUI.backgroundColor = Color.white;
            GUI.color = Color.white;

            // Opaque per-state TextArea backgrounds repaint after the selection quads in some Unity/KSP IMGUI paths,
            // which looks like a broken highlight (correct near the top, wrong lower in the viewport). Draw the fill
            // here and keep _logBodyStyle backgrounds null so selection stays visible.
            if (_logConsoleFieldBackground != null)
            {
                GUI.DrawTexture(viewRect, _logConsoleFieldBackground, ScaleMode.StretchToFill, true);
            }

            // Stock TextArea + BeginScrollView keeps selection aligned with wrapped glyphs. Sync the mutable backing
            // string from Luna when this control is not focused so we do not reset the TextEditor each frame.
            if (GUI.GetNameOfFocusedControl() != LogConsoleBodyFocusName)
            {
                if (_logTextAreaState == null || !string.Equals(_logTextAreaState, textForImgui))
                {
                    _logTextAreaState = textForImgui;
                }
            }

            GUI.SetNextControlName(LogConsoleBodyFocusName);
            BlockLogConsoleBodyTextInput();
            _logTextAreaState = GUI.TextArea(viewRect, _logTextAreaState, _logBodyStyle);

            GUI.color = prevGuiColor;
            GUI.backgroundColor = prevBgMul;

            GUI.EndScrollView();

            GUILayout.EndVertical();
        }

        /// <summary>
        /// Stops scroll-wheel from reaching facility UIs (e.g. R&amp;D tree) under this window.
        /// </summary>
        private void ConsumeScrollWheelWhenOverWindow()
        {
            var e = Event.current;
            if (e.type != EventType.ScrollWheel)
            {
                return;
            }

            if (GetConsoleDockHitRect().Contains(e.mousePosition))
            {
                e.Use();
            }
        }
    }
}
