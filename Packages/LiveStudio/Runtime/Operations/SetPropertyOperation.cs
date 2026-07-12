// Copyright (c) You-Ri, 2026

using System;
using UnityEngine.Scripting.APIUpdating;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Reflection;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Drives an exposed property of a target object (addressed by its stable
    /// <see cref="ExposedObjectRegistry"/> id) from the input — the generic counterpart of
    /// <see cref="SetActiveOperation"/>, which is hard-wired to <c>GameObject.activeSelf</c>. Created from the
    /// remote app's "bind to key" affordance next to a control, so the control's owning object
    /// (<see cref="targetId"/>) and member (<see cref="propertyPath"/>) are known at creation.
    ///
    /// Handles <c>bool</c> properties (bound to <see cref="InputMode.Toggle"/> by the manager so a key
    /// press flips the latched on/off, written from <see cref="OperationContext.active"/>) and <c>float</c>
    /// properties (bound to <see cref="InputMode.Value"/>, written from the continuous 0..1
    /// <see cref="OperationContext.value"/> — e.g. an avatar expression weight via
    /// <c>expressions[name].weight</c>). Both write only when the value differs, so there is no
    /// per-frame write/broadcast, like <see cref="SetActiveOperation"/>.
    ///
    /// When the id no longer resolves (object gone after a reload) <see cref="valid"/> is false and
    /// <see cref="Apply"/> is a no-op — distinct from <see cref="OperationSet.enabled"/>.
    /// </summary>
    [Serializable]
    [ExposedClass(Category = "Operation", Icon = "toggle_on")]
    [MovedFrom(false, null, null, "SetPropertyAction")]
    [FormerlyExposedAs("SetPropertyAction")]
    public class SetPropertyOperation : OperationBase
    {
        /// <summary>Stable id of the exposed object that owns the property.</summary>
        [ExposedField]
        public string targetId = string.Empty;

        /// <summary>Path of the property to drive. Stored in the remote app's transport (slash) form so it
        /// round-trips with the bind UI; e.g. "useSpout" or, for a keyed array element,
        /// "expressions/Joy/weight". Resolved via <see cref="_ResolvePath"/> just before lookup.</summary>
        [ExposedField]
        public string propertyPath = string.Empty;

        // 解決結果のキャッシュ。Apply は毎フレーム呼ばれるが、FindProperty の walk は要素 path の文字列確保
        // (PropertyPath.AppendIndex) や GameObject プロキシの components 配列確保 (ExposedGameObject.components)
        // を伴うため、targetId / propertyPath と解決元 handle・キー配列世代が不変な間は解決済み ExposedProperty を
        // 再利用して walk 自体を毎フレーム行わない。[NonSerialized] なのでシリアライズ / Domain Reload 復元時は
        // 既定 (null) に戻り次回 Apply で必ず再解決される。[ExposedField] でないため RemoteControl 非露出
        // (scene.json / REST 応答は不変)。Apply は LateUpdate (メインスレッド) からのみ触る (valid getter は非依存)。
        [NonSerialized] private ExposedProperty? _cachedProperty;
        [NonSerialized] private ExposedObjectHandle _cachedHandle;
        [NonSerialized] private string _cachedTargetId;
        [NonSerialized] private string _cachedPropertyPath;
        [NonSerialized] private int _cachedGeneration;
        // 解決を試みた結果 (null=未解決/dangling も含む) が現在のバインドに対して有効かどうか。
        // null 結果もキャッシュ対象にして、アバター読込中/表情欠落で解決できないパスが毎フレーム
        // 再 walk (components[] の get_components や AppendIndex の GC) するのを防ぐための可否フラグ。
        [NonSerialized] private bool _cacheValid;

        // Normalize the stored path (slash transport form) to the DotBracket form FindProperty expects.
        // A no-op for bare names ("useSpout") and DotBracket paths without slashes; converts a slash key
        // path "expressions/Joy/weight" to "expressions.Joy.weight" so it resolves by [ExposedKey].
        private string _ResolvePath() => PropertyPath.FromSlash(propertyPath).Value;

        // 解決済みプロパティを返す。バインド (targetId / propertyPath)、解決元オブジェクトの再登録
        // (handle 変化 = 対象破棄→再生成やシーン再読込)、およびキー付き配列の再構築 (keyedCollectionVersion 変化
        // = 例: アバター切替で expressions の要素が差し替わる) を検知したときだけ FindProperty で再解決する。
        // 解決不能 (null) の間は毎フレーム再解決し、対象が現れれば自己回復する (ダングリングは誤設定 / 一時状態)。
        private ExposedProperty? _ResolveProperty()
        {
            var handle = ExposedObjectRegistry.FindById(targetId);
            // 対象が未登録: 現れたら再解決するようキャッシュを無効化して即返す (walk なし)。
            if (handle == null) { _cacheValid = false; _cachedProperty = null; return null; }

            int generation = ExposedObjectRegistry.keyedCollectionVersion;
            bool fresh = _cacheValid
                && string.Equals(targetId, _cachedTargetId, StringComparison.Ordinal)
                && string.Equals(propertyPath, _cachedPropertyPath, StringComparison.Ordinal)
                && handle.Value.Equals(_cachedHandle)
                && generation == _cachedGeneration;

            if (!fresh)
            {
                // FindProperty が null (パス未解決 = dangling: 読込中/表情欠落) でもキャッシュして、毎フレームの
                // 再 walk を止める。無効化 (targetId/path/handle 変化, keyedCollectionVersion bump=アバター変更,
                // 型付き read 失敗) が起きるまで再解決しない → アバター読込完了時の InvalidateExpressions で自己回復する。
                _cachedProperty = handle.Value.FindProperty(_ResolvePath());
                _cachedHandle = handle.Value;
                _cachedTargetId = targetId;
                _cachedPropertyPath = propertyPath;
                _cachedGeneration = generation;
                _cacheValid = true;
            }
            return _cachedProperty;
        }

        /// <summary>True while the target id resolves and still exposes the property. Read-only and computed
        /// (never serialized); separate from <see cref="OperationSet.enabled"/>.</summary>
        [ExposedProperty]
        public bool valid
        {
            get
            {
                var handle = ExposedObjectRegistry.FindById(targetId);
                return handle != null && handle.Value.FindProperty(_ResolvePath()) != null;
            }
        }

        public override void Apply(in OperationContext context)
        {
            var property = _ResolveProperty();
            if (property == null) return;

            // 毎フレーム呼ばれるため、bool/float は型付きアクセサ (TryGetValue/TrySetValue) で box せず読み書きする。
            // TryGetValue<bool> を先に試して失敗→float、という順だと float プロパティで box するので、
            // 宣言型 (valueType) で静的に振り分ける。PropertyRef / object 宣言メンバー等は else の従来経路へ。
            var vt = property.Value.type?.valueType;
            if (vt == typeof(bool))
            {
                bool desired = context.active;
                if (property.Value.TryGetValue<bool>(out var currentBool))
                {
                    if (currentBool != desired) property.Value.TrySetValue(desired);
                }
                else
                {
                    // 型付き read 失敗 (破棄済み Unity leaf 等) はキャッシュを無効化して次フレーム再解決する。
                    _cacheValid = false;
                }
            }
            else if (vt == typeof(float))
            {
                // Value モード想定: 入力の連続値 (0..1) を制御の範囲 [min, max] へマッピングして書く
                // (DeckSlider が min/max を持つ。既定 0..1 では恒等なので素の値を書くのと同じ)。
                float mapped = context.MappedValue;
                if (property.Value.TryGetValue<float>(out var currentFloat))
                {
                    if (currentFloat != mapped) property.Value.TrySetValue(mapped);
                }
                else
                {
                    _cacheValid = false;
                }
            }
            else
            {
                // PropertyRef (valueType が ref 型) や object 宣言メンバー等は従来経路で互換維持。
                var current = property.Value.GetValue();
                switch (current)
                {
                    case bool currentBool:
                        bool desired = context.active;
                        if (currentBool != desired) property.Value.SetValue(desired);
                        break;
                    case float currentFloat:
                        float mapped = context.MappedValue;
                        if (currentFloat != mapped) property.Value.SetValue(mapped);
                        break;
                }
            }
        }
    }
}
