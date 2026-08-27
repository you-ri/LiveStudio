// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Lilium.RemoteControl.Editor.LiveDataViewer;

namespace Lilium.LiveStudio.Editor
{
    /// <summary>
    /// Reads a captured pose for the LiveData viewer.
    ///
    /// Needed because the type cannot say what most of itself is. A pose stores its rotations as
    /// <c>fixed byte[880]</c> and its cameras as <c>fixed byte[88]</c>, and reflection can only
    /// report "880 bytes" -- that they are 55 quaternions in <see cref="HumanBodyBones"/> order, and
    /// two cameras, is knowledge held by the code that reads them, not by the type. So it is said
    /// here, next to that code, rather than guessed at by the viewer.
    ///
    /// Lives in this package's editor assembly and registers upward. The viewer sits underneath and
    /// must not know about what is built on it.
    /// </summary>
    [InitializeOnLoad]
    internal static class AvatarAnimationDataPresenter
    {
        /// <summary>Bones written out. All of them: a missing one is what a reader would be looking for.</summary>
        private static readonly int kBoneCount = (int)HumanBodyBones.LastBone;

        static AvatarAnimationDataPresenter()
        {
            LiveDataValuePresenters.Register(typeof(AvatarAnimationData), _Present);
        }

        private static void _Present(byte[] value, int length, List<LiveDataValueRow> rows)
        {
            if (value == null) return;

            var poseOffset = _OffsetOf(typeof(AvatarAnimationData), "pose");
            var expressionOffset = _OffsetOf(typeof(AvatarAnimationData), "expression");
            var camerasOffset = _OffsetOf(typeof(AvatarAnimationData), "cameras");
            var framesOffset = _OffsetOf(typeof(AvatarAnimationData), "frames");

            _PresentRoot(value, length, rows);

            rows.Add(new LiveDataValueRow("pose", string.Empty));
            _PresentPose(value, length, poseOffset, rows);

            rows.Add(new LiveDataValueRow("expression", string.Empty));
            _PresentExpression(value, length, expressionOffset, rows);

            rows.Add(new LiveDataValueRow("cameras", string.Empty));
            _PresentCameras(value, length, camerasOffset, rows);

            rows.Add(new LiveDataValueRow("frames", _Long(value, length, framesOffset)));
        }

        private static void _PresentRoot(byte[] value, int length, List<LiveDataValueRow> rows)
        {
            var at = _OffsetOf(typeof(AvatarAnimationData), "root");

            rows.Add(new LiveDataValueRow("root", string.Empty));
            rows.Add(new LiveDataValueRow("valid",
                _Byte(value, length, at + _OffsetOf(typeof(AvatarRootData), "valid")), 1));
            rows.Add(new LiveDataValueRow("position",
                _Vector3(value, length, at + _OffsetOf(typeof(AvatarRootData), "position")), 1));
            rows.Add(new LiveDataValueRow("rotation",
                _Quaternion(value, length, at + _OffsetOf(typeof(AvatarRootData), "rotation")), 1));
            rows.Add(new LiveDataValueRow("scale",
                _Vector3(value, length, at + _OffsetOf(typeof(AvatarRootData), "scale")), 1));
        }

        private static void _PresentPose(byte[] value, int length, int poseOffset,
            List<LiveDataValueRow> rows)
        {
            var hips = poseOffset + _OffsetOf(typeof(HumanoidPoseData), "hipPosition");
            rows.Add(new LiveDataValueRow("hipPosition", _Vector3(value, length, hips), 1));

            var rotations = poseOffset + _OffsetOf(typeof(HumanoidPoseData), "boneRotations");
            var presences = poseOffset + _OffsetOf(typeof(HumanoidPoseData), "bonePresences");

            // A bone with no presence is not being driven, and its rotation is whatever was left
            // there. Shown together so the two are never read apart.
            for (int i = 0; i < kBoneCount; i++)
            {
                var rotation = _Quaternion(value, length, rotations + i * 16);
                var presence = _Float(value, length, presences + i * 4);
                if (rotation.Length == 0) break;

                rows.Add(new LiveDataValueRow(((HumanBodyBones)i).ToString(),
                    $"{rotation}   ({presence})", 1));
            }
        }

        private static void _PresentExpression(byte[] value, int length, int expressionOffset,
            List<LiveDataValueRow> rows)
        {
            var weights = expressionOffset + _OffsetOf(typeof(ARKitWeightData), "weights");
            var count = (int)ARKitBlendShapeLocation.Max;

            for (int i = 0; i < count; i++)
            {
                var weight = _Float(value, length, weights + i * 4);
                if (weight.Length == 0) break;

                rows.Add(new LiveDataValueRow(((ARKitBlendShapeLocation)i).ToString(), weight, 1));
            }
        }

        private static void _PresentCameras(byte[] value, int length, int camerasOffset,
            List<LiveDataValueRow> rows)
        {
            var count = AvatarAnimationData.kCameraChannelCount;

            for (int i = 0; i < count; i++)
            {
                var at = camerasOffset + i * CameraData.Size;
                if (at + CameraData.Size > length) break;

                rows.Add(new LiveDataValueRow($"[{i}]", string.Empty, 1));
                rows.Add(new LiveDataValueRow("position", _Vector3(value, length, at), 2));
                rows.Add(new LiveDataValueRow("rotation", _Quaternion(value, length, at + 12), 2));
                rows.Add(new LiveDataValueRow("fieldOfView", _Float(value, length, at + 28), 2));
                rows.Add(new LiveDataValueRow("nearClipPlane", _Float(value, length, at + 32), 2));
                rows.Add(new LiveDataValueRow("farClipPlane", _Float(value, length, at + 36), 2));
                rows.Add(new LiveDataValueRow("aspect", _Float(value, length, at + 40), 2));
            }
        }

        // Taken from the type rather than written down, so a field moving does not silently shift
        // every reading after it.
        private static int _OffsetOf(Type type, string field)
        {
            var info = type.GetField(field,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

            return info == null ? -1 : (int)UnsafeUtility.GetFieldOffset(info);
        }

        private static bool _Fits(int offset, int size, int length)
            => offset >= 0 && offset + size <= length;

        private static string _Float(byte[] value, int length, int at)
            => _Fits(at, 4, length) ? BitConverter.ToSingle(value, at).ToString("0.#####") : string.Empty;

        private static string _Long(byte[] value, int length, int at)
            => _Fits(at, 8, length) ? BitConverter.ToInt64(value, at).ToString() : string.Empty;

        private static string _Byte(byte[] value, int length, int at)
            => _Fits(at, 1, length) ? value[at].ToString() : string.Empty;

        private static string _Vector3(byte[] value, int length, int at)
            => _Fits(at, 12, length)
                ? $"{BitConverter.ToSingle(value, at):0.###}, {BitConverter.ToSingle(value, at + 4):0.###}, " +
                  $"{BitConverter.ToSingle(value, at + 8):0.###}"
                : string.Empty;

        private static string _Quaternion(byte[] value, int length, int at)
            => _Fits(at, 16, length)
                ? $"{BitConverter.ToSingle(value, at):0.###}, {BitConverter.ToSingle(value, at + 4):0.###}, " +
                  $"{BitConverter.ToSingle(value, at + 8):0.###}, {BitConverter.ToSingle(value, at + 12):0.###}"
                : string.Empty;
    }
}
