// Copyright (c) You-Ri, 2026
using UnityEngine;

using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl.Notification
{
    /// <summary>
    /// Studio 等の Unity アプリから接続中の Remote App へ通知ダイアログを表示するための静的 API。
    /// 内部的には RemoteControlServerCore.BroadcastSystemNotification を全サーバへ送る。
    /// 通知は各クライアントの受信箱に積まれ、リモート側が定期ポーリングで拾って
    /// "system_notification" として表示する。取りこぼしても次のポーリングで届く。
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
        /// データ層が上げた知らせを接続中の RemoteApp へ橋渡しする。データ層はサーバーを知らない
        /// (永続化はサーバー未起動でも動く) ので、購読はこちら側で張る。
        /// 再生中とエディタの両方で張るのは、シーン保存が非再生中にも走るため。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        private static void _InstallNoticeBridge()
        {
            // Domain Reload 無効時に二重購読しないよう、必ず外してから張る。
            RemoteControlService.onNotice -= _OnNotice;
            RemoteControlService.onNotice += _OnNotice;
        }

        private static void _OnNotice(string message, NoticeLevel level, string title, string icon)
        {
            Show(message, _FromNoticeLevel(level), title, icon);
        }

        private static Type _FromNoticeLevel(NoticeLevel level)
        {
            switch (level)
            {
                case NoticeLevel.Success: return Type.Success;
                case NoticeLevel.Warning: return Type.Warning;
                case NoticeLevel.Error: return Type.Error;
                default: return Type.Information;
            }
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
