// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Lilium.RemoteControl.Reflection;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// The objects a frame carries, beyond the ones the registry holds.
    ///
    /// An exposed scene component is not registered: it is found when someone asks for it and
    /// answers to its type name (<c>/live/object/Screen</c>), and the handle made for it is thrown
    /// away again. That is fine for a request and not fine for a frame -- a lane walking only the
    /// registry never sees such an object at all, and a recording made that way says the screen
    /// never changed rather than saying nothing about it.
    ///
    /// So the roster resolves them the same way the request path does, ahead of time: the type name
    /// is the address, which keeps one way of naming a thing rather than two.
    ///
    /// **Only when there is exactly one.** Two components of a type both answer to the type name,
    /// and a frame that addressed them both by it would have two objects at one address -- the
    /// values of whichever was walked last. An object kind that can exist more than once is
    /// addressed by an id of its own, which means being registered.
    /// </summary>
    public static class LiveObjectRoster
    {
        /// <summary>One exposed scene component, and the name it answers to.</summary>
        public readonly struct Entry
        {
            /// <summary>The address: the exposed type name, which is what REST resolves it by.</summary>
            public readonly string id;

            /// <summary>
            /// The exposed type. The same string as <see cref="id"/> for these -- being the only one
            /// of its type is what gives such an object an address -- but the two say different
            /// things, and an inventory reader is asking about the type.
            /// </summary>
            public readonly string typeName;

            /// <summary>The component itself.</summary>
            public readonly Component target;

            public Entry(string id, string typeName, Component target)
            {
                this.id = id;
                this.typeName = typeName;
                this.target = target;
            }

            /// <summary>False once the object behind it has gone.</summary>
            public bool isAlive => target != null;
        }

        private static readonly List<Entry> _sceneComponents = new List<Entry>();
        private static bool _resolved;
        private static bool _stale;
        private static bool _watchingScenes;

        /// <summary>
        /// Says the scene's population has changed, so the next read walks it again.
        ///
        /// Deferred rather than walked here: what changes the population is a load or an unload,
        /// and both happen while other things are still settling -- walking mid-load would resolve
        /// half a scene and cache it. The walk is one FindObjectsByType per exposed component type,
        /// so it is also not something to do on the spot when a caller may ask several times in a
        /// row.
        ///
        /// Scene loads and unloads mark themselves (see <see cref="_WatchScenes"/>). This is for
        /// everything else that puts an exposed component in the world or takes one out --
        /// instantiating a prefab, loading an asset bundle -- which the engine gives no event for.
        /// </summary>
        public static void MarkStale() => _stale = true;

        /// <summary>
        /// Marks the roster stale whenever a scene is loaded or unloaded.
        ///
        /// Installed once at startup rather than at first use: with domain reloads off, static state
        /// survives entering play mode while the objects it names do not, so the subscription has to
        /// be re-established from a runtime hook. Subscribing is idempotent here, which is what makes
        /// it safe to run either way.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _WatchScenes()
        {
            _sceneComponents.Clear();
            _resolved = false;
            _stale = false;

            if (_watchingScenes) return;
            _watchingScenes = true;

            SceneManager.sceneLoaded += (scene, mode) => MarkStale();
            SceneManager.sceneUnloaded += scene => MarkStale();
        }

        /// <summary>
        /// The exposed scene components a frame should carry, resolved on first use.
        ///
        /// Resolving walks the scene once per exposed component type, which is why it is not done
        /// per frame. <see cref="Refresh"/> is how a caller says the scene has changed under it.
        /// </summary>
        public static IReadOnlyList<Entry> sceneComponents
        {
            get
            {
                if (!_resolved || _stale) Refresh();
                return _sceneComponents;
            }
        }

        /// <summary>
        /// Walks the scene again.
        ///
        /// Called when a lane starts carrying frames, and by whoever knows the scene changed --
        /// a component destroyed with a scene leaves an entry pointing at nothing, and one loaded
        /// with a new scene is not in the list at all.
        /// </summary>
        public static void Refresh()
        {
            _resolved = true;
            _stale = false;
            _sceneComponents.Clear();

            // Asked of the attribute rather than of LiveClass.all: a class is registered on demand
            // and a test assembly (or any assembly loaded late) may not have contributed to that
            // table yet, so a component type nothing had touched would be missing from the frame
            // for the life of the domain. The editor answers this from its type cache.
            foreach (var type in TypeReflectionSystem.FindAllTypesWithAttribute<LiveClassAttribute>())
            {
                if (type == null || type.IsAbstract) continue;
                if (!typeof(Component).IsAssignableFrom(type)) continue;

                var liveClass = LiveClass.Find(type);
                if (liveClass == null || liveClass.isStatic) continue;

                var found = UnityEngine.Object.FindObjectsByType(
                    type, FindObjectsInactive.Include, FindObjectsSortMode.None);

                // None: nothing to carry. More than one: the type name does not say which, and
                // guessing would put two objects at one address.
                if (found == null || found.Length != 1) continue;

                if (!(found[0] is Component component) || component == null) continue;

                // Already registered under an id of its own -- by a request that reached it first,
                // or by a scene restore. The registry walk carries it, and carrying it here as well
                // would put the same object in the frame twice under two names.
                if (LiveObjectRegistry.FindByTarget(component) != null) continue;

                _sceneComponents.Add(new Entry(liveClass.typeName, liveClass.typeName, component));
            }
        }

        /// <summary>
        /// Drops what was resolved, so the next read walks the scene again. For tests, and for a
        /// host tearing a run down.
        /// </summary>
        public static void Clear()
        {
            _sceneComponents.Clear();
            _resolved = false;
            _stale = false;
        }

    }
}
