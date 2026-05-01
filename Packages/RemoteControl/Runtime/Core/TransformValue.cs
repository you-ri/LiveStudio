// Copyright (c) You-Ri, 2026
using System;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// position / rotation / scale を単一の値として保持する構造体。
    /// UnityEngine.Transform (Component) は値として扱えないため、
    /// ExposedProperty で Transform 相当のデータをやり取りする用途で用いる。
    /// </summary>
    [Serializable]
    public struct TransformValue : IEquatable<TransformValue>
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;

        public static readonly TransformValue identity =
            new TransformValue(Vector3.zero, Quaternion.identity, Vector3.one);

        public TransformValue(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
        }

        /// <summary>
        /// Transform からローカル TRS を取得する。
        /// </summary>
        public static TransformValue FromTransform(Transform t)
        {
            if (t == null) return identity;
            return new TransformValue(t.localPosition, t.localRotation, t.localScale);
        }

        /// <summary>
        /// Transform のローカル TRS を書き換える。
        /// </summary>
        public void ApplyTo(Transform t)
        {
            if (t == null) return;
            t.localPosition = position;
            t.localRotation = rotation;
            t.localScale = scale;
        }

        public bool Equals(TransformValue other)
            => position == other.position
            && rotation == other.rotation
            && scale == other.scale;

        public override bool Equals(object obj)
            => obj is TransformValue v && Equals(v);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = position.GetHashCode();
                hash = (hash * 397) ^ rotation.GetHashCode();
                hash = (hash * 397) ^ scale.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(TransformValue a, TransformValue b) => a.Equals(b);
        public static bool operator !=(TransformValue a, TransformValue b) => !a.Equals(b);

        public override string ToString()
            => $"TransformValue(pos={position}, rot={rotation}, scale={scale})";
    }
}
