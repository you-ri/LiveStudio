// Copyright (c) You-Ri, 2026

using System;

using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Puts a REST write on the editor's undo history and unsaved state, so a change made from the
    /// remote is treated the same as one made in the Inspector: the scene gets its asterisk and
    /// Ctrl+Z takes it back.
    /// </summary>
    /// <remarks>
    /// Create it <b>before</b> writing the value: <c>Undo.RecordObject</c> snapshots the value as it is
    /// at the call, so recording after the write would capture the new value.
    /// <para/>
    /// It only does something in the editor while not playing, and only for
    /// <see cref="UnityEngine.Object"/> targets. Play mode is left alone (the session baseline owns
    /// "changed" there), and a plain C# live object has nothing to record onto.
    /// <para/>
    /// ⚠ Undo records <b>serialized fields</b>. Live members exposed as C# properties are restored only
    /// as far as their backing field is serialized, and their setter does not re-run on undo — the
    /// same limitation the Inspector has when it writes a serialized field directly.
    /// </remarks>
    public readonly struct LiveEditorWriteScope : IDisposable
    {
#if UNITY_EDITOR
        private const string kUndoName = "Remote Control Edit";

        private readonly UnityEngine.Object _target;
        private readonly UnityEngine.Object _owner;
#endif

        /// <param name="target">The registered live object the request addressed.</param>
        /// <param name="propertyOwner">
        /// The instance that actually holds the member, when the path descends into a nested object.
        /// Ignored when it is the same as <paramref name="target"/> or is not a Unity object.
        /// </param>
        public LiveEditorWriteScope(object target, object propertyOwner = null)
        {
#if UNITY_EDITOR
            _target = null;
            _owner = null;

            if (Application.isPlaying) return;

            _target = _AsLiveUnityObject(target);
            _owner = ReferenceEquals(propertyOwner, target) ? null : _AsLiveUnityObject(propertyOwner);

            if (_target != null) Undo.RecordObject(_target, kUndoName);
            if (_owner != null) Undo.RecordObject(_owner, kUndoName);
#endif
        }

        public void Dispose()
        {
#if UNITY_EDITOR
            _MarkDirty(_target);
            _MarkDirty(_owner);
#endif
        }

#if UNITY_EDITOR
        private static UnityEngine.Object _AsLiveUnityObject(object obj)
        {
            // The == null comparison is the Unity one on purpose: a destroyed object must not be recorded.
            return obj is UnityEngine.Object unityObject && unityObject != null ? unityObject : null;
        }

        private static void _MarkDirty(UnityEngine.Object unityObject)
        {
            if (unityObject == null) return;

            EditorUtility.SetDirty(unityObject);

            // ⚠ プレハブインスタンスへスクリプトから書いた分は、放っておくと上書き一覧に載らない。
            // 載るのは次にこのオブジェクトが直列化されたときで、書いた直後の応答には間に合わず、
            // 「変更したのに changed が false、あとで見ると true」になる。エディタが自分で
            // 書くときと同じように、書いた直後にここで載せる。
            if (PrefabUtility.IsPartOfPrefabInstance(unityObject))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(unityObject);
            }

            // An object living in a scene needs the scene itself marked; that is what puts the
            // asterisk on the scene and makes Ctrl+S clear it.
            var gameObject = unityObject as GameObject ?? (unityObject as Component)?.gameObject;
            if (gameObject != null && gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
        }
#endif
    }
}
