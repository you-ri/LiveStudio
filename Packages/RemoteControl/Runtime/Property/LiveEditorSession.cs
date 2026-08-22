// Copyright (c) You-Ri, 2026

using System;

using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Whether this is an editor session (the editor, not playing) and what that rules out.
    /// </summary>
    /// <remarks>
    /// Editing in the editor edits the base scene: the values live in the .unity scene or in an
    /// asset, and saving them is the editor's own Save. Live scenes belong to play mode and to the
    /// built app. Keeping both at once would give every value two homes with no answer to which one
    /// wins, so an editor session does not open, write, or track a live scene at all.
    /// <para/>
    /// ⚠ The refusal belongs here, on the server, rather than in the remote app's UI. The same
    /// values are reachable from operation tiles and from other clients, and hiding a button stops
    /// none of those. The UI hides them too, but only so nobody is offered something that fails.
    /// <para/>
    /// This is the single source for the question. <see cref="LiveEditorProperty"/> asks it to decide
    /// whose rule answers "is this changed", and the file I/O in
    /// <see cref="LiveSceneSaveSystem"/> asks it to decide whether to run at all.
    /// </remarks>
    public static class LiveEditorSession
    {
        /// <summary>Pinned answer while a <see cref="Override"/> is alive. Null means "look at the app".</summary>
        private static bool? _override;

        /// <summary>
        /// Whether play mode is running, kept up to date from the main thread.
        /// </summary>
        /// <remarks>
        /// ⚠ Not <c>Application.isPlaying</c>. This question is asked from request handlers, which run
        /// on worker threads, and every Unity API — that property included — throws there. So the
        /// value is written at the two moments it can change, both of which are on the main thread,
        /// and read from anywhere.
        /// <para/>
        /// Written without a lock on purpose: it is a single bool flipped twice per play session,
        /// and a reader that catches the previous value is one request behind, which is the same
        /// tolerance the poll interval already has.
        /// </remarks>
        private static bool _isPlaying;

        /// <summary>True in the editor while not playing. Always false in a build.</summary>
        public static bool isEditorSession
        {
            get
            {
                if (_override.HasValue) return _override.Value;
#if UNITY_EDITOR
                return !_isPlaying;
#else
                // A build is nothing but a play session, and there is no editor to ask.
                return false;
#endif
            }
        }

        /// <summary>
        /// Marks the session as playing. Runs before the first scene's Awake, on the main thread.
        /// </summary>
        /// <remarks>
        /// ⚠ Must land before anything asks. The live scene load happens in a host's Start, and
        /// answering "editor" there would refuse the very load that starts the session.
        /// <para/>
        /// This also covers domain reload being off: statics keep their previous values then, but
        /// this method still runs on entering play, and the exit path below clears it again.
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void _MarkPlaying()
        {
            _isPlaying = true;
        }

#if UNITY_EDITOR
        /// <summary>Clears the flag when the editor comes back from play.</summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void _TrackPlayModeExit()
        {
            // Re-subscribing on every domain reload would stack handlers; drop ours first.
            UnityEditor.EditorApplication.playModeStateChanged -= _OnPlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += _OnPlayModeStateChanged;
            // A reload can land mid-session (script recompile while playing), so take the state as
            // it is now rather than assuming the editor is idle.
            _isPlaying = UnityEditor.EditorApplication.isPlaying;
        }

        private static void _OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
            {
                _isPlaying = false;
            }
        }
