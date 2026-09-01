// Copyright (c) You-Ri, 2026
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// How one element of a GameObject's exposed component list is addressed.
    ///
    /// By the component's exposed type name ("components[Light]") rather than by its position
    /// ("components[0]"), because the position is whatever order the components happen to sit in:
    /// re-export a bundle, or reorder them in the inspector, and the same index is a different
    /// component -- silently, which is the part that makes it dangerous. The type name survives
    /// both.
    ///
    /// One rule, asked in one place, because two consumers address the same component and a
    /// recording is read against a scene: the live scene writes the key into a saved document
    /// (<see cref="IFileScopedResolver"/>), and the frame writes it into every keyframe. Working it
    /// out separately in each is how they came to disagree -- the save side named the element and
    /// the frame side went on counting.
    /// </summary>
    public static class ComponentElementKey
    {
        /// <summary>The exposed property name of the GameObject wrapper's component list.</summary>
        public const string kMemberName = "components";

        /// <summary>
        /// The key <paramref name="target"/> is addressed by, or null when there is no exposed type
        /// to name it after. Null rather than a fallback: what a caller does without a name is its
        /// own business (the live scene leaves the path alone, the frame counts), and inventing one
        /// here would put a name in a document that nothing can resolve.
        /// </summary>
        public static string Of(Object target)
        {
            if (!(target is Component)) return null;

            var liveClass = LiveClass.Find(target.GetType());
            if (liveClass == null || string.IsNullOrEmpty(liveClass.typeName)) return null;

            return liveClass.typeName;
        }
    }
}
