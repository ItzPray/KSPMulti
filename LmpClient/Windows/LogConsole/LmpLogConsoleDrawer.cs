using LmpClient;
using UnityEngine;

namespace LmpClient.Windows.LogConsole
{
    public partial class LmpLogConsoleWindow
    {
        private static bool _pendingLogAutoscroll;

        protected override void DrawWindowContent(int windowId)
        {
            GUI.DragWindow(MoveRect);
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
                _logRichDisplayBuffer = LunaLog.GetRecentLogRichTextTailForDisplay(2500);
                if (string.IsNullOrEmpty(_logRichDisplayBuffer))
                {
                    _logRichDisplayBuffer = "<color=#8899AA>(no LMP log lines yet)</color>";
                }

                _logRichDisplayRevision = historyRev;
            }

            var lineCount = LunaLog.GetLogHistoryLineCount();
            if (_autoScrollToEnd && Event.current.type == EventType.Layout && lineCount != _lastRenderedLineCount)
            {
                _lastRenderedLineCount = lineCount;
                _pendingLogAutoscroll = true;
            }

            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandHeight(true));
            var viewport = GUILayoutUtility.GetRect(8f, 140f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            var innerWidth = Mathf.Max(32f, viewport.width - 8f);
            var contentHeight = _logBodyStyle.CalcHeight(new GUIContent(_logRichDisplayBuffer), innerWidth);
            contentHeight = Mathf.Max(contentHeight, viewport.height);

            var evt = Event.current;
            if (evt.type == EventType.ScrollWheel && viewport.Contains(evt.mousePosition))
            {
                _logScroll.y += evt.delta.y * 42f;
                evt.Use();
            }

            var maxScroll = Mathf.Max(0f, contentHeight - viewport.height);
            if (_pendingLogAutoscroll && evt.type == EventType.Layout)
            {
                _logScroll.y = maxScroll;
                _pendingLogAutoscroll = false;
            }

            _logScroll.y = Mathf.Clamp(_logScroll.y, 0f, maxScroll);

            GUI.BeginGroup(viewport);
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.11f, 0.12f, 0.14f, 1f);
            // IMGUI ScrollView steals mouse drags for scrolling, which breaks TextArea selection. Manual clip +
            // GUI.TextArea keeps wheel scrolling (above) while allowing click-drag selection and Ctrl+C.
            var textRect = new Rect(4f, -_logScroll.y, innerWidth, contentHeight);
            _logRichDisplayBuffer = GUI.TextArea(textRect, _logRichDisplayBuffer, _logBodyStyle);
            GUI.backgroundColor = prevBg;
            GUI.EndGroup();

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
