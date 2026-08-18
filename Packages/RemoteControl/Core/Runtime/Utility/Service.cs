// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Static service locator providing access to multiple registered subjects of type <typeparamref name="T"/>.
    /// Callers look up registered instances to invoke work on them or read their state.
    /// For broadcasting to all subjects, use <see cref="ForEach"/> which is safe against
    /// Register/Unregister from within the callback.
    /// </summary>
    public static class Service<T> where T : class
    {
        static readonly List<T> _subjects = new List<T>();

        /// <summary>
        /// Read-only view of the registered subjects. Do not cache; the underlying
        /// collection changes as subjects register and unregister.
        /// </summary>
        public static IReadOnlyList<T> subjects => _subjects;

        public static void Register(T obj)
        {
            if (_subjects.Contains(obj))
            {
                return;
            }
            _subjects.Add(obj);
        }

        public static void Unregister(T obj)
        {
            _subjects.Remove(obj);
        }

        public static void Initialize()
        {
            _subjects.Clear();
        }

        /// <summary>
        /// Invokes <paramref name="action"/> on each registered subject. Iterates over a
        /// pooled snapshot, so subjects may safely Register/Unregister (including themselves)
        /// from within the callback without corrupting the iteration.
        /// </summary>
        public static void ForEach(Action<T> action)
        {
            if (_subjects.Count == 0)
            {
                return;
            }
            using (ListPool<T>.Get(out var snapshot))
            {
                snapshot.AddRange(_subjects);
                for (int i = 0; i < snapshot.Count; i++)
                {
                    action(snapshot[i]);
                }
            }
        }
    }

    /// <summary>
    /// Static service locator providing access to a single registered subject of type <typeparamref name="T"/>.
    /// </summary>
    public static class SingletonService<T> where T : class
    {
        public static T subject { get; private set; }

        public static void Register(T obj)
        {
            Debug.Assert(subject == null || subject == obj,
                $"[RemoteControl] {typeof(T)} already has a subject. Unregister the previous subject before registering a new one.");
            subject = obj;
        }

        public static void Unregister(T obj)
        {
            if (subject == obj)
            {
                subject = null;
            }
        }

        public static void Initialize()
        {
            subject = null;
        }
    }

    /// <summary>
    /// Static service locator that groups subjects of type <typeparamref name="T"/> by an id string,
    /// allowing callers to <see cref="Select"/> one of multiple registered implementations.
    /// </summary>
    public static class SelectableService<T> where T : class
    {
        static readonly Dictionary<string, List<T>> _subjects = new Dictionary<string, List<T>>();

        public static event Action<string, T> onRegistered;
        public static event Action<string, T> onUnregistered;

        public static void Register(string id, T obj)
        {
            if (!_subjects.TryGetValue(id, out var list))
            {
                list = new List<T>();
                _subjects[id] = list;
            }
            list.Add(obj);
            onRegistered?.Invoke(id, obj);
        }

        public static void Unregister(string id, T obj)
        {
            if (_subjects.TryGetValue(id, out var list))
            {
                list.Remove(obj);
            }
            onUnregistered?.Invoke(id, obj);
        }

        public static void Initialize()
        {
            _subjects.Clear();
            onRegistered = null;
            onUnregistered = null;
        }

        public static T Select(string id)
        {
            if (_subjects.TryGetValue(id, out var list) && list.Count > 0)
            {
                return list[0];
            }
            return null;
        }
    }
}
