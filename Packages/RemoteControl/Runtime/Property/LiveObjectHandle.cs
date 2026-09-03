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
    /// 状態 (デフォルト値・dirty) は <see cref="LiveObjectDefaultRegistry"/> が target 参照キーで保持するため、
    /// このハンドル自体は不変な readonly struct として使い捨てできる。
    /// </summary>
    public readonly struct LiveObjectHandle : IEquatable<LiveObjectHandle>
    {
        public readonly LiveClass targetType;

        public readonly string id;

        public bool hasId => !string.IsNullOrEmpty(id);

        public readonly object target;

        public string name => target as UnityEngine.Object != null ? ((UnityEngine.Object)target).name : target as ILiveObject != null ? ((ILiveObject)target).name : id;

        public string targetTypeName => targetType?.typeName ?? null;

        public LivePropertyType[] propertyTypes => targetType?.propertyTypes ?? new LivePropertyType[0];

        public bool isValid => _IsAlive(target) || (targetType != null && targetType.isStatic);

        private static bool _IsAlive(object obj)
        {
            if (obj == null) return false;
            if (obj is UnityEngine.Object unityObj) return unityObj != null;
            return true;
        }

        public bool isDirty => LiveObjectDefaultRegistry.IsDirty(this, DefaultLiveObjectResolver.Instance);

        // [Debug] TEMPORARY. Remove with the probe in the constructor below.
        private static readonly HashSet<Type> _idlessReported = new HashSet<Type>();

        public LiveObjectHandle(string id, LiveClass type, object target)
        {
            Debug.Assert(type != null, "LiveClass type cannot be null");

            if (target == null && type != null && !type.isStatic)
            {
                Debug.LogWarning($"[RemoteControl] Creating LiveObjectHandle with null target for non-static type:{type.typeName} id:{id}");
            }

            // [Debug] TEMPORARY: find who registers a component with no id. Once per type so it
            // cannot spam. Remove once the caller is fixed.
            if (string.IsNullOrEmpty(id) && target is Component && _idlessReported.Add(target.GetType()))
                Debug.LogWarning("[Debug] id-less handle for " + type?.typeName + " :: " + System.Environment.StackTrace);

            this.targetType = type;
            this.target = target;
            this.id = id;

            // レジストリに登録 (struct のコピーを格納する。id/target は不変なので同値判定で解決できる)
            LiveObjectRegistry.Register(this);

            // デフォルト値を自動キャプチャ（dirty検出のベースライン）
            // インスタンス型: ターゲットの型がLiveClassの型と互換性がある場合のみ実行
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
                LiveObjectDefaultRegistry.EnsureDefaultsCaptured(this, DefaultLiveObjectResolver.Instance);
            }
        }

        /// <summary>
        /// レジストリに登録しないLiveObjectを生成する。
        /// プロパティ走査やAPI応答など、一時的なコンテキストとして使用する。
        /// </summary>
        internal static LiveObjectHandle CreateUnregistered(LiveClass type, object target)
        {
            Debug.Assert(type != null, "LiveClass type cannot be null");
            return new LiveObjectHandle(type, target);
        }

        private LiveObjectHandle(LiveClass type, object target)
        {
            this.targetType = type;
            this.target = target;
            this.id = null;
        }

        // 値だけを設定する内部ctor（登録/デフォルトキャプチャを行わない）。引数順を変えて公開ctorとシグネチャを分ける。
        private LiveObjectHandle(LiveClass type, object target, string id)
        {
            this.targetType = type;
            this.target = target;
            this.id = id;
        }

        /// <summary>
        /// id だけを差し替えた新しいハンドルを返す（副作用なし）。Registry の再キー専用。
        /// </summary>
        internal LiveObjectHandle WithId(string newId) => new LiveObjectHandle(targetType, target, newId);

        public bool ResolveReferences(IExposedPropertyTable resolver)
        {
            return isValid;
        }

        public LiveProperty? GetProperty(ReadOnlySpan<char> name)
        {
            // name.ToString() を挟まず span のまま引く (パス解決のたびの文字列確保を避ける)。
            var propertyType = targetType?.FindProperty(name);
            if (propertyType != null)
            {
                return new LiveProperty(propertyType, this, target);
            }
            return null;
        }

        public LiveProperty? FindProperty(string path)
        {
            if (path == null) throw new System.ArgumentNullException(nameof(path));

            LiveProperty? property = null;
            foreach (var segment in PropertyPathParser.Parse(path))
            {
                if (property == null)
                {
                    // LiveObjectHandle.GetProperty は ReadOnlySpan<char> を受け取る
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
                        // LiveProperty.GetProperty の span 版で name.ToString() を避ける
                        property = property?.GetProperty(segment.name);
                    }
                }

                if (property == null)
                {
                    return null;
                }
            }

            return property;
        }

        public bool TryFindProperty(ReadOnlySpan<char> name, out LiveProperty property)
        {
            var propertyOrNull = GetProperty(name);
            property = propertyOrNull ?? default;
            return propertyOrNull != null;
        }

        public LiveFunctionType GetFunction(string name)
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

        /// <summary>
        /// 関数を解決する。<paramref name="propertyPath"/> が空ならこのオブジェクト直下の関数、非空なら
        /// そのパス (slash 転送形式) のプロパティ値の型 (LiveClass) から関数を検索する。解決できなければ
        /// null を返す (ログは出さない — 呼び出し側が扱いを決める)。<paramref name="functionTarget"/> は実行対象
        /// インスタンス (直下なら target、ネストならプロパティ値)。REST invoke のネスト解決
        /// (LiveObjectHandler._ResolveInvokeFunction) と同じ経路で、ネストした exposed 関数
        /// (例: StageManager の set 要素の WarpTo) をオペレーションから実行するために使う。
        /// </summary>
        public LiveFunctionType ResolveFunction(string propertyPath, string functionName, out object functionTarget)
        {
            if (string.IsNullOrEmpty(propertyPath))
            {
                functionTarget = target;
                return GetFunction(functionName);
            }

            functionTarget = null;
            var property = FindProperty(PropertyPath.FromSlash(propertyPath).Value);
            if (property == null) return null;

            var value = property.Value.GetValue();
            if (value == null) return null;

            var liveClass = LiveClass.Get(value.GetType());
            if (liveClass == null) return null;

            functionTarget = value;
            return liveClass.FindFunction(functionName);
        }

        // --- Dirty追跡（LiveObjectDefaultRegistryに委譲） ---

        public bool IsPropertyDirty(string propertyPath)
        {
            // LivePropertyRef は参照先の dirty 状態を見る
            var property = FindProperty(propertyPath);
            if (property.HasValue && property.Value.type != null && property.Value.type.isLivePropertyReference)
            {
                return property.Value.isDirty;
            }
            return LiveObjectDefaultRegistry.IsPropertyDirty(this, propertyPath, DefaultLiveObjectResolver.Instance);
        }

        /// <summary>
        /// 指定パスまたはその子プロパティがdirtyかチェック
        /// </summary>
        public bool HasDirtyChildProperty(string propertyPath)
        {
            return LiveObjectDefaultRegistry.HasDirtyChildProperty(this, propertyPath, DefaultLiveObjectResolver.Instance);
        }

        internal void EnsureDefaultCaptured(string propertyPath)
        {
            LiveObjectDefaultRegistry.EnsurePropertyDefaultCaptured(this, propertyPath, DefaultLiveObjectResolver.Instance);
        }

        public void ClearDirty()
        {
            LiveObjectDefaultRegistry.ClearDirty(this, DefaultLiveObjectResolver.Instance);
        }

        public void ClearPropertyDirty(string propertyPath)
        {
            LiveObjectDefaultRegistry.ClearPropertyDirty(this, propertyPath, DefaultLiveObjectResolver.Instance);
        }

        /// <summary>
        /// Adopts the current state as the user-change baseline without touching the serialization
        /// defaults. See <see cref="LiveObjectDefaultRegistry.MarkClean"/>.
        /// </summary>
        public void MarkClean()
        {
            LiveObjectDefaultRegistry.MarkClean(this, DefaultLiveObjectResolver.Instance);
        }

        public IReadOnlyCollection<string> GetDirtyProperties()
        {
            return LiveObjectDefaultRegistry.GetDirtyProperties(this, DefaultLiveObjectResolver.Instance);
        }

        public bool Revert(string propertyPath)
        {
            // LivePropertyRef は参照先を revert する
            var property = FindProperty(propertyPath);
            if (property.HasValue && property.Value.type != null && property.Value.type.isLivePropertyReference)
            {
                // Answered rather than assumed, like everything else on this path: the reference's
                // target may have nothing recorded either.
                return property.Value.RevertValue();
            }
            return LiveObjectDefaultRegistry.Revert(this, propertyPath, DefaultLiveObjectResolver.Instance);
        }

        /// <summary>
        /// 指定パスのデフォルト値を取得する。デフォルト値が設定されていない場合はnullを返す。
        /// </summary>
        public object GetDefaultValue(string propertyPath)
        {
            var token = LiveObjectDefaultRegistry.GetDefaultToken(this, propertyPath);
            if (token == null) return null;

            // 配列長チェック用途ではJArrayのCountを直接返すことができないため、
            // プロパティの型情報を使ってデシリアライズする
            var property = FindProperty(propertyPath);
            if (property == null) return null;

            return LivePropertySerializer.DeserializeUnityType(
                DefaultLiveObjectResolver.Instance, token, property.Value.type.valueType);
        }

        public void SetDefault(string propertyPath, object defaultValue)
        {
            // JSON-based system: 現在のシリアライズ値でデフォルトを更新
            LiveObjectDefaultRegistry.ClearPropertyDirty(this, propertyPath, DefaultLiveObjectResolver.Instance);
        }

        /// <summary>
        /// 登録解除
        /// </summary>
        public void Unregister()
        {
            LiveObjectDefaultRegistry.Remove(this);
            LiveObjectRegistry.Unregister(this);
        }

        // --- 値等価 (struct なので参照同一性ではなく targetType + target(参照) + id で比較) ---

        public bool Equals(LiveObjectHandle other)
        {
            return ReferenceEquals(targetType, other.targetType)
                && ReferenceEquals(target, other.target)
                && string.Equals(id, other.id, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is LiveObjectHandle other && Equals(other);

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

        public static bool operator ==(LiveObjectHandle a, LiveObjectHandle b) => a.Equals(b);

        public static bool operator !=(LiveObjectHandle a, LiveObjectHandle b) => !a.Equals(b);
    }
}
