// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Static service locator providing access to multiple registered subjects of type <typeparamref name="T"/>.
    /// Used as a loosely-coupled inter-class messaging API.
    /// </summary>
    public static class Service<T> where T : class
    {
        public static List<T> subjects = new List<T>();

        public static void Register(T obj)
        {
            if (subjects.Contains(obj))
            {
                return;
            }
            subjects.Add(obj);
        }

        public static void Unregister(T obj)
        {
            subjects.Remove(obj);
        }

        public static void Initialize()
        {
            subjects = new List<T>();
        }
    }

    /// <summary>
    /// Static service locator providing access to a single registered subject of type <typeparamref name="T"/>.
    /// </summary>
    public static class SingletonService<T> where T : class
    {
        public static T subject = null;

        public static void Register(T obj)
        {
            Debug.Assert(obj != null, $"{typeof(T)} already has a subject. Unsubscribe the previous subject before subscribing a new one.");
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
        public static Dictionary<string, List<T>> subjects = new Dictionary<string, List<T>>();

        public static event System.Action<string, T> onRegistered;
        public static event System.Action<string, T> onUnregistered;

        public static void Register(string id, T obj)
        {
            if (!subjects.ContainsKey(id))
            {
                subjects[id] = new List<T>();
            }
            subjects[id].Add(obj);
            onRegistered?.Invoke(id, obj);
        }

        public static void Unregister(string id, T obj)
        {
            if (subjects.ContainsKey(id))
            {
                subjects[id].Remove(obj);
            }
            onUnregistered?.Invoke(id, obj);
        }

        public static void Initialize()
        {
            subjects = new Dictionary<string, List<T>>();
            onRegistered = null;
            onUnregistered = null;
        }

        public static T Select(string id)
        {
            if (subjects.ContainsKey(id) && subjects[id].Count > 0)
            {
                return subjects[id][0];
            }
            return null;
        }
    }
}
