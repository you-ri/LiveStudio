// Copyright (c) You-Ri, 2026
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// AssetBundle ビルド中だけ URP の StripUnusedVariants を一時的に無効化するスコープ。
    /// 有効のままだと lilToon 等の shader_feature_local バリアントが「未使用」と判定されて削られ、
    /// .lsavatar ロード時に紫(マゼンタ)になる。<see cref="Dispose"/> で元の値へ必ず戻す。
    ///
    /// パッケージとして任意プロジェクトに導入しても機能するよう、URP をハード参照せず reflection で扱う。
    /// 非 URP プロジェクトや解決不可時は no-op。
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
        /// URP の StripUnusedVariants を 0 に設定したスコープを返す。
        /// URP 未使用・解決不可・既に 0 の場合は何もしない no-op スコープ。
        /// </summary>
        public static UrpShaderStrippingOverride DisableUnusedVariantStripping()
        {
            var instance = _GetUrpGlobalSettings();
            if (instance == null) return default; // 非 URP プロジェクト等

            var serializedObject = new SerializedObject(instance);
            var property = serializedObject.FindProperty(kPropertyPath);
            if (property == null)
            {
                VRChatAvatarTransferLog.Error(
                    $"URP global settings found but '{kPropertyPath}' was not resolved (URP version mismatch?); shader variant stripping not overridden.");
                return default;
            }

            var original = property.intValue;
            if (original == 0) return default; // 既に無効。触らない。

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
