// Copyright (c) You-Ri, 2026

using System;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;   // AllocatingGCMemory 拡張メソッドのため必須
using Lilium.RemoteControl;
using Debug = UnityEngine.Debug;
using Stopwatch = System.Diagnostics.Stopwatch;
using ConstraintIs = UnityEngine.TestTools.Constraints.Is;

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

        // 参照型メンバー: getter/setter ともボックス化が無いので GC alloc ゼロを検証できる。
        [ExposedProperty]
        public string label { get; set; }
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
        public void GeneratedSetter_DoesNotAllocate()
        {
            var obj = new BenchTarget();
            ExposedMemberAccessorTable.TryGet(typeof(BenchTarget), "speed", out _, out var setSpeed);
            ExposedMemberAccessorTable.TryGet(typeof(BenchTarget), "count", out _, out var setCount);
            ExposedMemberAccessorTable.TryGet(typeof(BenchTarget), "label", out _, out var setLabel);

            // 値型の set はボックス済みの値を渡すため、setter 自体は unbox のみで alloc しない。
            object boxedFloat = 2.5f;
            object boxedInt = 7;
            object str = "hello";

            // JIT ウォームアップ (初回呼び出しのコードパス確定)。
            setSpeed(obj, boxedFloat);
            setCount(obj, boxedInt);
            setLabel(obj, str);

            Assert.That(() => setSpeed(obj, boxedFloat), ConstraintIs.Not.AllocatingGCMemory(),
                "value-type property setter (boxed value) should not allocate");
            Assert.That(() => setCount(obj, boxedInt), ConstraintIs.Not.AllocatingGCMemory(),
                "value-type field setter (boxed value) should not allocate");
            Assert.That(() => setLabel(obj, str), ConstraintIs.Not.AllocatingGCMemory(),
                "reference-type property setter should not allocate");
        }

        [Test]
        public void GeneratedGetter_ReferenceType_DoesNotAllocate()
        {
            var obj = new BenchTarget { label = "hello" };
            ExposedMemberAccessorTable.TryGet(typeof(BenchTarget), "label", out var getLabel, out _);

            object sink = getLabel(obj); // warmup
            Assert.That(() => { sink = getLabel(obj); }, ConstraintIs.Not.AllocatingGCMemory(),
                "reference-type getter returns the reference without boxing -> no allocation");
            Assert.AreEqual("hello", sink);

            // 注: 値型 getter (speed/count) は戻り値を object にボックス化するため alloc する。
            // これは reflection と同じ本質的コストで、ゼロ alloc には raw/typed API が必要 (将来対応)。
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
