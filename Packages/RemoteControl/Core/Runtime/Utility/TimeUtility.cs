using System;

namespace Lilium.RemoteControl.Core
{
    /// <summary>
    /// スレッドセーフな時間取得ユーティリティ
    /// Unity の Time.time の代替として、メインスレッド以外でも使用可能
    /// </summary>
    public static class TimeUtility
    {
        private static readonly DateTime _startTime = DateTime.UtcNow;

        /// <summary>
        /// アプリケーション開始からの経過時間を取得（秒単位）
        /// スレッドセーフで、Unity の Time.time の代替として使用可能
        /// </summary>
        /// <returns>経過時間（秒）</returns>
        public static double GetTime()
        {
            return (DateTime.UtcNow - _startTime).TotalSeconds;
        }

        /// <summary>
        /// アプリケーション開始からの経過時間を取得（float型、秒単位）
        /// Unity の Time.time と同じ型での取得が必要な場合に使用
        /// </summary>
        /// <returns>経過時間（秒）</returns>
        public static float GetTimeAsFloat()
        {
            return (float)GetTime();
        }

        /// <summary>
        /// 現在の UTC タイムスタンプを ISO 8601 形式で取得
        /// </summary>
        /// <returns>ISO 8601 形式のタイムスタンプ文字列</returns>
        public static string GetISOTimestamp()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        }

        /// <summary>
        /// Unix エポックからの経過ミリ秒を取得
        /// </summary>
        /// <returns>Unix エポックからの経過ミリ秒</returns>
        public static long GetUnixTimeMilliseconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}