using UnityEngine;
using System;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Severity of a user-facing notice. Mirrors what a remote app shows as a toast style.
    /// </summary>
    public enum NoticeLevel
    {
        Information,
        Success,
        Warning,
        Error,
    }

    public static class RemoteControlService
    {
        public static event Action onResetData;

        /// <summary>
        /// Raised when something worth telling the operator about happened. The transport layer
        /// subscribes and delivers it (today: a toast in the connected remote apps).
        ///
        /// This exists so the data layer can report without depending on the server: persistence
        /// runs with no server started, and nothing here should fail because nobody is listening.
        /// Arguments are message, level, title (optional) and icon name (optional).
        /// </summary>
        public static event Action<string, NoticeLevel, string, string> onNotice;

        public static void ResetData()
        {
            onResetData?.Invoke();
        }

        /// <summary>
        /// Report a user-facing notice. Dropped when no transport is listening.
        /// </summary>
        public static void Notify(string message, NoticeLevel level = NoticeLevel.Information, string title = null, string icon = null)
        {
            onNotice?.Invoke(message, level, title, icon);
        }
    }
}
