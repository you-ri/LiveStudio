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
    /// ポーリング最適化システム
    /// レート制限、キャッシュ、Long Polling最適化機能を提供
    /// </summary>
    public class PollingOptimizer
    {
        private static PollingOptimizer _instance;
        public static PollingOptimizer Instance => _instance ?? (_instance = new PollingOptimizer());
        
        private readonly ConcurrentDictionary<string, ClientPollingState> _clientStates;
        private readonly ConcurrentDictionary<string, RateLimitInfo> _rateLimits;
        private readonly object _lockObject = new object();
        private CancellationTokenSource _cleanupCts;
        
        // 設定可能パラメータ
        public int MaxRequestsPerMinute { get; set; } = 120; // 1分間のリクエスト上限
        public int MaxLongPollDuration { get; set; } = 60; // Long Pollingの最大待機時間（秒）
        public int MinPollingInterval { get; set; } = 1; // 最小ポーリング間隔（秒）
        public bool EnableRateLimit { get; set; } = true;
        public bool EnableConditionalRequests { get; set; } = true;
        
        public PollingOptimizer()
        {
            _clientStates = new ConcurrentDictionary<string, ClientPollingState>();
            _rateLimits = new ConcurrentDictionary<string, RateLimitInfo>();
            
            // 定期的なクリーンアップタスク
            StartCleanupTask();
        }
        
        /// <summary>
        /// レート制限チェック
        /// </summary>
        public RateLimitResult CheckRateLimit(string clientId)
        {
            if (!EnableRateLimit)
            {
                return new RateLimitResult { IsAllowed = true };
            }
            
            var now = DateTime.UtcNow;
            var rateLimitInfo = _rateLimits.AddOrUpdate(clientId,
                // 新規作成
                id => new RateLimitInfo
                {
                    ClientId = id,
                    WindowStart = now,
                    RequestCount = 1,
                    LastRequest = now
                },
                // 更新
                (id, existing) =>
                {
                    // 時間窓をリセット
                    if ((now - existing.WindowStart).TotalMinutes >= 1)
                    {
                        existing.WindowStart = now;
                        existing.RequestCount = 1;
                    }
                    else
                    {
                        existing.RequestCount++;
                    }
                    
                    existing.LastRequest = now;
                    return existing;
                });
            
            var isAllowed = rateLimitInfo.RequestCount <= MaxRequestsPerMinute;
            var remainingRequests = Math.Max(0, MaxRequestsPerMinute - rateLimitInfo.RequestCount);
            var resetTime = rateLimitInfo.WindowStart.AddMinutes(1);
            
            if (!isAllowed)
            {
                Debug.LogWarning($"[RemoteControl] PollingOptimizer: Rate limit exceeded for client {clientId} ({rateLimitInfo.RequestCount}/{MaxRequestsPerMinute})");
            }
            
            return new RateLimitResult
            {
                IsAllowed = isAllowed,
                RemainingRequests = remainingRequests,
                ResetTime = resetTime,
                RetryAfter = isAllowed ? TimeSpan.Zero : (resetTime - now)
            };
        }
        
        /// <summary>
        /// ポーリング状態を更新
        /// </summary>
        public void UpdatePollingState(string clientId, long lastEventId, TimeSpan requestedTimeout)
        {
            var now = DateTime.UtcNow;
            var adjustedTimeout = TimeSpan.FromSeconds(Math.Min(requestedTimeout.TotalSeconds, MaxLongPollDuration));
            
            var pollingState = _clientStates.AddOrUpdate(clientId,
                // 新規作成
                id => new ClientPollingState
                {
                    ClientId = id,
                    LastEventId = lastEventId,
                    LastPollTime = now,
                    RequestedTimeout = adjustedTimeout,
                    PollCount = 1,
                    AverageInterval = TimeSpan.Zero
                },
                // 更新
                (id, existing) =>
                {
                    var interval = now - existing.LastPollTime;
                    
                    // 平均間隔を計算
                    if (existing.AverageInterval == TimeSpan.Zero)
                    {
                        existing.AverageInterval = interval;
                    }
                    else
                    {
                        // 指数移動平均で更新
                        var alpha = 0.3; // 平滑化係数
                        existing.AverageInterval = TimeSpan.FromMilliseconds(
                            alpha * interval.TotalMilliseconds + (1 - alpha) * existing.AverageInterval.TotalMilliseconds);
                    }
                    
                    existing.LastEventId = lastEventId;
                    existing.LastPollTime = now;
                    existing.RequestedTimeout = adjustedTimeout;
                    existing.PollCount++;
                    
                    return existing;
                });
            
            Debug.Log($"[RemoteControl] PollingOptimizer: Updated polling state for client {clientId} (poll #{pollingState.PollCount}, avg interval: {pollingState.AverageInterval.TotalSeconds:F1}s)");
        }
        
        /// <summary>
        /// 推奨ポーリング間隔を取得
        /// </summary>
        public TimeSpan GetRecommendedPollingInterval(string clientId)
        {
            if (!_clientStates.TryGetValue(clientId, out var state))
            {
                return TimeSpan.FromSeconds(MinPollingInterval);
            }
            
            // クライアントの平均間隔に基づいて推奨間隔を計算
            var baseInterval = state.AverageInterval.TotalSeconds;
            var recommendedInterval = Math.Max(MinPollingInterval, Math.Min(baseInterval * 1.5, 30));
            
            return TimeSpan.FromSeconds(recommendedInterval);
        }
        
        /// <summary>
        /// 条件付きリクエストのETAGを生成
        /// </summary>
        public string GenerateETag(object data, long eventId)
        {
            if (!EnableConditionalRequests)
            {
                return null;
            }
            
            try
            {
                var content = $"{eventId}_{data?.GetHashCode() ?? 0}";
                var hash = content.GetHashCode();
                return $"\"{hash:X}\"";
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// ETAGが一致するかチェック
        /// </summary>
        public bool IsETagMatch(string ifNoneMatch, string currentETag)
        {
            if (!EnableConditionalRequests || string.IsNullOrEmpty(ifNoneMatch) || string.IsNullOrEmpty(currentETag))
            {
                return false;
            }
            
            return ifNoneMatch.Equals(currentETag, StringComparison.OrdinalIgnoreCase);
        }
        
        /// <summary>
        /// ポーリング統計情報を取得
        /// </summary>
        public PollingOptimizerStats GetStats()
        {
            var activeClients = _clientStates.Count;
            var totalPolls = 0L;
            var averageInterval = TimeSpan.Zero;
            var rateLimitViolations = 0;
            
            foreach (var state in _clientStates.Values)
            {
                totalPolls += state.PollCount;
                averageInterval = averageInterval.Add(state.AverageInterval);
            }
            
            foreach (var rateLimit in _rateLimits.Values)
            {
                if (rateLimit.RequestCount > MaxRequestsPerMinute)
                {
                    rateLimitViolations++;
                }
            }
            
            if (activeClients > 0)
            {
                averageInterval = TimeSpan.FromMilliseconds(averageInterval.TotalMilliseconds / activeClients);
            }
            
            return new PollingOptimizerStats
            {
                ActiveClients = activeClients,
                TotalPolls = totalPolls,
                AveragePollingInterval = averageInterval,
                RateLimitViolations = rateLimitViolations,
                MaxRequestsPerMinute = MaxRequestsPerMinute,
                EnabledFeatures = new[]
                {
                    EnableRateLimit ? "Rate Limiting" : null,
                    EnableConditionalRequests ? "Conditional Requests" : null
                }.Where(f => f != null).ToArray(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }
        
        /// <summary>
        /// クライアント状態をクリア
        /// </summary>
        public void ClearClientState(string clientId)
        {
            _clientStates.TryRemove(clientId, out _);
            _rateLimits.TryRemove(clientId, out _);
            Debug.Log($"[RemoteControl] PollingOptimizer: Cleared state for client {clientId}");
        }
        
        /// <summary>
        /// クリーンアップタスクを停止し、リソースを解放
        /// </summary>
        public void Shutdown()
        {
            _cleanupCts?.Cancel();
            _cleanupCts?.Dispose();
            _cleanupCts = null;
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
                        await Task.Delay(TimeSpan.FromMinutes(10), token); // 10分間隔でクリーンアップ
                        CleanupOldStates();
                    }
                    catch (OperationCanceledException)
                    {
                        // シャットダウンによるキャンセルは正常終了
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[RemoteControl] PollingOptimizer cleanup error: {ex.Message}");
                    }
                }
            });
        }
        
        private void CleanupOldStates()
        {
            var cutoff = DateTime.UtcNow.Subtract(TimeSpan.FromHours(1)); // 1時間以上古い状態を削除
            var clientsToRemove = new List<string>();
            
            // 古いポーリング状態を検索
            foreach (var kvp in _clientStates)
            {
                if (kvp.Value.LastPollTime < cutoff)
                {
                    clientsToRemove.Add(kvp.Key);
                }
            }
            
            // レート制限の古い状態を検索
            foreach (var kvp in _rateLimits)
            {
                if (kvp.Value.LastRequest < cutoff)
                {
                    clientsToRemove.Add(kvp.Key);
                }
            }
            
            // 重複削除
            clientsToRemove = clientsToRemove.Distinct().ToList();
            
            // 削除実行
            foreach (var clientId in clientsToRemove)
            {
                ClearClientState(clientId);
            }
            
            if (clientsToRemove.Any())
            {
                Debug.Log($"[RemoteControl] PollingOptimizer: Cleaned up {clientsToRemove.Count} old client states");
            }
        }
    }
    
    /// <summary>
    /// レート制限結果
    /// </summary>
    public class RateLimitResult
    {
        public bool IsAllowed { get; set; }
        public int RemainingRequests { get; set; }
        public DateTime ResetTime { get; set; }
        public TimeSpan RetryAfter { get; set; }
    }
    
    /// <summary>
    /// レート制限情報
    /// </summary>
    public class RateLimitInfo
    {
        public string ClientId { get; set; }
        public DateTime WindowStart { get; set; }
        public int RequestCount { get; set; }
        public DateTime LastRequest { get; set; }
    }
    
    /// <summary>
    /// クライアントポーリング状態
    /// </summary>
    public class ClientPollingState
    {
        public string ClientId { get; set; }
        public long LastEventId { get; set; }
        public DateTime LastPollTime { get; set; }
        public TimeSpan RequestedTimeout { get; set; }
        public TimeSpan AverageInterval { get; set; }
        public long PollCount { get; set; }
    }
    
    /// <summary>
    /// PollingOptimizer統計情報
    /// </summary>
    public class PollingOptimizerStats
    {
        public int ActiveClients { get; set; }
        public long TotalPolls { get; set; }
        public TimeSpan AveragePollingInterval { get; set; }
        public int RateLimitViolations { get; set; }
        public int MaxRequestsPerMinute { get; set; }
        public string[] EnabledFeatures { get; set; }
        public long Timestamp { get; set; }
    }
}