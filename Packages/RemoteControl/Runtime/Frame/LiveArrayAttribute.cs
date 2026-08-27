// Copyright (c) You-Ri, 2026
using System;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Says what a fixed buffer really holds, for the tools that read these structs back.
    ///
    /// A fixed buffer declares an element type and a length and nothing else, so
    /// <c>fixed byte[880]</c> is 880 bytes as far as the type system is concerned. That it is 55
    /// quaternions in bone order is knowledge held by the code that reads it -- and a viewer, a
    /// recording or a diff has no way to recover it. This is where that knowledge is written down,
    /// next to the data rather than in whatever happens to be reading it.
    ///
    /// Costs nothing at runtime: an attribute changes no layout and is read only by editor tooling.
    ///
    /// <code>
    /// [LiveArray(typeof(Quaternion), labels = typeof(HumanBodyBones), pairedWith = nameof(bonePresences))]
    /// public fixed byte boneRotations[(int)HumanBodyBones.LastBone * 16];
    ///
    /// [LiveArray(labels = typeof(ARKitBlendShapeLocation))]   // element type already known
    /// public fixed float weights[(int)ARKitBlendShapeLocation.Max];
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class LiveArrayAttribute : Attribute
    {
        /// <summary>Labels the elements without changing how they are read.</summary>
        public LiveArrayAttribute()
        {
        }

        /// <summary>
        /// The element type the bytes really are. Only needed when the declared one is not it --
        /// which in practice means a byte buffer standing in for something else.
        /// </summary>
        public LiveArrayAttribute(Type elementType)
        {
            this.elementType = elementType;
        }

        /// <summary>What each element is, or null to take the buffer's declared element type.</summary>
        public Type elementType { get; }

        /// <summary>
        /// Enum whose names label the elements, in order. Null numbers them instead.
        ///
        /// A name is worth more than an index here: "LeftUpperLeg" is the thing being looked for,
        /// and "[7]" makes the reader count.
        /// </summary>
        public Type labels { get; set; }

        /// <summary>
        /// Field holding one value per element, shown beside each of them. Null for none.
        ///
        /// For pairs that must not be read apart -- a rotation whose presence says whether anything
        /// is driving it. Shown separately, the stale rotations of undriven bones look like data.
        /// </summary>
        public string pairedWith { get; set; }
    }
}
