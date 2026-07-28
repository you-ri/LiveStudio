// Copyright (c) You-Ri, 2026
using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// 型付きアクセサ (Source Generator が値型メンバーに生成する Func&lt;object,T&gt;/Action&lt;object,T&gt;) と、
    /// それを使う <see cref="LiveProperty.TryGetValue{T}"/> / <see cref="LiveProperty.TrySetValue{T}(T)"/> の
    /// 正当性と「値型読み取りが boxing しない」ことを保証する。
    /// </summary>
    [TestFixture]
    public class TypedAccessorTests
    {
        public enum Mode { A, B, C }

        [LiveClass("TypedAccessorTarget")]
        public class TypedAccessorTarget
        {
            [LiveField] public bool flag;
            [LiveField] public float weight;
            [LiveField] public int count;
            [LiveField] public Vector3 position;
            [LiveField] public Mode mode;
            [LiveField] public string label;
            [LiveProperty] public float readOnlyFloat => 3.5f;
        }

        LiveObjectHandle _handle;
        TypedAccessorTarget _target;

        [SetUp]
        public void SetUp()
        {
            LiveObjectRegistry.ClearAll();
            LiveClass.Clear();
            LiveClass.RegisterFromAttributes<TypedAccessorTarget>();
            _target = new TypedAccessorTarget
            {
                flag = true,
                weight = 2.5f,
                count = 7,
                position = new Vector3(1, 2, 3),
                mode = Mode.B,
                label = "hello",
            };
            _handle = LiveObjectRegistry.Create(_target, "typed_target").Value;
        }

        [TearDown]
        public void TearDown()
        {
            LiveObjectRegistry.ClearAll();
            LiveClass.Clear();
        }

        LiveProperty Prop(string name) => _handle.GetProperty(name).Value;

        // --- Source Generator が値型メンバーに型付き getter を生成している前提を確認 ---
        // (SG がこのテストアセンブリを処理していないと fast path が働かず、以降のゼロアロケーションが崩れる)
        [Test]
        public void ValueTypeMembers_HaveTypedGetter()
        {
            Assert.IsTrue(Prop("weight").type.typedGetter is Func<object, float>, "float typedGetter");
            Assert.IsTrue(Prop("flag").type.typedGetter is Func<object, bool>, "bool typedGetter");
            Assert.IsTrue(Prop("count").type.typedGetter is Func<object, int>, "int typedGetter");
            Assert.IsTrue(Prop("position").type.typedGetter is Func<object, Vector3>, "Vector3 typedGetter");
            Assert.IsTrue(Prop("mode").type.typedGetter is Func<object, Mode>, "enum typedGetter");
            // 参照型 (string) は object 経路で元々 box しないので typed は生成しない。
            Assert.IsNull(Prop("label").type.typedGetter, "reference type member has no typedGetter");
        }

        // --- TryGetValue ---
        [Test]
        public void TryGetValue_ReturnsTypedValue()
        {
            Assert.IsTrue(Prop("weight").TryGetValue<float>(out var f)); Assert.AreEqual(2.5f, f);
            Assert.IsTrue(Prop("flag").TryGetValue<bool>(out var b)); Assert.IsTrue(b);
            Assert.IsTrue(Prop("count").TryGetValue<int>(out var i)); Assert.AreEqual(7, i);
            Assert.IsTrue(Prop("position").TryGetValue<Vector3>(out var v)); Assert.AreEqual(new Vector3(1, 2, 3), v);
            Assert.IsTrue(Prop("mode").TryGetValue<Mode>(out var m)); Assert.AreEqual(Mode.B, m);
        }

        [Test]
        public void TryGetValue_ReferenceType_FallsBackAndSucceeds()
        {
            // string は typed 非対象だが object 経路フォールバックで取得できる。
            Assert.IsTrue(Prop("label").TryGetValue<string>(out var s));
            Assert.AreEqual("hello", s);
        }

        [Test]
        public void TryGetValue_TypeMismatch_ReturnsFalse()
        {
            // float メンバーに int で要求 → false (out は default)。
            Assert.IsFalse(Prop("weight").TryGetValue<int>(out var i));
            Assert.AreEqual(0, i);
        }

        // --- TrySetValue ---
        [Test]
        public void TrySetValue_UpdatesValue()
        {
            Assert.IsTrue(Prop("weight").TrySetValue(9.5f));
            Assert.AreEqual(9.5f, _target.weight);

            Assert.IsTrue(Prop("flag").TrySetValue(false));
            Assert.IsFalse(_target.flag);

            Assert.IsTrue(Prop("position").TrySetValue(new Vector3(4, 5, 6)));
            Assert.AreEqual(new Vector3(4, 5, 6), _target.position);
        }

        [Test]
        public void TrySetValue_ReadOnly_ReturnsFalse()
        {
            Assert.IsFalse(Prop("readOnlyFloat").TrySetValue(1f));
        }

        [Test]
        public void TrySetValue_FiresPropertyChanged_WithOldValue()
        {
            var cls = LiveClass.Find(typeof(TypedAccessorTarget));
            float oldSeen = float.NaN;
            int fired = 0;
            LiveClass.PropertyChangedDelegate handler = (p, old) =>
            {
                fired++;
                if (old is float o) oldSeen = o;
            };
            cls.onPropertyChanged += handler;
            try
            {
                Assert.IsTrue(Prop("weight").TrySetValue(8f));
            }
            finally { cls.onPropertyChanged -= handler; }

            Assert.AreEqual(1, fired, "onPropertyChanged fired once");
            Assert.AreEqual(2.5f, oldSeen, "old value passed to listener");
            Assert.AreEqual(8f, _target.weight);
        }

        [Test]
        public void TrySetValue_MarksDirty()
        {
            Assert.IsFalse(_handle.IsPropertyDirty("weight"), "precondition: not dirty");
            Assert.IsTrue(Prop("weight").TrySetValue(5f));
            Assert.IsTrue(_handle.IsPropertyDirty("weight"), "dirty after change");
        }

        // --- ゼロアロケーション (値型読み取り = 毎フレームのホット経路) ---
        [Test]
        public void TryGetValue_Float_IsAllocationFree()
        {
            var p = Prop("weight");
            p.TryGetValue<float>(out _); // ウォームアップ (JIT / 初回解決)

            float sink = 0f;
            Assert.That(() =>
            {
                for (int i = 0; i < 256; i++)
                {
                    if (p.TryGetValue<float>(out var f)) sink += f;
                }
            }, Is.Not.AllocatingGCMemory());

            Assert.AreNotEqual(0f, sink);
        }

        [Test]
        public void TryGetValue_Vector3_IsAllocationFree()
        {
            var p = Prop("position");
            p.TryGetValue<Vector3>(out _);

            float sink = 0f;
            Assert.That(() =>
            {
                for (int i = 0; i < 256; i++)
                {
                    if (p.TryGetValue<Vector3>(out var v)) sink += v.x;
                }
            }, Is.Not.AllocatingGCMemory());

            Assert.AreNotEqual(0f, sink);
        }

        [Test]
        public void TryGetValue_Enum_IsAllocationFree()
        {
            var p = Prop("mode");
            p.TryGetValue<Mode>(out _);

            int sink = 0;
            Assert.That(() =>
            {
                for (int i = 0; i < 256; i++)
                {
                    if (p.TryGetValue<Mode>(out var m)) sink += (int)m;
                }
            }, Is.Not.AllocatingGCMemory());

            Assert.AreNotEqual(0, sink);
        }
    }
}