#endif

        /// <summary>Why a live scene read or write was refused. Returned to the caller as-is.</summary>
        public const string kSceneIoRejected =
            "Live scenes are not available in the editor while not playing. "
            + "Editor changes belong to the scene; save the scene instead.";

        /// <summary>Why a property write was refused.</summary>
        public const string kWriteRejected =
            "Not editable in the editor: this value lives in the live scene or in the project settings, "
            + "neither of which is written while the editor is not playing.";

        /// <summary>
        /// Whether this write has nowhere to land, and so must be refused.
        /// </summary>
        /// <remarks>
        /// Only values that cannot reach the base scene are refused:
        /// <list type="bullet">
        /// <item>members declared <see cref="PersistScope.Project"/> — those are written by the live
        /// scene save, which never runs here, so accepting one means it is gone next launch</item>
        /// <item>persistable members with no <see cref="UnityEngine.Object"/> anywhere behind them —
        /// nothing saves those but the live scene, which is not written here</item>
        /// </list>
        /// Everything else stays writable. A member on a component or a ScriptableObject is saved by
        /// the editor itself, and a non-persistable member was never going to outlive the session
        /// anyway — refusing those would take away the scene name, the asset list, and every other
        /// "what is happening right now" value.
        /// <para/>
        /// ⚠ Main thread only — the null checks reach into the native object through Unity's
        /// <c>==</c>. Every write path already runs there; <see cref="isEditorSession"/> on its own
        /// is the one part of this class a worker thread may ask.
        /// </remarks>
        /// <param name="container">The container the request was resolved through. May be null.</param>
        /// <param name="target">The live object the request addressed.</param>
        /// <param name="property">The resolved member. Its declaration decides where the value lands.</param>
        public static bool IsWriteRejected(LiveObjectContainer container, object target, in LiveProperty property)
        {
            if (!isEditorSession) return false;

            var type = property.type;
            if (type == null || !type.isPersistable) return false;

            if (type.persistScope == PersistScope.Project) return true;

            return !HasSaveTarget(container, target, property.obj);
        }

        /// <summary>
        /// Whether an editor write on this member reaches a <see cref="UnityEngine.Object"/> that the
        /// editor's own Save would write out.
        /// </summary>
        /// <remarks>
        /// There are three ways for one to exist, and a member needs only one of them:
        /// the instance holding the member is itself a Unity object (a component reached through a
        /// nested path); the live object wraps one (every <see cref="LiveUnityObjectBase"/> proxy —
        /// the value is forwarded to the object it wraps); or something serializes the live object
        /// itself, which is what saves members the proxy keeps in its own fields.
        /// </remarks>
        public static bool HasSaveTarget(LiveObjectContainer container, object target, object propertyOwner)
        {
            if (ResolveSaveTarget(propertyOwner) != null) return true;
            if (ResolveSaveTarget(target) != null) return true;

            var serializedOwner = container?.FindSerializedOwner(target);
            return serializedOwner != null;
        }

        /// <summary>
        /// The <see cref="UnityEngine.Object"/> an editor write on <paramref name="obj"/> lands in,
        /// or null when it is not backed by one.
        /// </summary>
        /// <remarks>
        /// ⚠ The <c>!= null</c> comparisons are Unity's on purpose: a destroyed object is not
        /// somewhere a value can be saved.
        /// </remarks>
        public static UnityEngine.Object ResolveSaveTarget(object obj)
        {
            if (obj is UnityEngine.Object unityObject) return unityObject != null ? unityObject : null;

            // A proxy is a plain C# object, but the values it exposes belong to the object it wraps.
            if (obj is LiveUnityObjectBase proxy)
            {
                var reference = proxy.reference;
                return reference != null ? reference : null;
            }

            return null;
        }

        /// <summary>
        /// Pins <see cref="isEditorSession"/> for the lifetime of the scope. <b>Tests only.</b>
        /// </summary>
        /// <remarks>
        /// Edit mode tests run in the editor, so asking honestly always answers "editor". That leaves
        /// no way to cover what play mode and the built app do — which is the live scene itself.
        /// <para/>
        /// ⚠ Never use from product code. It breaks the premise that the question has one answer.
        /// </remarks>
        public sealed class Override : IDisposable
        {
            private readonly bool? _previous;

            /// <param name="editorSession">What <see cref="isEditorSession"/> reports meanwhile.</param>
            public Override(bool editorSession)
            {
                _previous = _override;
                _override = editorSession;
            }

            public void Dispose() => _override = _previous;
        }
    }
}
