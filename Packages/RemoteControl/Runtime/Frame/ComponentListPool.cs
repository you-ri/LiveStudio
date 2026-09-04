// Copyright (c) You-Ri, 2026
using System.Collections.Generic;

using UnityEngine;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Lists for <c>GetComponents</c> during a frame walk, lent out one per level.
    ///
    /// A single shared list is not enough because the walk re-enters: a component's members can
    /// hold another exposed GameObject, and carrying what that one holds refills the list the outer
    /// loop is still reading. The outer loop then walks the inner object's components under the
    /// outer index and stops at its length -- the rest of the outer object's components drop out of
    /// the frame with nothing reporting it.
    ///
    /// A pool rather than one list per nesting level, so that the depth arithmetic of the two walks
    /// is not what keeps them apart.
    /// </summary>
    internal static class ComponentListPool
    {
        // Grows to the depth actually walked and stops. GetComponents hands back a fresh array
        // otherwise, once per exposed GameObject per frame, for the whole of a take.
        private static readonly Stack<List<Component>> _free = new Stack<List<Component>>();

        public static List<Component> Rent()
            => _free.Count > 0 ? _free.Pop() : new List<Component>();

        /// <summary>
        /// Hands one back, emptied.
        ///
        /// Emptied rather than left as it is: a reference kept in a pooled list holds a destroyed
        /// component's managed side alive until that list is lent out again.
        /// </summary>
        public static void Return(List<Component> list)
        {
            if (list == null) return;

            list.Clear();
            _free.Push(list);
        }
    }
}
