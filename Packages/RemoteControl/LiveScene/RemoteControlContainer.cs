// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.RemoteControl.LiveScene
{
    /// <summary>
    /// Holds a list of remote-controllable <see cref="IExposedObject"/> instances for the scene it
    /// lives in, and registers itself in a static registry so a <see cref="RemoteControlBehaviour"/>
    /// (which owns the single HTTP server) can discover and merge those objects.
    /// </summary>
    /// <remarks>
    /// This component carries remote control across scene boundaries: place it in an additively
    /// loaded world (e.g. a .scene.lsb bundle) and its objects become listable, resolvable,
    /// saveable and operable through the host behaviour's server. It owns no server of its own.
    /// </remarks>
    [DefaultExecutionOrder(-32760)]
    [ExecuteAlways]
    public class RemoteControlContainer : MonoBehaviour
    {
        [SerializeReference, Select]
        [ExposedField(persistable = false)]
        public List<IExposedObject> _objects = new List<IExposedObject>();

        public IReadOnlyList<IExposedObject> objects => _objects;

        // --- Static registry (discovery for the host RemoteControlBehaviour) ---

        private static readonly List<RemoteControlContainer> _all = new List<RemoteControlContainer>();

        /// <summary>All containers currently enabled, in registration order.</summary>
        public static IReadOnlyList<RemoteControlContainer> all => _all;

        /// <summary>Raised after a container has been added to <see cref="all"/>.</summary>
        public static event Action<RemoteControlContainer> onRegistered;

        /// <summary>Raised after a container has been removed from <see cref="all"/>.</summary>
        public static event Action<RemoteControlContainer> onUnregistered;

        // Reset static state at runtime startup so disabling Domain Reload does not leak the
        // previous play session's containers or host subscriptions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _ClearStatics()
        {
            _all.Clear();
            onRegistered = null;
            onUnregistered = null;
        }

        protected virtual void OnEnable()
        {
            if (_all.Contains(this)) return;
            _all.Add(this);
            onRegistered?.Invoke(this);
        }

        protected virtual void OnDisable()
        {
            if (_all.Remove(this))
                onUnregistered?.Invoke(this);
        }
    }
}
