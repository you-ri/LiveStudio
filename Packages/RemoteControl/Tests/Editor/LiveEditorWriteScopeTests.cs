// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Verifies that a REST write made in the editor lands on Unity's own unsaved state. Without it a
    /// user changes something from the remote, sees no asterisk on the scene, and loses the edit by
    /// closing without saving.
    /// </summary>
    public class LiveEditorWriteScopeTests
    {
        private GameObject _gameObject;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject(nameof(LiveEditorWriteScopeTests));
            EditorUtility.ClearDirty(_gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null) Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void Scope_MarksTheTargetDirty()
        {
            Assume.That(EditorUtility.IsDirty(_gameObject), Is.False);

            using (new LiveEditorWriteScope(_gameObject))
            {
                _gameObject.name = "edited from the remote";
            }

            Assert.IsTrue(EditorUtility.IsDirty(_gameObject));
        }

        [Test]
        public void Scope_MarksTheNestedOwnerDirtyToo()
        {
            // パスが入れ子の公開オブジェクトへ降りていると、実際に書き換わるのは
            // リクエストが名指しした対象ではなくその子になる。
            var owner = new GameObject("nested owner");
            EditorUtility.ClearDirty(owner);

            using (new LiveEditorWriteScope(_gameObject, owner))
            {
            }

            Assert.IsTrue(EditorUtility.IsDirty(owner));
            Object.DestroyImmediate(owner);
        }

        [Test]
        public void Scope_IgnoresTargetsWithNothingToRecord()
        {
            // 素の C# の公開オブジェクトは Unity の未保存状態を持たない。
            // ここで落ちると、その手の対象への書き込みが丸ごと 500 になる。
            Assert.DoesNotThrow(() =>
            {
                using (new LiveEditorWriteScope(new object()))
                {
                }
            });

            Assert.DoesNotThrow(() =>
            {
                using (new LiveEditorWriteScope(null))
                {
                }
            });
        }

        [Test]
        public void Scope_IgnoresDestroyedTargets()
        {
            var destroyed = new GameObject("destroyed");
            Object.DestroyImmediate(destroyed);

            Assert.DoesNotThrow(() =>
            {
                using (new LiveEditorWriteScope(destroyed))
                {
                }
            });
        }
    }
}
