using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// REST APIクライアント接続管理システム。
    /// 「今この瞬間リモートアプリが繋がっているか」を、直近に接続確認 (/api/status) を
    /// 投げてきた時刻で判定する。常時接続が無いので切断イベントは飛んでこず、
    /// 沈黙が続いたことをもって離脱とみなすしかない。
    /// 複数のサーバーインスタンスで独立して使用可能。
    /// </summary>
    public class RestApiConnectionManager
    {
        /// <summary>
        /// 在席とみなす無沈黙時間。接続確認の間隔 (1 秒) の 5 倍。
        /// 短すぎると一時的な遅延で在席中のリモートを見失い、確認ダイアログが
        /// 「誰も見ていない」と判断して即キャンセルしてしまう。長すぎると逆に、
        /// とっくに閉じたリモートの返事を待って操作が止まる。
        /// これは在席判定専用で、受信箱の保持期間 (<see cref="EventQueue"/>) とは別物。
        /// 受信箱は一時的に離れたクライアントが戻ってきたときのために長く持つ。
        /// </summary>
        public static readonly TimeSpan kClientTimeout = TimeSpan.FromSeconds(5);

        private readonly ConcurrentDictionary<string, RestApiClient> _clients;
        private readonly object _lockObject = new object();
        private CancellationTokenSource _cleanupCts;
        
        public event Action<RestApiClient> OnClientConnected;
        public event Action<RestApiClient> OnClientDisconnected;

        public int ConnectionCount
        {
            get
            {
                // Plain loop instead of LINQ Count(predicate): this is polled every frame by the
                // editor toolbar and must not allocate a delegate per call.
                int count = 0;
                foreach (var kvp in _clients)
                {
                    if (kvp.Value.IsActive) count++;
                }
                return count;
            }
        }
        
        public RestApiConnectionManager()
        {
            _clients = new ConcurrentDictionary<string, RestApiClient>();
            
            // 定期的な非アクティブクライアントのクリーンアップ
            StartCleanupTask();
        }
        
        /// <summary>
        /// クライアントを登録または更新
        /// </summary>
        public RestApiClient RegisterClient(string clientId, string userAgent = null, string ipAddress = null)
        {
            var isNewClient = false;
            
            var client = _clients.AddOrUpdate(clientId,
                // 新規作成
                id =>
                {
                    isNewClient = true;
                    return new RestApiClient(id, userAgent, ipAddress);
                },
                // 更新
                (id, existingClient) =>
                {
                    existingClient.UpdateActivity();
                    return existingClient;
                });
            
            if (isNewClient)
            {
                OnClientConnected?.Invoke(client);
            }
            
            return client;
        }
        
        /// <summary>
        /// クライアントを削除
        /// </summary>
        public void RemoveClient(string clientId)
        {
            if (_clients.TryRemove(clientId, out var client))
            {
                OnClientDisconnected?.Invoke(client);
            }
        }
        
        /// <summary>
        /// 全クライアントを削除（サーバー起動時のクリーンアップ用）
        /// </summary>
        public void RemoveAllClients()
        {
            lock (_lockObject)
            {
                var allClients = _clients.Values.ToList();
                _clients.Clear();
                
                foreach (var client in allClients)
                {
                    OnClientDisconnected?.Invoke(client);
                }
                
                if (allClients.Count > 0)
                {
                    Debug.Log($"[RemoteControl] RestApiConnectionManager: Removed all {allClients.Count} clients");
                }
            }
        }
        
        private void StartCleanupTask()
        {
            _cleanupCts = new CancellationTokenSource();
            var token = _cleanupCts.Token;
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // 在席の窓 (5 秒) より十分長く、かつ離脱を長く引きずらない間隔。
                        await Task.Delay(TimeSpan.FromSeconds(30), token);
                        CleanupInactiveClients();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[RemoteControl] RestApiConnectionManager cleanup error: {ex.Message}");
                    }
                }
            });
        }
        
        private void CleanupInactiveClients()
        {
            var cutoff = DateTime.UtcNow.Subtract(kClientTimeout);
            var inactiveClients = _clients.Values
                .Where(client => client.LastActivity < cutoff)
                .ToList();

            foreach (var client in inactiveClients)
            {
                RemoveClient(client.ClientId);
            }

            if (inactiveClients.Any())
            {
                Debug.Log($"[RemoteControl] RestApiConnectionManager: Cleaned up {inactiveClients.Count} inactive clients");
            }
        }

        /// <summary>
        /// シャットダウン処理: バックグラウンドタスクを停止し、全クライアントを削除
        /// </summary>
        public void Shutdown()
        {
            _cleanupCts?.Cancel();
            _cleanupCts?.Dispose();
            _cleanupCts = null;

            RemoveAllClients();
        }
    }
    
    /// <summary>
    /// REST APIクライアント情報
    /// </summary>
    public class RestApiClient
    {
        public string ClientId { get; }
        public string UserAgent { get; }
        public string IpAddress { get; }
        public DateTime ConnectedAt { get; }
        public DateTime LastActivity { get; private set; }

        public bool IsActive => (DateTime.UtcNow - LastActivity) < RestApiConnectionManager.kClientTimeout;

        public RestApiClient(string clientId, string userAgent = null, string ipAddress = null)
        {
            ClientId = clientId;
            UserAgent = userAgent ?? "Unknown";
            IpAddress = ipAddress ?? "Unknown";
            ConnectedAt = DateTime.UtcNow;
            LastActivity = DateTime.UtcNow;
        }

        public void UpdateActivity()
        {
            LastActivity = DateTime.UtcNow;
        }
    }
}