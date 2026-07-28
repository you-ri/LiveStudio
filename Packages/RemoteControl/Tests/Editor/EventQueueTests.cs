// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// ClientEventQueue (リングバッファ + lock + TCS) と EventQueue.GetEventsAsync の挙動・
    /// アロケーション特性を検証する。アイドル時 0 alloc と long-poll 修復が主眼。
    /// </summary>
    public class EventQueueTests
    {
        private static EventItem Ev(long id)
        {
            return new EventItem
            {
                Id = id,
                Data = "payload",
                Timestamp = DateTimeOffset.MinValue,
                Type = "String",
                EventType = "data"
            };
        }

        // 非同期メソッドを Unity の SynchronizationContext を避けてスレッドプールで完了させる。
        private static T Run<T>(Func<Task<T>> body)
        {
            return Task.Run(body).GetAwaiter().GetResult();
        }

        // ---- DrainInto ----

        [Test]
        public void DrainInto_ReturnsOnlyEventsAfterId()
        {
            var q = new ClientEventQueue("c", 1000);
            for (long i = 1; i <= 5; i++) q.AddEvent(Ev(i));

            var buffer = new List<EventItem>();
            int n = q.DrainInto(2, buffer);

            Assert.AreEqual(3, n);
            Assert.AreEqual(3, buffer.Count);
            Assert.AreEqual(3, buffer[0].Id);
            Assert.AreEqual(4, buffer[1].Id);
            Assert.AreEqual(5, buffer[2].Id);
        }

        [Test]
        public void DrainInto_EmptyDoesNotTouchBuffer()
        {
            var q = new ClientEventQueue("c", 1000);
            var buffer = new List<EventItem> { Ev(99) }; // 既存内容

            int n = q.DrainInto(0, buffer);

            Assert.AreEqual(0, n);
            // 空時は buffer に一切触れない (Clear も Add もしない)
            Assert.AreEqual(1, buffer.Count);
            Assert.AreEqual(99, buffer[0].Id);
        }

        [Test]
        public void DrainInto_NoNewWhenCursorAtMax_DoesNotTouchBuffer()
        {
            var q = new ClientEventQueue("c", 1000);
            for (long i = 1; i <= 3; i++) q.AddEvent(Ev(i));

            var buffer = new List<EventItem> { Ev(99) };
            int n = q.DrainInto(3, buffer); // afterEventId == _maxId

            Assert.AreEqual(0, n);
            Assert.AreEqual(1, buffer.Count);
        }

        [Test]
        public void DrainInto_NoRepeatAfterCursorAdvance()
        {
            var q = new ClientEventQueue("c", 1000);
            for (long i = 1; i <= 5; i++) q.AddEvent(Ev(i));

            var buffer = new List<EventItem>();
            long cursor = 0;
            int n = q.DrainInto(cursor, buffer);
            Assert.AreEqual(5, n);
            cursor = buffer[n - 1].Id; // 5 まで消費

            // 再 drain。新着なしなので 0、前回分は再出現しない
            int n2 = q.DrainInto(cursor, buffer);
            Assert.AreEqual(0, n2);

            q.AddEvent(Ev(6));
            int n3 = q.DrainInto(cursor, buffer);
            Assert.AreEqual(1, n3);
            Assert.AreEqual(6, buffer[0].Id);
        }

        // ---- リング eviction ----

        [Test]
        public void RingEviction_KeepsNewestInAscendingOrder()
        {
            var q = new ClientEventQueue("c", 4);
            for (long i = 1; i <= 6; i++) q.AddEvent(Ev(i)); // 1,2 が落ちる

            Assert.AreEqual(4, q.EventCount);

            var buffer = new List<EventItem>();
            int n = q.DrainInto(0, buffer);
            Assert.AreEqual(4, n);
            Assert.AreEqual(3, buffer[0].Id);
            Assert.AreEqual(4, buffer[1].Id);
            Assert.AreEqual(5, buffer[2].Id);
            Assert.AreEqual(6, buffer[3].Id);
        }

        [Test]
        public void RingEviction_EvictedIdsNotReturnedEvenFromZero()
        {
            var q = new ClientEventQueue("c", 4);
            for (long i = 1; i <= 6; i++) q.AddEvent(Ev(i));

            var buffer = new List<EventItem>();
            q.DrainInto(0, buffer);

            foreach (var e in buffer)
                Assert.GreaterOrEqual(e.Id, 3, "落ちた Id (1,2) は返らない");
        }

        // ---- 採番/enqueue 逆転 ----

        [Test]
        public void OutOfOrderInsert_DrainsAscending()
        {
            var q = new ClientEventQueue("c", 1000);
            // Id を逆順に投入 (採番後に enqueue 順が逆転したケースを模す)
            q.AddEvent(Ev(2));
            q.AddEvent(Ev(1));
            q.AddEvent(Ev(4));
            q.AddEvent(Ev(3));

            var buffer = new List<EventItem>();
            int n = q.DrainInto(0, buffer);

            Assert.AreEqual(4, n);
            Assert.AreEqual(1, buffer[0].Id);
            Assert.AreEqual(2, buffer[1].Id);
            Assert.AreEqual(3, buffer[2].Id);
            Assert.AreEqual(4, buffer[3].Id);
        }

        // ---- HasMoreEvents / Clear ----

        [Test]
        public void HasMoreEvents_ReflectsMaxId()
        {
            var q = new ClientEventQueue("c", 1000);
            Assert.IsFalse(q.HasMoreEvents(0));
            q.AddEvent(Ev(5));
            Assert.IsTrue(q.HasMoreEvents(4));
            Assert.IsFalse(q.HasMoreEvents(5));
        }

        [Test]
        public void Clear_ResetsQueue()
        {
            var q = new ClientEventQueue("c", 1000);
            for (long i = 1; i <= 3; i++) q.AddEvent(Ev(i));
            q.Clear();

            Assert.AreEqual(0, q.EventCount);
            var buffer = new List<EventItem>();
            Assert.AreEqual(0, q.DrainInto(0, buffer));
        }

        // ---- 並行ストレス ----

        [Test]
        public void Concurrency_ConcurrentAddAndDrainIsThreadSafe()
        {
            // ClientEventQueue 単体のスレッド安全性を検証する。契約 (enqueue 順 == Id 順) を守るため
            // 採番と AddEvent を共有ロックで直列化しつつ、drain は並行して走らせる。
            var q = new ClientEventQueue("c", 100000);
            const int producers = 4;
            const int perProducer = 500;
            long idCounter = 0;
            var addGate = new object();

            var received = Run(async () =>
            {
                var consumed = new List<long>();
                var buffer = new List<EventItem>();
                int total = producers * perProducer;

                var adders = new Task[producers];
                for (int p = 0; p < producers; p++)
                {
                    adders[p] = Task.Run(() =>
                    {
                        for (int i = 0; i < perProducer; i++)
                        {
                            // 採番と enqueue を直列化 (EventQueue._addLock 相当の契約)
                            lock (addGate)
                            {
                                long id = Interlocked.Increment(ref idCounter);
                                q.AddEvent(Ev(id));
                            }
                        }
                    });
                }

                // 消費側はポーリングと同じく即時 drain を繰り返す。
                long cursor = 0;
                var deadline = Stopwatch.StartNew();
                while (consumed.Count < total && deadline.Elapsed < TimeSpan.FromSeconds(10))
                {
                    int n = q.DrainInto(cursor, buffer);
                    if (n == 0)
                    {
                        await Task.Yield();
                        continue;
                    }
                    for (int i = 0; i < n; i++) consumed.Add(buffer[i].Id);
                    cursor = buffer[n - 1].Id;
                }

                await Task.WhenAll(adders);
                return consumed;
            });

            Assert.AreEqual(producers * perProducer, received.Count, "全イベントを受信");
            for (int i = 1; i < received.Count; i++)
                Assert.Less(received[i - 1], received[i], "Id 昇順かつ重複なし");
        }

        [Test]
        public void EventQueue_ConcurrentAdd_PreservesOrderAndDeliversAll()
        {
            // 本番経路の検証: 複数スレッドが EventQueue.AddEvent を並行呼び出ししても、
            // _addLock により採番順 == enqueue 順となり、消費側カーソルの飛び越しが起きない。
            var eq = new EventQueue();
            try
            {
                eq.UpdateClientActivity("c1"); // 受信クライアント登録
                const int producers = 4;
                const int perProducer = 500;
                int total = producers * perProducer;

                var received = Run(async () =>
                {
                    var consumed = new List<long>();
                    var buffer = new List<EventItem>();

                    var adders = new Task[producers];
                    for (int p = 0; p < producers; p++)
                    {
                        adders[p] = Task.Run(() =>
                        {
                            for (int i = 0; i < perProducer; i++)
                            {
                                eq.AddEvent("payload", "data");
                            }
                        });
                    }

                    long cursor = 0;
                    var deadline = Stopwatch.StartNew();
                    while (consumed.Count < total && deadline.Elapsed < TimeSpan.FromSeconds(10))
                    {
                        int n = eq.DrainEvents("c1", cursor, buffer);
                        if (n == 0)
                        {
                            await Task.Yield();
                            continue;
                        }
                        for (int i = 0; i < n; i++) consumed.Add(buffer[i].Id);
                        cursor = buffer[n - 1].Id;
                    }

                    await Task.WhenAll(adders);
                    return consumed;
                });

                Assert.AreEqual(total, received.Count, "全イベントを受信 (飛び越しなし)");
                for (int i = 1; i < received.Count; i++)
                    Assert.Less(received[i - 1], received[i], "Id 昇順かつ重複なし");
            }
            finally
            {
                eq.Shutdown();
            }
        }

        // ---- EventQueue 経由の統合 (受信箱ポーリング) ----

        [Test]
        public void EventQueue_DrainEvents_FillsBuffer()
        {
            var eq = new EventQueue();
            try
            {
                eq.UpdateClientActivity("c1"); // クライアント登録 (AddEvent が届くように)
                eq.AddEvent("hello", "data");

                var buffer = new List<EventItem>();
                int n = eq.DrainEvents("c1", 0, buffer);

                Assert.AreEqual(1, n);
                Assert.AreEqual(1, buffer.Count);
                Assert.AreEqual("hello", buffer[0].Data);
            }
            finally
            {
                eq.Shutdown();
            }
        }

        [Test]
        public void EventQueue_DrainEvents_EmptyReturnsZero()
        {
            var eq = new EventQueue();
            try
            {
                eq.UpdateClientActivity("c1");

                var buffer = new List<EventItem>();
                Assert.AreEqual(0, eq.DrainEvents("c1", 0, buffer));
            }
            finally
            {
                eq.Shutdown();
            }
        }

        [Test]
        public void EventQueue_DrainEvents_UnknownClientRegistersAndReceivesLater()
        {
            // ポーリングで初めて名乗ったクライアントは、その時点では何も溜まっていないが、
            // 以後のブロードキャストは届く (受信箱がここで作られるため)。
            var eq = new EventQueue();
            try
            {
                var buffer = new List<EventItem>();
                Assert.AreEqual(0, eq.DrainEvents("newcomer", 0, buffer));

                eq.AddEvent("hello", "data");

                Assert.AreEqual(1, eq.DrainEvents("newcomer", 0, buffer));
                Assert.AreEqual("hello", buffer[0].Data);
            }
            finally
            {
                eq.Shutdown();
            }
        }

        // ---- ワイヤ形式 ----

        [Test]
        public void GetPayload_FillsTypeAndTimestampWhenMissing()
        {
            var item = new EventItem
            {
                Id = 1,
                Data = new { message = "hi" },
                EventType = "system_notification",
            };

            var payload = item.GetPayload();

            Assert.AreEqual("system_notification", (string)payload["type"]);
            Assert.IsNotNull(payload["timestamp"]);
            Assert.AreEqual("hi", (string)payload["message"]);
        }

        [Test]
        public void GetPayload_KeepsTypeCarriedByTheData()
        {
            var item = new EventItem
            {
                Id = 1,
                Data = new { type = "confirm_request", id = "confirm-1" },
                EventType = "confirm_request",
            };

            Assert.AreEqual("confirm_request", (string)item.GetPayload()["type"]);
            Assert.AreEqual("confirm-1", (string)item.GetPayload()["id"]);
        }

        [Test]
        public void GetPayload_IsCachedAcrossClients()
        {
            // 1 つの EventItem が全クライアントの受信箱に配られるため、
            // ペイロードは 1 度だけ組み立てて使い回す。
            var item = new EventItem { Id = 1, Data = new { message = "hi" }, EventType = "data" };

            Assert.AreSame(item.GetPayload(), item.GetPayload());
        }
    }
}
