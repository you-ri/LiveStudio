// Copyright (c) You-Ri, 2026

#if !UNITY_6000_3_OR_NEWER

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>
    /// Injects a native <see cref="VisualElement"/> into the main toolbar's left-aligned zone. Unity
    /// 2021.2 - 6.2 expose no public toolbar-extension API, so the toolbar <see cref="VisualElement"/>
    /// is reached through reflection over the internal <c>UnityEditor.Toolbar</c> view. Unity 6.3+
    /// has <c>MainToolbarElement</c> and never compiles this file.
    /// </summary>
    [InitializeOnLoad]
    public static class MainToolbarHook
    {
        struct Entry
        {
            public int order;
            public VisualElement element;
        }

        // Elements waiting for the toolbar zone to exist, and the ones already in it. Both are kept
        // sorted by order so the buttons keep the same left-to-right arrangement whatever order the
        // owning assemblies happen to register in.
        static readonly List<Entry> _pending = new List<Entry>();
        static readonly List<Entry> _hosted = new List<Entry>();

        static readonly System.Type _toolbarType =
            typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");
        static readonly System.Type _guiViewType =
            typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GUIView");
        static readonly System.Type _windowBackendType =
            typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.IWindowBackend");

        static readonly System.Reflection.PropertyInfo _windowBackendProperty =
            _guiViewType?.GetProperty("windowBackend",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        static readonly System.Reflection.PropertyInfo _visualTreeProperty =
            _windowBackendType?.GetProperty("visualTree",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        static VisualElement _zone;

        static MainToolbarHook()
        {
            EditorApplication.update -= _OnUpdate;
            EditorApplication.update += _OnUpdate;
        }

        /// <summary>
        /// Add an element to the toolbar's left zone, deferring until the zone is available.
        /// Elements are arranged by ascending <paramref name="order"/> among themselves; the editor's
        /// own toolbar contents keep their place.
        /// </summary>
        public static void AddLeftElement(VisualElement element, int order = 0)
        {
            var entry = new Entry { order = order, element = element };
            if (_zone == null)
            {
                _Insert(_pending, entry);
                return;
            }
            _Host(entry);
        }

        // Inserts into a list kept in ascending order, after the entries sharing the same order so
        // equal-order elements stay in registration order.
        static void _Insert(List<Entry> list, Entry entry)
        {
            int index = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].order <= entry.order) continue;
                index = i;
                break;
            }
            list.Insert(index, entry);
        }

        static void _Host(Entry entry)
        {
            // The zone also holds the editor's own elements, so the insert position is taken from the
            // first already-hosted element that must stay to the right of this one; falling back to
            // the end of the zone keeps our elements after the editor's.
            int index = _zone.childCount;
            for (int i = 0; i < _hosted.Count; i++)
            {
                if (_hosted[i].order <= entry.order) continue;
                int at = _zone.IndexOf(_hosted[i].element);
                if (at < 0) continue;
                index = at;
                break;
            }

            _zone.Insert(index, entry.element);
            _Insert(_hosted, entry);
        }

        static void _OnUpdate()
        {
            if (_zone != null) return;
            if (_toolbarType == null || _windowBackendProperty == null || _visualTreeProperty == null) return;

            var toolbars = Resources.FindObjectsOfTypeAll(_toolbarType);
            if (toolbars.Length == 0) return;

            var toolbar = (ScriptableObject)toolbars[0];
            var backend = _windowBackendProperty.GetValue(toolbar);
            if (backend == null) return;
            var visualTree = _visualTreeProperty.GetValue(backend, null) as VisualElement;
            var zone = visualTree?.Q("ToolbarZoneLeftAlign");
            if (zone == null) return;

            _zone = zone;
            for (int i = 0; i < _pending.Count; i++)
            {
                _Host(_pending[i]);
            }
            _pending.Clear();
        }
    }
}

#endif
