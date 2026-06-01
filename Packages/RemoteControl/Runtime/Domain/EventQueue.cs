// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Lilium.RemoteControl
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
        // id 採番と各クライアントキューへの enqueue を直列化し、id 順 == enqueue 順を保証する。
        // これがないと、複数スレッドが採番後に enqueue 順が逆転し、消費側カーソルが
        // 未到着の小さい id を飛び越えてしまい、その id が永久に配信されなくなる。
        private readonly object _addLock = new object();
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
            // 採番と fan-out を _addLock で直列化 (id 順 == enqueue 順を保証)。
            lock (_addLock)
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

                // 全クライアントのキューに追加。ConcurrentDictionary は列挙中の変更に対して
                // 安全なスナップショット列挙を提供するため、Keys.ToList() のコピーは不要。
                foreach (var kvp in _clientQueues)
                {
                    if (kvp.Key != excludeClient)
                    {
                        kvp.Value.AddEvent(eventItem);
                    }
                }
            }
        }

        /// <summary>
        /// 特定クライアント向けイベントを追加
        /// </summary>
        public void AddEventToClient(string clientId, object eventData, string eventType = null)
        {
            // AddEvent と同じ _addLock で採番〜enqueue を直列化する。
            lock (_addLock)
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
        }

        private void AddEventToClient(string clientId, EventItem eventItem)
        {
            var clientQueue = _clientQueues.GetOrAdd(clientId, id => new ClientEventQueue(id, _maxEventsPerClient));
            clientQueue.AddEvent(eventItem);
        }

        /// <summary>
        /// イベントを取得（Long Polling対応）。
        /// 新着イベントを呼び出し側が用意した <paramref name="buffer"/> に書き込み、件数を返す。
        /// 新着が無い場合は buffer に一切触れず 0 を返す（GCAlloc 回避）。
        /// </summary>
        /// <returns>buffer に書き込んだイベント数。タイムアウト/キャンセル時は 0。</returns>
        public async Task<int> GetEventsAsync(string clientId, long lastEventId, List<EventItem> buffer, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            UpdateClientActivity(clientId);

            var clientQueue = _clientQueues.GetOrAdd(clientId, id => new ClientEventQueue(id, _maxEventsPerClient));

            // 外部の CancellationToken と内部のタイムアウトを組み合わせる
            var actualTimeout = timeout ?? TimeSpan.FromSeconds(30);
            using var timeoutCts = new CancellationTokenSource(actualTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // 即時 drain → 無ければ long-poll 待機、をまとめて担う。
            // タイムアウト/キャンセルは内部で吸収され 0 が返るため try-catch は不要。
            return await clientQueue.WaitAndDrainAsync(lastEventId, buffer, linkedCts.Token);
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
        /// クライアントが有効期限内にアクティブだったか（O(1) 判定、Alloc 無し）。
        /// </summary>
        private bool _IsClientActive(string clientId)
        {
            if (!_clientActivity.TryGetValue(clientId, out var lastActivity))
            {
                return false;
            }
            return lastActivity > DateTime.UtcNow.Subtract(_clientTimeout);
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

            var deliveredCount = 0;
            foreach (var clientId in targetClients)
            {
                if (_IsClientActive(clientId))
                {
                    AddEventToClient(clientId, targetedMessage);
                    deliveredCount++;
                }
            }

            return Task.FromResult(deliveredCount);
        }

        /// <summary>
        /// 特定クライアントにメッセージを送信
        /// </summary>
        public Task<bool> SendToClientAsync(string clientId, object message)
        {
            if (!_IsClientActive(clientId))
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
        /// <param name="message">本文</param>
        /// <param name="notificationType">"info" / "warning" / "error"</param>
        /// <param name="data">付随データ (任意)</param>
        /// <param name="title">タイトル (任意)</param>
        /// <param name="icon">Material Symbols 名 (任意)</param>
        public Task<int> BroadcastSystemNotificationAsync(string message, string notificationType = "info", object data = null, string title = null, string icon = null)
        {
            var systemNotification = new SystemNotification
            {
                Type = "system_notification",
                NotificationType = notificationType,
                Title = title,
                Message = message,
                Icon = icon,
                Data = data,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = Guid.NewGuid().ToString()
            };

            // eventType を指定することで StreamHandler に unified format
            // ({type:"data", data:{...}}) へラップさせる。eventType=null だと
            // 既に unified format とみなされ生のオブジェクトが流れてしまう。
            AddEvent(systemNotification, eventType: "system_notification");

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
            foreach (var kvp in _clientQueues)
            {
                RemoveClient(kvp.Key);
            }
        }
    }

    /// <summary>
    /// クライアント別のイベントキュー。
    /// 固定長リングバッファ + lock + TaskCompletionSource で実装し、
    /// アイドル時のポーリングで GCAlloc が発生しないよう設計している。
    /// イベントの Id は <see cref="EventQueue"/> の Interlocked.Increment で単調増加するため、
    /// リング内は Id 昇順を保ち、afterEventId 以降の開始位置を二分探索できる。
    ///
    /// スレッド安全性: 全ての可変状態 (_ring/_count/_head/_maxId/_waiter) は単一の
    /// _gate ロック下でのみ読み書きする。AddEvent は複数スレッドから、drain 系は単一の
    /// SSE 接続タスクからの呼び出しを想定する (1 ClientEventQueue = 1 消費者)。
    /// </summary>
    public class ClientEventQueue
    {
        private readonly string _clientId;
        private readonly int _capacity;
        private readonly EventItem[] _ring;     // ctor で1回だけ確保。以後再確保しない
        private readonly object _gate = new object();

        private int _count;                     // 保持件数 (<= _capacity)
        private int _head;                       // 最古要素の物理 index
        private long _maxId;                     // 保持中の最大 Id。空なら 0

        // long-poll 用。新着で起床、null = 待機者なし。
        private TaskCompletionSource<bool> _waiter;

        public string ClientId => _clientId;
        public int EventCount { get { lock (_gate) { return _count; } } }

        public ClientEventQueue(string clientId, int maxEvents = 1000)
        {
            _clientId = clientId;
            _capacity = maxEvents < 1 ? 1 : maxEvents;
            _ring = new EventItem[_capacity];
        }

        /// <summary>
        /// イベントを追加する。呼び出し側は enqueue 順 == Id 順 (Id 単調増加) を守ること。
        /// この契約は <see cref="EventQueue"/> が _addLock で採番〜fan-out を直列化して担保する。
        /// 契約が守られない (採番より後に小さい Id が enqueue される) と、消費側カーソルが
        /// その Id を飛び越えて永久に未配信になる。末尾バブルは微小な乱れへの保険に過ぎない。
        /// </summary>
        public void AddEvent(EventItem eventItem)
        {
            TaskCompletionSource<bool> toSignal;
            lock (_gate)
            {
                _PushBack(eventItem);

                // 待機者がいれば取り出してロック外で起床させる (継続がロック内で走るのを防ぐ)
                toSignal = _waiter;
                _waiter = null;
            }
            toSignal?.TrySetResult(true);
        }

        /// <summary>
        /// リング末尾へ追加。Id 単調増加の通常ケースは O(1)。満杯時は最古を O(1) で eviction。
        /// 採番/enqueue 逆転による稀な順序乱れは末尾からの隣接スワップ (バブル) で吸収する。
        /// </summary>
        private void _PushBack(EventItem item)
        {
            if (_count < _capacity)
            {
                _ring[(_head + _count) % _capacity] = item;
                _count++;
            }
            else
            {
                // 満杯: 最古 (_head) を上書きし head を進める。空いた _head 物理スロットが新しい末尾になる。
                _ring[_head] = item;
                _head = (_head + 1) % _capacity;
            }

            // 末尾から左へバブルして Id 昇順を維持。通常 (item.Id が最大) は即 break で O(1)。
            int i = _count - 1;
            while (i > 0)
            {
                int cur = (_head + i) % _capacity;
                int prev = (_head + i - 1) % _capacity;
                if (_ring[prev].Id <= _ring[cur].Id) break;
                var tmp = _ring[prev];
                _ring[prev] = _ring[cur];
                _ring[cur] = tmp;
                i--;
            }

            _maxId = _ring[(_head + _count - 1) % _capacity].Id;
        }

        /// <summary>
        /// afterEventId より大きい Id のイベントを buffer に書き込み件数を返す。
        /// 新着が無い場合は buffer に一切触れず 0 を返す (空時 0 alloc)。
        /// </summary>
        public int DrainInto(long afterEventId, List<EventItem> buffer)
        {
            lock (_gate)
            {
                if (_count == 0 || afterEventId >= _maxId)
                {
                    return 0;
                }

                // afterEventId < Id となる最小の論理 index を二分探索
                int lo = 0;
                int hi = _count;
                while (lo < hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (_ring[(_head + mid) % _capacity].Id > afterEventId)
                        hi = mid;
                    else
                        lo = mid + 1;
                }

                int start = lo;
                int n = _count - start;

                buffer.Clear(); // 内部配列は再利用。容量内なら再確保なし
                for (int i = 0; i < n; i++)
                {
                    buffer.Add(_ring[(_head + start + i) % _capacity]);
                }
                return n;
            }
        }

        /// <summary>
        /// 即時 drain → 無ければ新着 or キャンセルまで待機 → 再 drain。
        /// キャンセル (タイムアウト含む) 時は buffer に触れず 0 を返す。
        /// </summary>
        public async Task<int> WaitAndDrainAsync(long afterEventId, List<EventItem> buffer, CancellationToken cancellationToken)
        {
            while (true)
            {
                int n = DrainInto(afterEventId, buffer);
                if (n > 0)
                {
                    return n;
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    return 0;
                }

                // 「新着の再チェック」と「待機者登録」を同一ロック内で行い、
                // drain が空を返した直後に AddEvent が来てもロストウェイクアップしない。
                TaskCompletionSource<bool> waiter;
                lock (_gate)
                {
                    if (_count > 0 && afterEventId < _maxId)
                    {
                        continue; // 登録直前に新着 → ループ先頭で drain
                    }
                    // キャンセルで完了したままの古い TCS を再利用すると await が即返りビジースピンする。
                    // null か完了済みなら新規生成する (AddEvent は signal 時に _waiter を null にする)。
                    if (_waiter == null || _waiter.Task.IsCompleted)
                    {
                        _waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    waiter = _waiter;
                }

                // キャンセルされたら待機 Task を完了させてループに戻す (例外を投げない)。
                using (cancellationToken.Register(static s => ((TaskCompletionSource<bool>)s).TrySetResult(false), waiter))
                {
                    await waiter.Task;
                }
            }
        }

        public bool HasMoreEvents(long afterEventId)
        {
            lock (_gate)
            {
                return _count > 0 && afterEventId < _maxId;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                Array.Clear(_ring, 0, _capacity);
                _count = 0;
                _head = 0;
                _maxId = 0;
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
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("notificationType")] public string NotificationType { get; set; } // info, warning, error
        [JsonProperty("title")] public string Title { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("icon")] public string Icon { get; set; } // Material Symbols 名 (例: "license", "check_circle")。null/空ならクライアント側でタイプ既定
        [JsonProperty("data")] public object Data { get; set; }
        [JsonProperty("timestamp")] public long Timestamp { get; set; }
        [JsonProperty("messageId")] public string MessageId { get; set; }
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
