using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Lilium.RemoteControl.Core
{
    /// <summary>
    /// REST APIクライアント接続管理システム
    /// REST API経由の擬似接続を管理
    /// 複数のサーバーインスタンスで独立して使用可能
    /// </summary>
    public class RestApiConnectionManager
    {
        private readonly ConcurrentDictionary<string, RestApiClient> _clients;
        private readonly object _lockObject = new object();
        private readonly TimeSpan _clientTimeout = TimeSpan.FromSeconds(10);
        private CancellationTokenSource _cleanupCts;
        
        public event Action<RestApiClient> OnClientConnected;
        public event Action<RestApiClient> OnClientDisconnected;
        public event Action<RestApiClient, object> OnClientMessageReceived;
        
        public int ConnectionCount
        {
            get
            {
                lock (_lockObject)
                {
                    return _clients.Count(kvp => kvp.Value.IsActive);
                }
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
        /// クライアントのアクティビティを更新
        /// </summary>
        public void UpdateClientActivity(string clientId)
        {
            if (_clients.TryGetValue(clientId, out var client))
            {
                client.UpdateActivity();
            }
            else
            {
                // 新規クライアントとして登録
                RegisterClient(clientId);
            }
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
        
        /// <summary>
        /// クライアント情報を取得
        /// </summary>
        public RestApiClient GetClient(string clientId)
        {
            return _clients.TryGetValue(clientId, out var client) ? client : null;
        }
        
        /// <summary>
        /// アクティブなクライアント一覧を取得
        /// </summary>
        public List<RestApiClient> GetActiveClients()
        {
            lock (_lockObject)
            {
                return _clients.Values
                    .Where(client => client.IsActive)
                    .ToList();
            }
        }
        
        /// <summary>
        /// 全クライアント一覧を取得
        /// </summary>
        public List<RestApiClient> GetAllClients()
        {
            return _clients.Values.ToList();
        }
        
        /// <summary>
        /// クライアントIDのリストを取得
        /// </summary>
        public List<string> GetActiveClientIds()
        {
            return GetActiveClients().Select(client => client.ClientId).ToList();
        }
        
        /// <summary>
        /// クライアントメッセージを処理
        /// </summary>
        public void ProcessClientMessage(string clientId, object message)
        {
            var client = GetClient(clientId);
            if (client != null)
            {
                client.UpdateActivity();
                client.IncrementMessageCount();
                OnClientMessageReceived?.Invoke(client, message);
            }
        }
        
        /// <summary>
        /// 統計情報を取得
        /// </summary>
        public ConnectionManagerStats GetStats()
        {
            var activeClients = GetActiveClients();
            var totalClients = GetAllClients();
            
            return new ConnectionManagerStats
            {
                ActiveClientCount = activeClients.Count,
                TotalClientCount = totalClients.Count,
                TotalMessages = totalClients.Sum(c => c.MessageCount),
                AverageResponseTime = activeClients.Any() ? activeClients.Average(c => c.AverageResponseTime) : 0,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
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
                        await Task.Delay(TimeSpan.FromMinutes(2), token); // 2分間隔でクリーンアップ
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
            var cutoff = DateTime.UtcNow.Subtract(_clientTimeout);
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
        private long _messageCount;
        public long MessageCount => _messageCount;
        public double AverageResponseTime { get; private set; }
        
        private readonly List<double> _responseTimes = new List<double>();
        private readonly object _lockObject = new object();
        
        public bool IsActive => (DateTime.UtcNow - LastActivity).TotalSeconds < 10;
        public TimeSpan ConnectionDuration => DateTime.UtcNow - ConnectedAt;
        
        public RestApiClient(string clientId, string userAgent = null, string ipAddress = null)
        {
            ClientId = clientId;
            UserAgent = userAgent ?? "Unknown";
            IpAddress = ipAddress ?? "Unknown";
            ConnectedAt = DateTime.UtcNow;
            LastActivity = DateTime.UtcNow;
            _messageCount = 0;
            AverageResponseTime = 0;
        }
        
        public void UpdateActivity()
        {
            LastActivity = DateTime.UtcNow;
        }
        
        public void IncrementMessageCount()
        {
            Interlocked.Increment(ref _messageCount);
        }
        
        public void AddResponseTime(double responseTimeMs)
        {
            lock (_lockObject)
            {
                _responseTimes.Add(responseTimeMs);
                
                // 最新の100件のレスポンス時間のみを保持
                if (_responseTimes.Count > 100)
                {
                    _responseTimes.RemoveAt(0);
                }
                
                AverageResponseTime = _responseTimes.Average();
            }
        }
        
        public RestApiClientInfo GetInfo()
        {
            return new RestApiClientInfo
            {
                ClientId = ClientId,
                UserAgent = UserAgent,
                IpAddress = IpAddress,
                ConnectedAt = ConnectedAt,
                LastActivity = LastActivity,
                MessageCount = MessageCount,
                AverageResponseTime = AverageResponseTime,
                IsActive = IsActive,
                ConnectionDuration = ConnectionDuration
            };
        }
    }
    
    /// <summary>
    /// REST APIクライアント情報（シリアライズ用）
    /// </summary>
    public class RestApiClientInfo
    {
        public string ClientId { get; set; }
        public string UserAgent { get; set; }
        public string IpAddress { get; set; }
        public DateTime ConnectedAt { get; set; }
        public DateTime LastActivity { get; set; }
        public long MessageCount { get; set; }
        public double AverageResponseTime { get; set; }
        public bool IsActive { get; set; }
        public TimeSpan ConnectionDuration { get; set; }
    }
    
    /// <summary>
    /// ConnectionManager統計情報
    /// </summary>
    public class ConnectionManagerStats
    {
        public int ActiveClientCount { get; set; }
        public int TotalClientCount { get; set; }
        public long TotalMessages { get; set; }
        public double AverageResponseTime { get; set; }
        public long Timestamp { get; set; }
    }
}