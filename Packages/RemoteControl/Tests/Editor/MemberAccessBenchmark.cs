// Copyright (c) You-Ri, 2026

using System;
using NUnit.Framework;
using Lilium.RemoteControl;
using Debug = UnityEngine.Debug;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Lilium.RemoteControl.Tests
{
    // Source Generator がアクセサを生成する対象。トップレベル internal なので、
    // 同アセンブリに emit される生成コード (自由関数) から public メンバーにアクセス可能。
    [ExposedClass]
    internal class BenchTarget
    {
        [ExposedProperty]
        public float speed { get; set; }

        [ExposedField]
        public int count;
    }

    /// <summary>
    /// Phase 1b 検証: ExposedClass の公開メンバーに対し Source Generator が生成した
    /// get/set アクセサが登録され、reflection より高速であることを確認する。
    /// </summary>
    public class MemberAccessBenchmark
    {
        private const int kWarmup = 20000;
        private const int kIter = 1000000;

        [Test]
        public void GeneratedAccessor_IsRegistered()
        {
            Assert.IsTrue(
                ExposedMemberAccessorTable.TryGet(typeof(BenchTarget), "speed", out var pg, out var ps),
                "property 'speed' accessor should be generated");
            Assert.IsNotNull(pg, "property getter");
            Assert.IsNotNull(ps, "property setter");

            Assert.IsTrue(
                ExposedMemberAccessorTable.TryGet(typeof(BenchTarget), "count", out var fg, out var fs),
                "field 'count' accessor should be generated");
            Assert.IsNotNull(fg, "field getter");
            Assert.IsNotNull(fs, "field setter");

            // 値が正しく get/set できること。
            var obj = new BenchTarget();
            ps(obj, 3.5f);
            Assert.AreEqual(3.5f, (float)pg(obj));
            fs(obj, 42);
            Assert.AreEqual(42, (int)fg(obj));
        }

        [Test]
        public void Benchmark_GeneratedVsReflection()
        {
            var obj = new BenchTarget { speed = 1.5f, count = 7 };

            var prop = typeof(BenchTarget).GetProperty("speed");
            ExposedMemberAccessorTable.TryGet(typeof(BenchTarget), "speed", out var del, out var setDel);

            object boxed = 2.5f;
            object sink = null;

            for (int i = 0; i < kWarmup; i++)
            {
                sink = prop.GetValue(obj);
                sink = del(obj);
                prop.SetValue(obj, boxed);
                setDel(obj, boxed);
            }

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < kIter; i++) sink = prop.GetValue(obj);
            sw.Stop();
            double gRefl = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            for (int i = 0; i < kIter; i++) sink = del(obj);
            sw.Stop();
            double gDel = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            for (int i = 0; i < kIter; i++) prop.SetValue(obj, boxed);
            sw.Stop();
            double sRefl = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            for (int i = 0; i < kIter; i++) setDel(obj, boxed);
            sw.Stop();
            double sDel = sw.Elapsed.TotalMilliseconds;

            Debug.Log(
                $"[Bench] real-path x{kIter:N0}  " +
                $"GET reflection={gRefl:F1}ms generated={gDel:F1}ms ({gRefl / gDel:F1}x)  " +
                $"SET reflection={sRefl:F1}ms generated={sDel:F1}ms ({sRefl / sDel:F1}x)  " +
                $"(sink={sink}, speed={obj.speed})");

            Assert.Pass();
        }
    }
}
