// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;

using UnityEngine;
using Lilium.RemoteControl.Reflection;



namespace Lilium.RemoteControl
{
    public class ExposedObject
    {
        public readonly ExposedClass targetType;

        public string id { get; private set; }

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

        public ExposedObject(string id, ExposedClass type, object target)
        {
            Debug.Assert(type != null, "ExposedClass type cannot be null");

            if (target == null)
            {
                if (!(type.isStatic))
                {
                    Debug.LogWarning($"[RemoteControl] Creating ExposedObject with null target for non-static type:{type.typeName} id:{id}");
                }
            }

            this.targetType = type;
            this.target = target;
            this.id = id;

            // レジストリに登録
            ExposedObjectRegistry.Register(this);

            // デフォルト値を自動キャプチャ（dirty検出のベースライン）
            // インスタンス型: ターゲットの型がExposedClassの型と互換性がある場合のみ実行
            // static型: target=nullだがstaticプロパティを直接読み取れるため実行
            if (type.isStatic || (target != null && type.type != null && type.type.IsInstanceOfType(target)))
            {
                ExposedPropertyUtility.SetDefault(this);
            }
        }

        /// <summary>
        /// レジストリに登録しないExposedObjectを生成する。
        /// プロパティ走査やAPI応答など、一時的なコンテキストとして使用する。
        /// </summary>
        internal static ExposedObject CreateUnregistered(ExposedClass type, object target)
        {
            Debug.Assert(type != null, "ExposedClass type cannot be null");
            return new ExposedObject(type, target);
        }

        private ExposedObject(ExposedClass type, object target)
        {
            this.targetType = type;
            this.target = target;
            this.id = null;
        }

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
                    // ExposedObject.GetProperty は ReadOnlySpan<char> を受け取る
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
        /// IDなしのExposedObjectにIDを後から割り当てる。
        /// コンテナ登録前にIDなしで生成されたケースの救済用。
        /// </summary>
        internal void AssignId(string newId)
        {
            if (hasId) return;
            if (string.IsNullOrEmpty(newId)) return;

            id = newId;
            ExposedObjectRegistry.AssignId(this, newId);
        }

        /// <summary>
        /// 登録解除
        /// </summary>
        /// <summary>
        /// IDを変更してレジストリを再登録する。
        /// Play mode再入時のGUID再生成によるIDミスマッチの復元に使用。
        /// </summary>
        public void ReplaceId(string newId)
        {
            if (id == newId) return;
            ExposedObjectRegistry.Unregister(this);
            id = newId;
            ExposedObjectRegistry.Register(this);
        }

        public void Unregister()
        {
            ExposedObjectDefaultRegistry.Remove(this);
            ExposedObjectRegistry.Unregister(this);
        }

    }
}
