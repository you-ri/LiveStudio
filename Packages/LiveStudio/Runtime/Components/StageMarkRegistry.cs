// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Static registry of all currently enabled <see cref="StageMark"/> instances.
    ///
    /// <see cref="StageMark"/> registers/unregisters itself on enable/disable, so the set stays
    /// in sync with additive scene bundles (<c>*.scene.lsb</c>) being loaded and unloaded.
    /// <see cref="onChanged"/> fires whenever the set changes so consumers (e.g. the remote-app
    /// dropdown in <see cref="AvatarStageController"/>) can refresh their candidate list.
    /// </summary>
    public static class StageMarkRegistry
    {
        private static readonly List<StageMark> _marks = new List<StageMark>();

        /// <summary>All currently registered marks.</summary>
        public static IReadOnlyList<StageMark> marks => _marks;

        /// <summary>Raised when a mark is registered or unregistered.</summary>
        public static event Action onChanged;

        // Reset static state at runtime start so this works with Domain Reload disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _Initialize()
        {
            _marks.Clear();
            onChanged = null;
        }

        public static void Register(StageMark mark)
        {
            if (mark == null || _marks.Contains(mark)) return;
            _marks.Add(mark);
            onChanged?.Invoke();
        }

        public static void Unregister(StageMark mark)
        {
            if (mark == null) return;
            if (_marks.Remove(mark))
            {
                onChanged?.Invoke();
            }
        }
    }
}
