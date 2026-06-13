// Copyright (c) You-Ri, 2026

using System;
using System.Reflection;

using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.Editor
{
    /// <summary>
    /// Temporarily disables URP's "Strip Unused Variants" for the duration of an AssetBundle build.
    /// Left enabled, it drops <c>shader_feature_local</c> variants (e.g. lilToon) as "unused",
    /// which makes bundled materials render magenta when the bundle is loaded later.
    /// <see cref="Dispose"/> always restores the original value.
    ///
    /// URP is reached through reflection so this works in any project without a hard dependency;
    /// in non-URP projects or when the setting cannot be resolved it is a no-op.
    /// </summary>
    internal readonly struct UrpShaderStrippingOverride : IDisposable
    {
        private const string kPropertyPath = "m_URPShaderStrippingSetting.m_StripUnusedVariants";

        private readonly SerializedObject _serializedObject;
        private readonly int _originalValue;
        private readonly bool _engaged;

        private UrpShaderStrippingOverride(SerializedObject serializedObject, int originalValue)
        {
            _serializedObject = serializedObject;
            _originalValue = originalValue;
            _engaged = true;
        }

        /// <summary>
        /// Returns a scope that has set URP's StripUnusedVariants to 0. When URP is absent, the
        /// setting cannot be resolved, or it is already 0, returns a no-op scope.
        /// </summary>
        public static UrpShaderStrippingOverride DisableUnusedVariantStripping()
        {
            var instance = _GetUrpGlobalSettings();
            if (instance == null) return default; // Non-URP project, etc.

            var serializedObject = new SerializedObject(instance);
            var property = serializedObject.FindProperty(kPropertyPath);
            if (property == null)
            {
                Debug.LogError(
                    $"[LiveStudio] URP global settings found but '{kPropertyPath}' was not resolved (URP version mismatch?); shader variant stripping not overridden.");
                return default;
            }

            var original = property.intValue;
            if (original == 0) return default; // Already disabled; leave it alone.

            property.intValue = 0;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            return new UrpShaderStrippingOverride(serializedObject, original);
        }

        public void Dispose()
        {
            if (!_engaged) return;

            var property = _serializedObject.FindProperty(kPropertyPath);
            if (property == null) return;

            property.intValue = _originalValue;
            _serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static UnityEngine.Object _GetUrpGlobalSettings()
        {
            var type = Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings, Unity.RenderPipelines.Universal.Runtime");
            if (type == null) return null;

            var instanceProperty = type.GetProperty(
                "instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
            return instanceProperty?.GetValue(null) as UnityEngine.Object;
        }
    }
}
