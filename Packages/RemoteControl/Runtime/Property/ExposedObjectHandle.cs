// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using UnityEngine;
using Lilium.RemoteControl.Reflection;



namespace Lilium.RemoteControl
{
    /// <summary>
    /// 実オブジェクト1個 (targetType / target / id) を束ねる軽量な値ハンドル。
    /// 状態 (デフォルト値・dirty) は <see cref="ExposedObjectDefaultRegistry"/> が target 参照キーで保持するため、
    /// このハンドル自体は不変な readonly struct として使い捨てできる。
    /// </summary>
    public readonly struct ExposedObjectHandle : IEquatable<ExposedObjectHandle>
    {
        public readonly ExposedClass targetType;

        public readonly string id;

        public bool hasId => !string.IsNullOrEmpty(id);

        public readonly object target;

        public string name => target as UnityEngine.Object != null ? ((UnityEngine.Object)target).name : target as IExposedObject != null ? ((IExposedObject)target).name : id;

        public string targetTypeName => targetType?.typeName ?? null;

        public ExposedPropertyType[] propertyTypes => targetType?.propertyTypes ?? new ExposedPropertyType[0];

        public bool isValid => _IsAlive(target) || (targetType != null && targetType.isStatic);

        private static bool _IsAlive(object obj)
        {
            if (obj == null) return false;
            if (obj is UnityEngine.Object unityObj) return unityObj != null;
            return true;
        }

        public bool isDirty => ExposedObjectDefaultRegistry.IsDirty(this, DefaultExposedObjectResolver.Instance);

        public ExposedObjectHandle(string id, ExposedClass type, object target)
        {
            Debug.Assert(type != null, "ExposedClass type cannot be null");

            if (target == null && type != null && !type.isStatic)
            {
                Debug.LogWarning($"[RemoteControl] Creating ExposedObjectHandle with null target for non-static type:{type.typeName} id:{id}");
            }

            this.targetType = type;
            this.target = target;
            this.id = id;

            // レジストリに登録 (struct のコピーを格納する。id/target は不変なので同値判定で解決できる)
            ExposedObjectRegistry.Register(this);

            // デフォルト値を自動キャプチャ（dirty検出のベースライン）
            // インスタンス型: ターゲットの型がExposedClassの型と互換性がある場合のみ実行
            // static型: target=nullだがstaticプロパティを直接読み取れるため実行
            //
            // EnsureDefaultsCaptured（未登録なら捕捉、既存があれば保持）を使う。無条件の
            // SetDefault だと、既にこの target 用の baseline が存在する（例: ライブシーン復元の
            // ApplyPendingEntry が未登録ハンドルで捕捉した「上書き前の既定値」）ケースで、生成時の
            // 現在値（＝適用済みの上書き値）を baseline に焼き込んでしまい、直後の
            // AssetStateSnapshot.CaptureDefaults(=preserving 再ベースライン) が差分を検出できず
            // ライブシーン保存で上書きが失われる。Unregister は baseline を Remove するので、
            // 正規の register→unregister→register サイクルでは従来どおり再捕捉される。
            if (type != null && (type.isStatic || (target != null && type.type != null && type.type.IsInstanceOfType(target))))
            {
                ExposedObjectDefaultRegistry.EnsureDefaultsCaptured(this, DefaultExposedObjectResolver.Instance);
            }
        }

        /// <summary>
        /// レジストリに登録しないExposedObjectを生成する。
        /// プロパティ走査やAPI応答など、一時的なコンテキストとして使用する。
        /// </summary>
        internal static ExposedObjectHandle CreateUnregistered(ExposedClass type, object target)
        {
            Debug.Assert(type != null, "ExposedClass type cannot be null");
            return new ExposedObjectHandle(type, target);
        }

        private ExposedObjectHandle(ExposedClass type, object target)
        {
            this.targetType = type;
            this.target = target;
            this.id = null;
        }

        // 値だけを設定する内部ctor（登録/デフォルトキャプチャを行わない）。引数順を変えて公開ctorとシグネチャを分ける。
        private ExposedObjectHandle(ExposedClass type, object target, string id)
        {
            this.targetType = type;
            this.target = target;
            this.id = id;
        }

        /// <summary>
        /// id だけを差し替えた新しいハンドルを返す（副作用なし）。Registry の再キー専用。
        /// </summary>
        internal ExposedObjectHandle WithId(string newId) => new ExposedObjectHandle(targetType, target, newId);

        public bool ResolveReferences(IExposedPropertyTable resolver)
        {
            return isValid;
        }

        public ExposedProperty? GetProperty(ReadOnlySpan<char> name)
        {
            var propertyType = targetType?.FindProperty(name.ToString());
            if (propertyType != null)
            {
                return new ExposedProperty(propertyType, this, target);
            }
            return null;
        }

        public ExposedProperty? FindProperty(string path)
        {
            if (path == null) throw new System.ArgumentNullException(nameof(path));

            ExposedProperty? property = null;
            foreach (var segment in PropertyPathParser.Parse(path))
            {
                if (property == null)
                {
                    // ExposedObjectHandle.GetProperty は ReadOnlySpan<char> を受け取る
                    property = GetProperty(segment.name);
                }
                else
                {
                    if (segment.isIndexed)
                    {
                        property = property?.GetPropertyIndex(segment.index);
                    }
                    else
                    {
                        // ExposedProperty.GetProperty は string を受け取る
                        property = property?.GetProperty(segment.name.ToString());
                    }
                }

                if (property == null)
                {
                    return null;
                }
            }

            return property;
        }

        public bool TryFindProperty(ReadOnlySpan<char> name, out ExposedProperty property)
        {
            var propertyOrNull = GetProperty(name);
            property = propertyOrNull ?? default;
            return propertyOrNull != null;
        }

        public ExposedFunctionType GetFunction(string name)
        {
            return targetType?.FindFunction(name);
        }

        public object InvokeFunction(string name, object[] args)
        {
            var function = GetFunction(name);
            if (function == null || !function.isValid)
            {
                Debug.LogError($"[RemoteControl] Function '{name}' not found on type '{targetTypeName}'");
                return null;
            }

            try
            {
                // staticメソッドの場合はnullを渡す
                return function.Invoke(function.isStatic ? null : target, args);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RemoteControl] Failed to invoke function '{name}': {ex.Message}");
                return null;
            }
        }

