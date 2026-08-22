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
    /// It only does something in the editor while not playing, and only as far as a
    /// <see cref="UnityEngine.Object"/> stands behind the value — the same three ways
    /// <see cref="LiveEditorSession.HasSaveTarget"/> looks for one, so whatever that call lets
    /// through is recorded here. Play mode is left alone (the session baseline owns "changed" there).
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
        private readonly UnityEngine.Object _serializedOwner;

        /// <summary>
        /// Whether this scope wraps an editor write at all. Not derivable from the three targets:
        /// they are also null for a write that reached nothing, and that write still has to redraw.
        /// </summary>
        private readonly bool _isEditorWrite;
#endif

        /// <param name="target">The registered live object the request addressed.</param>
        /// <param name="propertyOwner">
        /// The instance that actually holds the member, when the path descends into a nested object.
        /// Ignored when it is the same as <paramref name="target"/> or has no Unity object behind it.
        /// </param>
        /// <param name="container">
        /// The container the request was resolved through, used to find whoever serializes
        /// <paramref name="target"/>. Needed for members the live object keeps in its own fields:
        /// those are saved by the scene holding it, not by the object it wraps.
        /// </param>
        public LiveEditorWriteScope(object target, object propertyOwner = null, LiveObjectContainer container = null)
        {
#if UNITY_EDITOR
            _target = null;
            _owner = null;
            _serializedOwner = null;
            _isEditorWrite = false;

            if (Application.isPlaying) return;
            _isEditorWrite = true;

            _target = LiveEditorSession.ResolveSaveTarget(target);
            _owner = ReferenceEquals(propertyOwner, target)
                ? null
                : LiveEditorSession.ResolveSaveTarget(propertyOwner);
            _serializedOwner = container?.FindSerializedOwner(target);

            // A proxy and the object it wraps can both resolve to the same target; recording twice
            // would put two entries on the undo stack for one write.
            if (ReferenceEquals(_owner, _target)) _owner = null;
            if (ReferenceEquals(_serializedOwner, _target) || ReferenceEquals(_serializedOwner, _owner))
                _serializedOwner = null;

            _Record(_target);
            _Record(_owner);
            _Record(_serializedOwner);
#endif
        }

        public void Dispose()
        {
#if UNITY_EDITOR
            _MarkDirty(_target);
            _MarkDirty(_owner);
            _MarkDirty(_serializedOwner);

            if (!_isEditorWrite) return;

            // ⚠ 書いただけでは絵は変わらない。エディタは自分が描き直す理由を知らないので、
            // 頼まないと次にユーザーがシーンへ触るまで古い絵のままになる (「操作しても
            // 反映されない、シーンをクリックすると反映される」)。再生中は毎フレーム描き
            // 直されるので、これが要るのはエディタセッションだけ。
            //
            // 2 つとも要る: PlayerLoop は値を実体へ流し込む側の処理 ([ExecuteAlways] の
            // Update など) を 1 度回し、Repaint は回った結果を画面へ出す。
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
#endif
        }

#if UNITY_EDITOR
        private static void _Record(UnityEngine.Object unityObject)
        {
            // The == null comparison is the Unity one on purpose: a destroyed object must not be recorded.
            if (unityObject == null) return;
            Undo.RecordObject(unityObject, kUndoName);
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
