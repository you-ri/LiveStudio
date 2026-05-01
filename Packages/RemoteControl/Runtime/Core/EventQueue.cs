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
    /// クライアント別イベントキューシステム
    /// Long Polling対応の効率的なイベント配信機能を提供
    /// 複数のサーバーインスタンスで独立して使用可能
    /// </summary>
    public class EventQueue
    {
        private readonly ConcurrentDictionary<string, ClientEventQueue> _clientQueues;
        private readonly ConcurrentDictionary<string, DateTime> _clientActivity;
        private readonly object _lockObject = new object();
        private long _nextEventId = 1;
        private readonly int _maxEventsPerClient = 1000;
        private readonly TimeSpan _clientTimeout = TimeSpan.FromMinutes(10);
        private CancellationTokenSource _cleanupCts;
        
        public EventQueue()
        {
            _clientQueues = new ConcurrentDictionary<string, ClientEventQueue>();
            _clientActivity = new ConcurrentDictionary<string, DateTime>();
            
            // 定期的な古いクライアントのクリーンアップ
            StartCleanupTask();
        }

        /// <summary>
        /// イベントを追加（全クライアント向け）
        /// </summary>
        public void AddEvent(object eventData, string eventType = null, string excludeClient = null)
        {
            var eventId = Interlocked.Increment(ref _nextEventId);
            var eventItem = new EventItem
            {
                Id = eventId,
                Data = eventData,
                Timestamp = DateTimeOffset.UtcNow,
                Type = eventData.GetType().Name,
                EventType = eventType ?? "data"
            };

            // 全クライアントのキューに追加
            foreach (var clientId in _clientQueues.Keys.ToList())
            {
                if (clientId != excludeClient)
                {
                    AddEventToClient(clientId, eventItem);
                }
            }
            
        }
        
        /// <summary>
        /// 特定クライアント向けイベントを追加
        /// </summary>
        public void AddEventToClient(string clientId, object eventData, string eventType = null)
        {
            var eventId = Interlocked.Increment(ref _nextEventId);
            var eventItem = new EventItem
            {
                Id = eventId,
                Data = eventData,
                Timestamp = DateTimeOffset.UtcNow,
                Type = eventData.GetType().Name,
                EventType = eventType ?? "data"
            };
            
            AddEventToClient(clientId, eventItem);
        }
        
        private void AddEventToClient(string clientId, EventItem eventItem)
        {
            var clientQueue = _clientQueues.GetOrAdd(clientId, id => new ClientEventQueue(id, _maxEventsPerClient));
            clientQueue.AddEvent(eventItem);
            
            //Debug.Log($"[RemoteControl] EventQueue: Added event {eventItem.Id} to client {clientId}");
        }
        
        /// <summary>
        /// イベントを取得（Long Polling対応）
        /// </summary>
        public async Task<List<EventItem>> GetEventsAsync(string clientId, long lastEventId = 0, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            UpdateClientActivity(clientId);

            var clientQueue = _clientQueues.GetOrAdd(clientId, id => new ClientEventQueue(id, _maxEventsPerClient));

            // 即座に利用可能なイベントがあるかチェック
            var immediateEvents = clientQueue.GetEvents(lastEventId);
            if (immediateEvents.Any())
            {
                return immediateEvents;
            }

            // Long Polling: 新しいイベントを待機
            var actualTimeout = timeout ?? TimeSpan.FromSeconds(30);

            // 外部の CancellationToken と内部のタイムアウトを組み合わせる
            using var timeoutCts = new CancellationTokenSource(actualTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                var events = await clientQueue.WaitForEventsAsync(lastEventId, linkedCts.Token);
                return events;
            }
            catch (OperationCanceledException)
            {
                // タイムアウトまたは明示的なキャンセル時は空のリストを返す
                return new List<EventItem>();
            }
        }
        
        /// <summary>
        /// 更多イベントが利用可能かチェック
        /// </summary>
        public bool HasMoreEvents(string clientId, long lastEventId)
        {
            if (!_clientQueues.TryGetValue(clientId, out var clientQueue))
            {
                return false;
            }
            
            return clientQueue.HasMoreEvents(lastEventId);
        }
        
        /// <summary>
        /// クライアントのイベントをクリア
        /// </summary>
        public void ClearEvents(string clientId)
        {
            if (_clientQueues.TryGetValue(clientId, out var clientQueue))
            {
                clientQueue.Clear();
                Debug.Log($"[RemoteControl] EventQueue: Cleared events for client {clientId}");
            }
        }
        
        /// <summary>
        /// クライアントアクティビティを更新
        /// </summary>
        public void UpdateClientActivity(string clientId)
        {
            _clientActivity.AddOrUpdate(clientId, DateTime.UtcNow, (key, oldValue) => DateTime.UtcNow);
            
            // クライアントキューも作成/更新
            _clientQueues.GetOrAdd(clientId, id => new ClientEventQueue(id, _maxEventsPerClient));
        }
        
        /// <summary>
        /// 接続中のクライアント数を取得
        /// </summary>
        public int GetConnectedClientCount()
        {
            return _clientActivity.Count;
        }
        
        /// <summary>
        /// アクティブなクライアントIDリストを取得
        /// </summary>
        public List<string> GetActiveClientIds()
        {
            var cutoff = DateTime.UtcNow.Subtract(_clientTimeout);
            return _clientActivity
                .Where(kvp => kvp.Value > cutoff)
                .Select(kvp => kvp.Key)
                .ToList();
        }
        
        /// <summary>
        /// クライアントを削除
        /// </summary>
        public void RemoveClient(string clientId)
        {
            _clientQueues.TryRemove(clientId, out _);
            _clientActivity.TryRemove(clientId, out _);
        }
        
        /// <summary>
        /// サーバー統計情報を取得
        /// </summary>
        public EventQueueStats GetStats()
        {
            var activeClients = GetActiveClientIds();
            var totalEvents = _clientQueues.Values.Sum(q => q.EventCount);
            
            return new EventQueueStats
            {
                ActiveClientCount = activeClients.Count,
                TotalClientCount = _clientQueues.Count,
                TotalEvents = totalEvents,
                NextEventId = _nextEventId,
                ActiveClients = activeClients
            };
        }
        
        /// <summary>
        /// 全クライアントにメッセージをブロードキャスト
        /// </summary>
        public Task<int> BroadcastAsync(object message, string eventType = null, string excludeClient = null)
        {
            AddEvent(message, eventType, excludeClient);

            var deliveredCount = GetConnectedClientCount();
            if (!string.IsNullOrEmpty(excludeClient))
            {
                deliveredCount = Math.Max(0, deliveredCount - 1);
            }

            return Task.FromResult(deliveredCount);
        }
        
        /// <summary>
        /// 特定クライアントのリストにメッセージを送信
        /// </summary>
        public Task<int> SendToClientsAsync(object message, string[] targetClients)
        {
            var targetedMessage = new TargetedMessage
            {
                Type = "targeted",
                Payload = message,
                TargetClients = targetClients,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = Guid.NewGuid().ToString()
            };

            var activeClients = GetActiveClientIds();
            var validTargets = targetClients.Where(client => activeClients.Contains(client)).ToArray();

            foreach (var clientId in validTargets)
            {
                AddEventToClient(clientId, targetedMessage);
            }

            return Task.FromResult(validTargets.Length);
        }
        
        /// <summary>
        /// 特定クライアントにメッセージを送信
        /// </summary>
        public Task<bool> SendToClientAsync(string clientId, object message)
        {
            var activeClients = GetActiveClientIds();
            if (!activeClients.Contains(clientId))
            {
                Debug.LogWarning($"[RemoteControl] EventQueue: Client {clientId} is not active");
                return Task.FromResult(false);
            }

            var directMessage = new DirectMessage
            {
                Type = "direct",
                Payload = message,
                TargetClient = clientId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = Guid.NewGuid().ToString()
            };

            AddEventToClient(clientId, directMessage);
            return Task.FromResult(true);
        }
        
        /// <summary>
        /// システム通知をブロードキャスト
        /// </summary>
        public Task<int> BroadcastSystemNotificationAsync(string message, string notificationType = "info", object data = null)
        {
            var systemNotification = new SystemNotification
            {
                Type = "system_notification",
                NotificationType = notificationType,
                Message = message,
                Data = data,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = Guid.NewGuid().ToString()
            };

            AddEvent(systemNotification);

            var deliveredCount = GetConnectedClientCount();
            return Task.FromResult(deliveredCount);
        }
        
        /// <summary>
        /// クライアント接続通知をブロードキャスト
        /// </summary>
        public async Task NotifyClientConnected(string clientId)
        {
            var connectionEvent = new ClientConnectionEvent
            {
                Type = "client_connected",
                ClientId = clientId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                EventId = Guid.NewGuid().ToString()
            };
            
            await BroadcastAsync(connectionEvent, "client_connected", clientId);
        }
        
        /// <summary>
        /// クライアント切断通知をブロードキャスト
        /// </summary>
        public async Task NotifyClientDisconnected(string clientId)
        {
            var disconnectionEvent = new ClientConnectionEvent
            {
                Type = "client_disconnected",
                ClientId = clientId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                EventId = Guid.NewGuid().ToString()
            };
            
            await BroadcastAsync(disconnectionEvent, "client_disconnected");
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
                        await Task.Delay(TimeSpan.FromMinutes(5), token); // 5分間隔でクリーンアップ
                        CleanupInactiveClients();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[RemoteControl] EventQueue cleanup error: {ex.Message}");
                    }
                }
            });
        }
        
        private void CleanupInactiveClients()
        {
            var cutoff = DateTime.UtcNow.Subtract(_clientTimeout);
            var inactiveClients = _clientActivity
                .Where(kvp => kvp.Value < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var clientId in inactiveClients)
            {
                RemoveClient(clientId);
            }

            if (inactiveClients.Any())
            {
                Debug.Log($"[RemoteControl] EventQueue: Cleaned up {inactiveClients.Count} inactive clients");
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

            // 全クライアントのキューをクリア
            foreach (var clientId in _clientQueues.Keys.ToList())
            {
                RemoveClient(clientId);
            }
        }
    }
    
    /// <summary>
    /// クライアント別のイベントキュー
    /// </summary>
    public class ClientEventQueue
    {
        private readonly string _clientId;
        private readonly int _maxEvents;
        private readonly ConcurrentQueue<EventItem> _events;
        private readonly SemaphoreSlim _eventSemaphore;
        private readonly object _lockObject = new object();
        
        public string ClientId => _clientId;
        public int EventCount => _events.Count;
        
        public ClientEventQueue(string clientId, int maxEvents = 1000)
        {
            _clientId = clientId;
            _maxEvents = maxEvents;
            _events = new ConcurrentQueue<EventItem>();
            _eventSemaphore = new SemaphoreSlim(0);
        }
        
        public void AddEvent(EventItem eventItem)
        {
            _events.Enqueue(eventItem);
            
            // 最大イベント数を超えた場合、古いイベントを削除
            while (_events.Count > _maxEvents)
            {
                _events.TryDequeue(out _);
            }
            
            _eventSemaphore.Release(); // 待機中のGetEventsAsyncに通知
        }
        
        public List<EventItem> GetEvents(long afterEventId = 0)
        {
            var result = new List<EventItem>();
            var tempList = new List<EventItem>();
            
            // 全イベントを一時的にリストに移す
            while (_events.TryDequeue(out var item))
            {
                tempList.Add(item);
            }
            
            // 条件に合うイベントを選択し、残りを戻す
            foreach (var item in tempList)
            {
                if (item.Id > afterEventId)
                {
                    result.Add(item);
                }
                _events.Enqueue(item); // 全イベントをキューに戻す
            }
            
            return result.OrderBy(e => e.Id).ToList();
        }
        
        public async Task<List<EventItem>> WaitForEventsAsync(long afterEventId, CancellationToken cancellationToken)
        {
            // 既存のイベントをチェック
            var existingEvents = GetEvents(afterEventId);
            if (existingEvents.Any())
            {
                return existingEvents;
            }
            
            // 新しいイベントを待機
            await _eventSemaphore.WaitAsync(cancellationToken);
            
            // セマフォが解除された後、イベントを再取得
            return GetEvents(afterEventId);
        }
        
        public bool HasMoreEvents(long afterEventId)
        {
            return _events.Any(e => e.Id > afterEventId);
        }
        
        public void Clear()
        {
            while (_events.TryDequeue(out _)) { }
            
            // セマフォもリセット
            while (_eventSemaphore.CurrentCount > 0)
            {
                _eventSemaphore.Wait(0);
            }
        }
    }
    
    /// <summary>
    /// イベントアイテム
    /// </summary>
    public class EventItem
    {
        public long Id { get; set; }
        public object Data { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string Type { get; set; }
        public string EventType { get; set; }
    }
    
    /// <summary>
    /// EventQueue統計情報
    /// </summary>
    public class EventQueueStats
    {
        public int ActiveClientCount { get; set; }
        public int TotalClientCount { get; set; }
        public long TotalEvents { get; set; }
        public long NextEventId { get; set; }
        public List<string> ActiveClients { get; set; }
    }
    
    /// <summary>
    /// ターゲット指定メッセージ
    /// </summary>
    public class TargetedMessage
    {
        public string Type { get; set; }
        public object Payload { get; set; }
        public string[] TargetClients { get; set; }
        public long Timestamp { get; set; }
        public string MessageId { get; set; }
    }
    
    /// <summary>
    /// ダイレクトメッセージ
    /// </summary>
    public class DirectMessage
    {
        public string Type { get; set; }
        public object Payload { get; set; }
        public string TargetClient { get; set; }
        public long Timestamp { get; set; }
        public string MessageId { get; set; }
    }
    
    /// <summary>
    /// システム通知
    /// </summary>
    public class SystemNotification
    {
        public string Type { get; set; }
        public string NotificationType { get; set; } // info, warning, error
        public string Message { get; set; }
        public object Data { get; set; }
        public long Timestamp { get; set; }
        public string MessageId { get; set; }
    }
    
    /// <summary>
    /// クライアント接続イベント
    /// </summary>
    public class ClientConnectionEvent
    {
        public string Type { get; set; }
        public string ClientId { get; set; }
        public long Timestamp { get; set; }
        public string EventId { get; set; }
    }
}