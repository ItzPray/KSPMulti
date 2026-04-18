using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LmpClient
{
    /// <summary>
    /// This class implements a thread safe logger that writes into the Unity log.
    /// </summary>
    public class LunaLog
    {
        #region Helper classes

        private enum LogType
        {
            Error,
            Warning,
            Info
        }

        private class LogEntry
        {
            public LogType Type { get; }
            public string Text { get; }

            public LogEntry(LogType type, string text)
            {
                Type = type;
                Text = text;
            }
        }

        #endregion

        #region Fields & properties

        private static readonly ConcurrentQueue<LogEntry> Queue = new ConcurrentQueue<LogEntry>();

        private static readonly object LogHistoryLock = new object();
        private static volatile int _historyRevision;
        private const int MaxLogHistoryLines = 2000;

        private struct HistoryLine
        {
            public LogType Type;
            public string Text;
        }

        private static readonly List<HistoryLine> LogHistory = new List<HistoryLine>(512);

        #endregion

        #region Logging methods

        public static void LogWarning(string message)
        {
            var msg = message.Contains("[LMP]") ? message : $"[LMP]: {message}";
            if (MainSystem.IsUnityThread)
            {
                AppendLogHistory(LogType.Warning, msg);
                Debug.LogWarning(msg);
            }
            else
            {
                Queue.Enqueue(new LogEntry(LogType.Warning, msg));
            }
        }

        public static void LogError(string message)
        {
            var msg = message.Contains("[LMP]") ? message : $"[LMP]: {message}";
            if (MainSystem.IsUnityThread)
            {
                AppendLogHistory(LogType.Error, msg);
                Debug.LogError(msg);
            }
            else
            {
                Queue.Enqueue(new LogEntry(LogType.Error, msg));
            }
        }

        public static void Log(string message)
        {
            var msg = message.StartsWith("[LMP]") ? message : $"[LMP]: {message}";
            if (MainSystem.IsUnityThread)
            {
                AppendLogHistory(LogType.Info, msg);
                Debug.Log(msg);
            }
            else
            {
                Queue.Enqueue(new LogEntry(LogType.Info, msg));
            }
        }

        #endregion

        #region Log history (LMP console)

        private static string ColorHexForHistory(LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                    return "#FF6A6A";
                case LogType.Warning:
                    return "#F0B429";
                default:
                    return "#DCE6F0";
            }
        }

        /// <summary>
        /// Prevents Unity rich-text markup from breaking on arbitrary log text.
        /// </summary>
        private static string EscapeForUnityRichText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Replace("<", "\uFF1C").Replace(">", "\uFF1E");
        }

        /// <summary>
        /// Plain text (no color tags) for clipboard export.
        /// </summary>
        public static string GetRecentLogTextForClipboard()
        {
            lock (LogHistoryLock)
            {
                if (LogHistory.Count == 0)
                {
                    return string.Empty;
                }

                var sb = new StringBuilder(LogHistory.Count * 64);
                for (var i = 0; i < LogHistory.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append('\n');
                    }

                    sb.Append(LogHistory[i].Text);
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// Last lines as Unity IMGUI rich text (per-line color by severity).
        /// </summary>
        public static string GetRecentLogRichTextTailForDisplay(int maxLines)
        {
            if (maxLines <= 0)
            {
                return string.Empty;
            }

            lock (LogHistoryLock)
            {
                if (LogHistory.Count == 0)
                {
                    return string.Empty;
                }

                var start = LogHistory.Count <= maxLines ? 0 : LogHistory.Count - maxLines;
                var sb = new StringBuilder(maxLines * 80);
                for (var i = start; i < LogHistory.Count; i++)
                {
                    if (i > start)
                    {
                        sb.Append('\n');
                    }

                    var line = LogHistory[i];
                    var hex = ColorHexForHistory(line.Type);
                    sb.Append("<color=").Append(hex).Append('>')
                        .Append(EscapeForUnityRichText(line.Text))
                        .Append("</color>");
                }

                return sb.ToString();
            }
        }

        private static void AppendLogHistory(LogType type, string line)
        {
            lock (LogHistoryLock)
            {
                LogHistory.Add(new HistoryLine { Type = type, Text = line });
                var overflow = LogHistory.Count - MaxLogHistoryLines;
                if (overflow > 0)
                {
                    LogHistory.RemoveRange(0, overflow);
                }

                unchecked
                {
                    _historyRevision++;
                }
            }
        }

        public static int GetLogHistoryLineCount()
        {
            lock (LogHistoryLock)
            {
                return LogHistory.Count;
            }
        }

        /// <summary>
        /// Bumps whenever log lines are added or cleared; the console uses this to refresh its buffer without
        /// resetting text selection on every GUI frame.
        /// </summary>
        public static int GetHistoryRevision()
        {
            return _historyRevision;
        }

        public static void ClearLogHistory()
        {
            lock (LogHistoryLock)
            {
                LogHistory.Clear();
                unchecked
                {
                    _historyRevision++;
                }
            }
        }

        #endregion

        #region Process

        /// <summary>
        /// Call this method FROM the unity thread so it reads all the queued log messages and prints them
        /// </summary>
        public static void ProcessLogMessages()
        {
            if (!MainSystem.IsUnityThread)
            {
                throw new Exception("Cannot call ProcessLogMessages from another thread that is not the Unity thread");
            }

            while (Queue.TryDequeue(out var entry))
            {
                AppendLogHistory(entry.Type, entry.Text);
                switch (entry.Type)
                {
                    case LogType.Error:
                        Debug.LogError(entry.Text);
                        break;
                    case LogType.Warning:
                        Debug.LogWarning(entry.Text);
                        break;
                    case LogType.Info:
                        Debug.Log(entry.Text);
                        break;
                }
            }
        }

        #endregion
    }
}
