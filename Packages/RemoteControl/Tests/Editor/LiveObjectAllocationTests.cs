// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// LiveObjectHandle を readonly struct 化した主目的である「使い捨てビューの GC Alloc ゼロ」を計測・保証する。
    /// <see cref="LiveObjectHandle.CreateUnregistered"/> は登録もデフォルトキャプチャも行わない純粋な値構築なので、
    /// 旧 class 実装で毎回発生していたヒープ確保 (~48 byte/呼び出し) が発生しなくなる。
    /// Is.Not.AllocatingGCMemory は内部で ProfilerRecorder により GC.Alloc を計測する Unity 標準の制約。
    /// </summary>
    [TestFixture]
    public class LiveObjectAllocationTests
    {
        [LiveClass("AllocTestTarget")]
        public class AllocTestTarget
        {
            [LiveField] public int value;
            [LiveProperty] public string label { get; set; }
        }

        LiveClass _class;
        AllocTestTarget _target;

        [SetUp]
        public void SetUp()
        {
            LiveObjectRegistry.ClearAll();
            LiveClass.Clear();
            LiveClass.RegisterFromAttributes<AllocTestTarget>();
            _class = LiveClass.Find(typeof(AllocTestTarget));
            _target = new AllocTestTarget { value = 7, label = "x" };
        }

        [TearDown]
        public void TearDown()
        {
            LiveObjectRegistry.ClearAll();
            LiveClass.Clear();
        }

        [Test]
        public void CreateUnregistered_IsAllocationFree()
        {
            // ウォームアップ (JIT・初回解決を計測対象から外す)
            var warm = LiveObjectHandle.CreateUnregistered(_class, _target);
            Assert.IsTrue(warm.target != null, "precondition: warmup view is valid");

            int hits = 0;
            Assert.That(() =>
            {
                for (int i = 0; i < 256; i++)
                {
                    // readonly struct なのでスタック値。ヒープ確保は発生しない。
                    var view = LiveObjectHandle.CreateUnregistered(_class, _target);
                    // dead-code 除去を防ぐため参照を読む (target は既存参照で boxing/alloc しない)
                    if (view.target != null) hits++;
                }
            }, Is.Not.AllocatingGCMemory());

            Assert.AreEqual(256, hits, "all views constructed and observed");
        }

        [Test]
        public void StructMemberAccess_IsAllocationFree()
        {
            // 値型メンバアクセス (target / targetTypeName / hasId / isValid) が alloc しないことを確認。
            var warm = LiveObjectHandle.CreateUnregistered(_class, _target);
            Assert.IsNotNull(warm.targetTypeName);

            int hits = 0;
            Assert.That(() =>
            {
                for (int i = 0; i < 256; i++)
                {
                    var view = LiveObjectHandle.CreateUnregistered(_class, _target);
                    if (view.target != null && !view.hasId && view.isValid && view.targetTypeName != null)
                        hits++;
                }
            }, Is.Not.AllocatingGCMemory());

            Assert.AreEqual(256, hits);
        }
    }
}
