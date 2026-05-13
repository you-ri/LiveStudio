// Copyright (c) You-Ri, 2026
using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Studio 等の Unity アプリから接続中の Remote App へ通知ダイアログを表示するための静的 API。
    /// 内部的には RemoteControlServerCore.BroadcastSystemNotification を全サーバへ送る。
    /// RemoteApp 側は SSE の "system_notification" を NotificationSystem に変換して表示する。
    /// </summary>
    public static class RemoteNotificationSystem
    {
        /// <summary>
        /// 通知の種別。RemoteApp 側のスタイル (アイコン色等) を切り替える。
        /// </summary>
        public enum Type
        {
            Information,
            Success,
            Warning,
            Error,
        }

        /// <summary>
        /// 接続中の全 RemoteApp に通知を送る。
        /// </summary>
        /// <param name="message">本文</param>
        /// <param name="type">情報 / 警告 / エラー</param>
        /// <param name="title">タイトル (省略時は本文のみ)</param>
        /// <param name="icon">Material Symbols 名 (省略時は type 既定アイコン)</param>
        public static void Show(string message, Type type = Type.Information, string title = null, string icon = null)
        {
            var typeStr = _ToWireType(type);
            foreach (var kv in RemoteControlServerManager.servers)
            {
                var server = kv.Value?.server;
                if (server == null) continue;
                _ = server.BroadcastSystemNotification(message, typeStr, data: null, title: title, icon: icon);
            }
        }

        private static string _ToWireType(Type type)
        {
            switch (type)
            {
                case Type.Success: return "success";
                case Type.Warning: return "warning";
                case Type.Error: return "error";
                default: return "info";
            }
        }
    }
}