        // --- Dirty追跡（ExposedObjectDefaultRegistryに委譲） ---

        public bool IsPropertyDirty(string propertyPath)
        {
            // ExposedPropertyRef は参照先の dirty 状態を見る
            var property = FindProperty(propertyPath);
            if (property.HasValue && property.Value.type != null && property.Value.type.isExposedPropertyReference)
            {
                return property.Value.isDirty;
            }
            return ExposedObjectDefaultRegistry.IsPropertyDirty(this, propertyPath, DefaultExposedObjectResolver.Instance);
        }

        /// <summary>
        /// 指定パスまたはその子プロパティがdirtyかチェック
        /// </summary>
        public bool HasDirtyChildProperty(string propertyPath)
        {
            return ExposedObjectDefaultRegistry.HasDirtyChildProperty(this, propertyPath, DefaultExposedObjectResolver.Instance);
        }

        internal void EnsureDefaultCaptured(string propertyPath)
        {
            ExposedObjectDefaultRegistry.EnsurePropertyDefaultCaptured(this, propertyPath, DefaultExposedObjectResolver.Instance);
        }

        public void ClearDirty()
        {
            ExposedObjectDefaultRegistry.ClearDirty(this, DefaultExposedObjectResolver.Instance);
        }

        public void ClearPropertyDirty(string propertyPath)
        {
            ExposedObjectDefaultRegistry.ClearPropertyDirty(this, propertyPath, DefaultExposedObjectResolver.Instance);
        }

        public IReadOnlyCollection<string> GetDirtyProperties()
        {
            return ExposedObjectDefaultRegistry.GetDirtyProperties(this, DefaultExposedObjectResolver.Instance);
        }

        public bool Revert(string propertyPath)
        {
            // ExposedPropertyRef は参照先を revert する
            var property = FindProperty(propertyPath);
            if (property.HasValue && property.Value.type != null && property.Value.type.isExposedPropertyReference)
            {
                property.Value.RevertValue();
                return true;
            }
            return ExposedObjectDefaultRegistry.Revert(this, propertyPath, DefaultExposedObjectResolver.Instance);
        }

        /// <summary>
        /// 指定パスのデフォルト値を取得する。デフォルト値が設定されていない場合はnullを返す。
        /// </summary>
        public object GetDefaultValue(string propertyPath)
        {
            var token = ExposedObjectDefaultRegistry.GetDefaultToken(this, propertyPath);
            if (token == null) return null;

            // 配列長チェック用途ではJArrayのCountを直接返すことができないため、
            // プロパティの型情報を使ってデシリアライズする
            var property = FindProperty(propertyPath);
            if (property == null) return null;

            return ExposedPropertySerializer.DeserializeUnityType(
                DefaultExposedObjectResolver.Instance, token, property.Value.type.valueType);
        }

        public void SetDefault(string propertyPath, object defaultValue)
        {
            // JSON-based system: 現在のシリアライズ値でデフォルトを更新
            ExposedObjectDefaultRegistry.ClearPropertyDirty(this, propertyPath, DefaultExposedObjectResolver.Instance);
        }

        /// <summary>
        /// 登録解除
        /// </summary>
        public void Unregister()
        {
            ExposedObjectDefaultRegistry.Remove(this);
            ExposedObjectRegistry.Unregister(this);
        }

        // --- 値等価 (struct なので参照同一性ではなく targetType + target(参照) + id で比較) ---

        public bool Equals(ExposedObjectHandle other)
        {
            return ReferenceEquals(targetType, other.targetType)
                && ReferenceEquals(target, other.target)
                && string.Equals(id, other.id, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is ExposedObjectHandle other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = target != null
                    ? RuntimeHelpers.GetHashCode(target)
                    : (targetType != null ? targetType.GetHashCode() : 0);
                h = (h * 397) ^ (id != null ? id.GetHashCode() : 0);
                return h;
            }
        }

        public static bool operator ==(ExposedObjectHandle a, ExposedObjectHandle b) => a.Equals(b);

        public static bool operator !=(ExposedObjectHandle a, ExposedObjectHandle b) => !a.Equals(b);
    }
}
