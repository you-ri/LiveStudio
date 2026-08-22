// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Threading;

using NUnit.Framework;
using UnityEngine;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// The editor keeps away from live scenes: what it edits is the .unity scene, saved by the
    /// editor's own Save. These cover which writes that rules out.
    /// </summary>
    /// <remarks>
    /// Only values with nowhere else to land are refused. Refusing more would take away the values
    /// that describe what is happening right now, and refusing less would let the user change
    /// something that quietly disappears at the next launch.
    /// </remarks>
    public class LiveEditorSessionTests
    {
        [LiveClass("SessionGuardComponent")]
        public class SessionGuardComponent : MonoBehaviour
        {
            /// <summary>Saved in the .unity scene by the editor itself.</summary>
            [SerializeField, LiveField]
            public float sceneValue;

            /// <summary>Written by the live scene save, which never runs in an editor session.</summary>
            [SerializeField, LiveField(persistScope = PersistScope.Project)]
            public float projectValue;

            /// <summary>Never saved anywhere, so there is nothing to lose.</summary>
            [LiveField(persistable = false)]
            public float volatileValue;
        }

        /// <summary>A live object that is not a Unity object: no scene and no asset to be saved in.</summary>
        [LiveClass("SessionGuardPlainObject")]
        public class SessionGuardPlainObject
        {
            [LiveField]
            public float sceneValue;

            [LiveField(persistable = false)]
            public float volatileValue;
        }

        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            LiveClass.RegisterFromAttributes<SessionGuardComponent>();
            LiveClass.RegisterFromAttributes<SessionGuardPlainObject>();
            LiveClass.RegisterFromAttributes<LiveGameObject>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        private SessionGuardComponent _CreateComponent(out LiveObjectHandle live)
        {
            var go = new GameObject(nameof(SessionGuardComponent));
            _created.Add(go);
            var component = go.AddComponent<SessionGuardComponent>();
            live = new LiveObjectHandle("guard-component", LiveClass.Find(typeof(SessionGuardComponent)), component);
            return component;
        }

        private static SessionGuardPlainObject _CreatePlain(out LiveObjectHandle live)
        {
            var plain = new SessionGuardPlainObject();
            live = new LiveObjectHandle("guard-plain", LiveClass.Find(typeof(SessionGuardPlainObject)), plain);
            return plain;
        }

        private static bool _IsRejected(LiveObjectHandle live, string member,
            LiveObjectContainer container = null)
        {
            var property = live.FindProperty(member);
            Assert.IsTrue(property.HasValue, $"the test needs '{member}' to resolve");
            return LiveEditorSession.IsWriteRejected(container, live.target, property.Value);
        }

        [Test]
        public void EditorSession_KeepsSceneBackedValuesWritable()
        {
            _CreateComponent(out var live);

            using (new LiveEditorSession.Override(editorSession: true))
            {
                // A component's field is saved by the editor with the scene, which is exactly the
                // arrangement this whole rule is built around. Refusing it would leave the editor
                // unable to edit its own scene from the remote.
                Assert.IsFalse(_IsRejected(live, "sceneValue"));
            }
        }

        [Test]
        public void EditorSession_RefusesProjectScopedValues()
        {
            _CreateComponent(out var live);

            using (new LiveEditorSession.Override(editorSession: true))
            {
                // Project-scoped members ride along with the live scene save. Accepting one here
                // means the user changes it, sees it take effect, and loses it at the next launch.
                Assert.IsTrue(_IsRejected(live, "projectValue"));
            }
        }

        [Test]
        public void EditorSession_RefusesLiveSceneOnlyOwners()
        {
            _CreatePlain(out var live);

            using (new LiveEditorSession.Override(editorSession: true))
            {
                // A plain C# object is in no scene and in no asset; the live scene is its only home.
                Assert.IsTrue(_IsRejected(live, "sceneValue"));

                // ...but a value that was never going to be saved is unaffected. This is what keeps
                // "what is open right now" style members writable in the editor.
                Assert.IsFalse(_IsRejected(live, "volatileValue"));
            }
        }

        private LiveGameObject _CreateProxy(bool withReference, out LiveObjectHandle live)
        {
            GameObject reference = null;
            if (withReference)
            {
                reference = new GameObject("proxy reference");
                _created.Add(reference);
            }

            var proxy = new LiveGameObject(reference);
            live = new LiveObjectHandle("guard-proxy", LiveClass.Find(typeof(LiveGameObject)), proxy);
            return proxy;
        }

        [Test]
        public void EditorSession_KeepsProxiedValuesWritable()
        {
            _CreateProxy(withReference: true, out var live);

            using (new LiveEditorSession.Override(editorSession: true))
            {
                // A proxy is a plain C# object, but "active" is forwarded to the GameObject it wraps,
                // and the editor saves that with the scene. Judging the proxy alone refused every
                // camera, light and transform the remote can reach in an editor session.
                Assert.IsFalse(_IsRejected(live, "active"));
            }
        }

        [Test]
        public void EditorSession_KeepsOwnFieldsWritable_WhenSomethingSerializesTheObject()
        {
            var proxy = _CreateProxy(withReference: false, out var live);

            var hostGameObject = new GameObject("container host");
            _created.Add(hostGameObject);
            var host = hostGameObject.AddComponent<SessionGuardComponent>();
            var container = new LiveObjectContainer(
                "guard", new List<ILiveObject> { proxy }, host);

            using (new LiveEditorSession.Override(editorSession: true))
            {
                // Nothing is wrapped here, so the value can only be saved by whoever serializes the
                // proxy itself — the container's host.
                Assert.IsFalse(_IsRejected(live, "active", container));

                // Same object, but asked without the container: then there is no known home.
                Assert.IsTrue(_IsRejected(live, "active"));
            }
        }

        [Test]
        public void EditorSession_RefusesRuntimeOnlySources()
        {
            var proxy = _CreateProxy(withReference: false, out var live);

            var container = new LiveObjectContainer("guard", new List<ILiveObject>());
            // Binding wrappers are registered under a plain C# owner precisely because they are
            // runtime-only: nothing writes them into a scene, so nothing saves what is put on them.
            container.AddSource(new List<ILiveObject> { proxy }, new object());

            using (new LiveEditorSession.Override(editorSession: true))
            {
                Assert.IsTrue(_IsRejected(live, "active", container));
            }
        }

        [Test]
        public void Playing_RefusesNothing()
        {
            _CreateComponent(out var component);
            _CreatePlain(out var plain);

            using (new LiveEditorSession.Override(editorSession: false))
            {
                // While playing there is a live scene to write to, so every one of them lands.
                Assert.IsFalse(_IsRejected(component, "sceneValue"));
                Assert.IsFalse(_IsRejected(component, "projectValue"));
                Assert.IsFalse(_IsRejected(plain, "sceneValue"));
            }
        }

        [Test]
        public void IsEditorSession_AnswersFromAWorkerThread()
        {
            // ⚠ Request handlers run on worker threads, and every Unity API throws there. Asking
            // Application.isPlaying directly took down /live/status with "get_isPlaying can only be
            // called from the main thread", so the answer has to be a cached value.
            Exception thrown = null;
            bool? answer = null;

            var worker = new Thread(() =>
            {
                try
                {
                    answer = LiveEditorSession.isEditorSession;
                }
                catch (Exception e)
                {
                    thrown = e;
                }
            });
            worker.Start();
            Assert.IsTrue(worker.Join(5000), "the worker thread should finish");

            Assert.IsNull(thrown, $"asking off the main thread threw: {thrown}");
            Assert.IsNotNull(answer);
            // Tests run in the editor without play mode, so the honest answer is also known.
            Assert.IsTrue(answer.Value);
        }

        [Test]
        public void Override_RestoresWhatItFound()
        {
            var before = LiveEditorSession.isEditorSession;

            using (new LiveEditorSession.Override(!before))
            {
                Assert.AreEqual(!before, LiveEditorSession.isEditorSession);

                // Nested scopes have to unwind to the enclosing answer, not to the app's.
                using (new LiveEditorSession.Override(before))
                {
                    Assert.AreEqual(before, LiveEditorSession.isEditorSession);
                }

                Assert.AreEqual(!before, LiveEditorSession.isEditorSession);
            }

            Assert.AreEqual(before, LiveEditorSession.isEditorSession);
        }
    }
}
