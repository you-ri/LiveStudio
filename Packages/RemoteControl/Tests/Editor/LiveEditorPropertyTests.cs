// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Pins the editor rule for "changed" and "revert": the editor answers only where it has an answer
    /// of its own (a serialized property overriding its prefab source), and says no everywhere else.
    /// The rule exists so that no per-property default is invented that the editor does not have, so
    /// the "says no" cases are the point, not an edge.
    /// <para/>
    /// The positive case (an actual prefab override reverting) is not covered here: a component defined
    /// in this test assembly is dropped when the object is saved as a prefab, and one defined at the top
    /// level of an editor assembly cannot be added to a GameObject at all. What is covered is the part
    /// written here — which live members reach a serialized property — while the override read and the
    /// revert itself are Unity's own calls on that property.
    /// </summary>
    public class LiveEditorPropertyTests
    {
        [LiveClass("EditorRuleTestComponent")]
        public class EditorRuleTestComponent : MonoBehaviour
        {
            /// 保存されるフィールド公開。エディタが答えを持てる唯一の形。
            [SerializeField, LiveField]
            public float level = 1.0f;

            /// 段降りのパス解決 (配列要素) の確認用。
            [SerializeField, LiveField]
            public float[] levels = { 1.0f, 2.0f };

            /// C# プロパティ公開。保存先の宣言が無いので、エディタには対応するものが無い。
            [LiveProperty]
            public float doubled
            {
                get => level * 2.0f;
                set => level = value * 0.5f;
            }
        }

        /// UnityEngine.Object ではない公開オブジェクト。
        [System.Serializable]
        [LiveClass("EditorRuleTestPlainObject")]
        public class EditorRuleTestPlainObject
        {
            [LiveField]
            public float amount = 1.0f;
        }

        private GameObject _sceneObject;
        private EditorRuleTestComponent _component;
        private readonly List<LiveObjectHandle> _handles = new List<LiveObjectHandle>();

        [SetUp]
        public void SetUp()
        {
            _sceneObject = new GameObject(nameof(LiveEditorPropertyTests));
            _component = _sceneObject.AddComponent<EditorRuleTestComponent>();
        }

        [TearDown]
        public void TearDown()
        {
            // レジストリはプロセス全体で 1 つ。残すと他のテストが拾う。
            foreach (var handle in _handles) LiveObjectRegistry.Unregister(handle);
            _handles.Clear();

            if (_sceneObject != null) Object.DestroyImmediate(_sceneObject);
        }

        private LiveProperty Property(object target, string path)
        {
            var handle = new LiveObjectHandle(
                "editor-rule-" + target.GetHashCode(), LiveClass.Find(target.GetType()), target);
            _handles.Add(handle);

            var property = handle.FindProperty(path);
            Assert.IsTrue(property.HasValue, "テストの前提: " + path + " が公開されていること");
            return property.Value;
        }

        [Test]
        public void EditorRule_IsActiveWhileNotPlaying()
        {
            Assert.IsTrue(LiveEditorProperty.isEditorRuleActive);
        }

        [Test]
        public void SerializedField_IsWithinTheEditorsView()
        {
            Assert.IsTrue(LiveEditorProperty.HasSerializedBacking(Property(_component, "level")));
        }

        [Test]
        public void PathIntoASerializedArray_IsWithinTheEditorsView()
        {
            // 段降りのパスも保存先まで辿れること。ここが切れると、要素が一律「対象外」に落ちる。
            Assert.IsTrue(LiveEditorProperty.HasSerializedBacking(Property(_component, "levels[0]")));
        }

        [Test]
        public void MemberWithNoSerializedBacking_IsOutOfScope()
        {
            // C# プロパティ公開は保存先の宣言が無いので、エディタには対応するものが無い。
            var property = Property(_component, "doubled");

            Assert.IsFalse(LiveEditorProperty.HasSerializedBacking(property));
            Assert.IsFalse(LiveEditorProperty.IsChanged(property));
            Assert.IsFalse(LiveEditorProperty.TryRevert(property));
        }

        [Test]
        public void NonUnityObject_IsOutOfScope()
        {
            var property = Property(new EditorRuleTestPlainObject { amount = 5.0f }, "amount");

            Assert.IsFalse(LiveEditorProperty.HasSerializedBacking(property));
            Assert.IsFalse(LiveEditorProperty.IsChanged(property));
            Assert.IsFalse(LiveEditorProperty.TryRevert(property));
        }

        [Test]
        public void NonPrefabObject_HasNothingToRevert()
        {
            // 保存先はあるが、シーンに直接置いたオブジェクトにはエディタ側の戻す先が無い。
            // 「保存先がある = 戻せる」ではないことを固定する。
            _component.level = 5.0f;
            var property = Property(_component, "level");

            Assert.IsTrue(LiveEditorProperty.HasSerializedBacking(property));
            Assert.IsFalse(LiveEditorProperty.IsChanged(property));
            Assert.IsFalse(LiveEditorProperty.TryRevert(property));
        }
    }
}
