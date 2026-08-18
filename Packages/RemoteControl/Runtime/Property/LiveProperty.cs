using System;
using System.Collections.Generic;

using UnityEngine;
using Lilium.RemoteControl.Reflection;

using PropertyPath = Lilium.RemoteControl.Reflection.PropertyPath;

namespace Lilium.RemoteControl
{
    public enum SerializeMode
    {
        Snapshot,
        Delta
    }

    [Flags]
    public enum ExcludeFilter
    {
        None = 0,
        Static = 1 << 0,
    }


    public readonly struct LiveProperty
    {
        public readonly LivePropertyType type;

        public readonly LiveObjectHandle owner;

        public readonly object obj;

        public readonly bool isArray;

        public readonly PropertyPath path;

        public LiveProperty(LivePropertyType type, LiveObjectHandle owner, object obj, string path = null)
        {
            this.type = type;
            this.owner = owner;
            this.obj = obj;
            this.path = new PropertyPath(path ?? type?.name);
            this.isArray = LivePropertyUtility.IsArrayType(type?.valueType);
        }

        public LiveProperty(LivePropertyType type, LiveObjectHandle owner, object obj, PropertyPath path)
        {
            this.type = type;
            this.owner = owner;
            this.obj = obj;
            this.path = path;
            this.isArray = LivePropertyUtility.IsArrayType(type?.valueType);
        }

        /// <summary>
        /// パスに指定されたメンバ名が含まれているかどうか
        /// </summary>
        public bool PathContains(string memberName) => path.Value.Contains(memberName);

        /// <summary>
        /// True when the path's first segment is exactly <paramref name="memberName"/> — i.e. the write
        /// went into that top-level member (or something nested under it).
        /// <para>
        /// ⚠ Prefer this over <see cref="PathContains"/> when reacting to "was this member written".
        /// A substring test matches any member whose name merely contains the word, so adding a
        /// <c>heldFrames</c> next to a <c>held</c> silently changes what the existing check answers.
        /// </para>
        /// </summary>
        public bool PathRootIs(string memberName) => _SegmentEquals(0, memberName);

        /// <summary>
        /// True when the path's last segment is exactly <paramref name="memberName"/> — i.e. that member
        /// itself was written, however deeply nested. Same reason to prefer it over
        /// <see cref="PathContains"/>.
        /// </summary>
        public bool PathLeafIs(string memberName)
        {
            var text = path.Value;
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(memberName)) return false;
            int start = text.LastIndexOf('/') + 1;
            return string.CompareOrdinal(text, start, memberName, 0, memberName.Length) == 0
                && text.Length - start == memberName.Length;
        }

        // Compares one '/'-separated segment without allocating a split.
        private bool _SegmentEquals(int index, string memberName)
        {
            var text = path.Value;
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(memberName)) return false;

            int start = 0;
            for (int i = 0; i < index; i++)
            {
                start = text.IndexOf('/', start);
                if (start < 0) return false;
                start++;
            }
            int end = text.IndexOf('/', start);
            if (end < 0) end = text.Length;
            return end - start == memberName.Length
                && string.CompareOrdinal(text, start, memberName, 0, memberName.Length) == 0;
        }

        // owner は struct。default(LiveObjectHandle) は targetType==null なので、これで「owner が存在する」を表す
        // (旧 class 実装の owner != null と等価。target 破棄の有無は問わない)
        public bool isValid => type != null && owner.targetType != null;

        public bool isDirty
        {
            get
            {
                if (type != null && type.isLivePropertyReference)
                {
                    var resolved = _ResolveRef();
                    return resolved?.isDirty ?? false;
                }
                return owner.targetType != null && owner.IsPropertyDirty(path);
            }
        }

        /// <summary>
        /// PropertyRef 型のフィールド値を読み、参照先 LiveProperty を解決する。
        /// PropertyRef でない/解決失敗なら null。
        /// </summary>
        private LiveProperty? _ResolveRef()
        {
            if (type == null || !type.isLivePropertyReference) return null;
            var raw = LivePropertyUtility.GetValueRaw(obj, type);
            if (raw is LivePropertyRef pr) return pr.Resolve();
            return null;
        }

        //TODO: パフォーマンス改善の余地あり
        public bool isLiveObjectReference => type.isLiveObjectReference && GetPolymorphicValueType() is Type t && LiveClass.Has(t);

        public int arrayLength
        {
            get
            {
                if (!isArray) return 0;
                return LivePropertyUtility.GetCollectionLength(GetValue());
            }
        }

        /// <summary>
        /// ポリモーフィックな値の型を取得
        /// </summary>
        /// TODO: パフォーマンス改善の余地あり
        public Type GetPolymorphicValueType()
        {
            if (type != null && type.isLivePropertyReference)
            {
                var resolved = _ResolveRef();
                return resolved?.GetPolymorphicValueType();
            }
            return LivePropertyUtility.GetValueRaw(obj, type)?.GetType();
        }

        public LiveProperty? GetProperty(string name)
        {
            // 配列要素の場合: type名もしくはキー(name)プロパティが一致する要素を探す。
            // 毎フレーム解決される (SetPropertyOperation 等) ため、コレクションは 1 回だけ取得し、
            // 各要素は実体から直接添字アクセス + キー値を直接読んで照合する。使い捨ての LiveProperty /
            // path 文字列を要素ごとに作らず、一致した 1 個だけを構築することで走査の GC を排除する。
            if (isArray)
            {
                var collection = GetValue();
                int len = LivePropertyUtility.GetCollectionLength(collection);

                // 要素型が PropertyRef のときは実体型と解決先型が異なるため、その場合のみ従来の
                // per-element 経路 (GetPolymorphicValueType が参照先を解決する) にフォールバックして挙動を維持する。
                var firstElement = len > 0 ? LivePropertyType.GetArrayElement(type.valueType, 0) : null;
                bool refElements = firstElement != null && firstElement.isLivePropertyReference;

                for (int i = 0; i < len; i++)
                {
                    if (refElements)
                    {
                        var element = _GetPropertyIndexFrom(collection, i);
                        if (element == null) continue;

                        var refType = LiveClass.Find(element?.GetPolymorphicValueType());
                        if (refType != null && string.Compare(refType.typeName, name, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            return element;
                        }
                        var refKeyProperty = element?.GetProperty(refType?.keyProperty?.name ?? "name");
                        if (refKeyProperty != null && refKeyProperty?.GetValue()?.ToString() == name)
                        {
                            return element;
                        }
                        continue;
                    }

                    var elementValue = LivePropertyUtility.GetCollectionElement(collection, i);
                    if (elementValue == null) continue;

                    var elementType = LiveClass.Find(elementValue.GetType());

                    // 1) type 名一致
                    if (elementType != null && string.Compare(elementType.typeName, name, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        return _GetPropertyIndexFrom(collection, i);
                    }

                    // 2) [LiveKey] が付いていればそのプロパティで、無ければ慣習的に "name" プロパティで照合する。
                    var keyPropertyName = elementType?.keyProperty?.name ?? "name";
                    var keyPropertyType = elementType?.FindProperty(keyPropertyName);
                    if (keyPropertyType != null)
                    {
                        var keyValue = LivePropertyUtility.GetValueRaw(elementValue, keyPropertyType);
                        if (keyValue != null && keyValue.ToString() == name)
                        {
                            return _GetPropertyIndexFrom(collection, i);
                        }
                    }
                }

                return null;
            }
            // 非配列 (参照型 / 実体型) は string 化せず span のまま共通処理へ委譲する。
            return _GetMemberProperty(name.AsSpan());
        }

        /// <summary>
        /// <see cref="GetProperty(string)"/> の span 版。ネストパス解決時の <c>name.ToString()</c> を避ける。
        /// 配列要素の名前照合は要素値の ToString など文字列生成を伴い span の利得が薄いため、
        /// その場合のみ string 版へフォールバックする。非配列 (メンバー引き) はホットパスなので span のまま引く。
        /// </summary>
        public LiveProperty? GetProperty(ReadOnlySpan<char> name)
        {
            if (isArray) return GetProperty(name.ToString());
            return _GetMemberProperty(name);
        }

        // 非配列 (参照型 / 実体型) のメンバー引き。string/span 両 GetProperty から共有する。
        private LiveProperty? _GetMemberProperty(ReadOnlySpan<char> name)
        {
            // 参照型かつLiveClassの場合
            if (this.isLiveObjectReference)
            {
                var polymorphicLiveType = LiveClass.Find(GetPolymorphicValueType());
                Debug.Assert(polymorphicLiveType != null, "Polymorphic LiveClass should not be null for LiveObjectHandle reference");

                var value = this.GetValue();
                // 既存のLiveObjectがあればそれを使用、なければIDなしで生成
                var existingLive = LiveObjectRegistry.FindByTarget(value);
                LiveObjectHandle newOwner = existingLive ?? LiveObjectHandle.CreateUnregistered(polymorphicLiveType, value);
                // FindProperty は [FormerlyNamedAs] の旧名もエイリアスとして解決する
                var propertyType = polymorphicLiveType.FindProperty(name);
                if (propertyType != null)
                {
                    // 取得子 path は属性解決後の canonical な name を使う
                    string childPath = propertyType.name;
                    return new LiveProperty(propertyType, newOwner, this.GetValue(), childPath);
                }
                return null;
            }
            // struct, primitive, stringなどの実体型の場合
            else
            {
                LiveObjectHandle newOwner = this.owner;

                var polymorphicType = GetPolymorphicValueType();
                if (polymorphicType == null) return null;

                LiveClass polymorphicLiveType = LiveClass.Find(polymorphicType);
                if (polymorphicLiveType == null) return null;

                // 参照型で既存のLiveObjectがあればオーナーを切り替え
                // (object[]などの宣言型がUnityEngine.Objectでなくても、実体が登録済みLiveObjectなら正しいオーナーを使用)
                bool ownerSwitched = false;
                if (!polymorphicType.IsValueType)
                {
                    var value = this.GetValue();
                    var existingLive = LiveObjectRegistry.FindByTarget(value);
                    if (existingLive != null)
                    {
                        newOwner = existingLive.Value;
                        ownerSwitched = true;
                    }
                    // UnityEngine.Object（MonoBehaviour/ScriptableObject等）でレジストリ未登録だが
                    // LiveClassが存在する場合は一時的なLiveObjectを生成する。
                    // Selector経由（object[]）で辿られるコンポーネント等が未登録でも、
                    // 正しいLiveClass上でonPropertyChangedイベントが発火するようにする。
                    else if (value is UnityEngine.Object && polymorphicLiveType != null)
                    {
                        newOwner = LiveObjectHandle.CreateUnregistered(polymorphicLiveType, value);
                        ownerSwitched = true;
                    }
                }

                // FindProperty は [FormerlyNamedAs] の旧名もエイリアスとして解決する
                {
                    var propertyType = polymorphicLiveType.FindProperty(name);
                    if (propertyType != null)
                    {
                        // オーナー切替時はプロパティ名のみ、それ以外は親パスから連結
                        // 取得子 path は属性解決後の canonical な name を使う
                        string childPath = ownerSwitched
                            ? propertyType.name
                            : new PropertyPath(this.path).Append(propertyType.name);
                        return new LiveProperty(propertyType, newOwner, this.GetValue(), childPath);
                    }
                }

                return null;
            }
        }
        
        public LiveProperty? GetPropertyIndex(int index) => _GetPropertyIndexFrom(GetValue(), index);

        // 取得済みのコレクション値から index 番目の要素プロパティを束ねる。GetPropertyIndex の
        // arrayLength(=別 GetValue) と this.GetValue() の二重読みを避け、キー走査 (GetProperty) が
        // コレクションを 1 回だけ読んで各要素を引けるようにする共通経路。
        private LiveProperty? _GetPropertyIndexFrom(object collection, int index)
        {
            if (!isArray) return null;

            var currentLength = LivePropertyUtility.GetCollectionLength(collection);
            if (index < 0 || index >= currentLength) return null;

            // 配列要素の記述子 (LivePropertyType) は (コレクション型, index) に対して不変なので
            // キャッシュから取得する。GetPropertyIndex はシリアライズ時に配列要素をループで引くため、
            // 毎回動的生成すると GC が嵩む。値・オーナー・パスは呼び出しごとに異なるので下で束ねる。
            var arrayElementEntity = LivePropertyType.GetArrayElement(type.valueType, index);
            var childPath = this.path.AppendIndex(index);

            return new LiveProperty(arrayElementEntity, owner, collection, childPath);
        }

        public object GetValue()
        {
            if (type != null && type.isLivePropertyReference)
            {
                var resolved = _ResolveRef();
                return resolved?.GetValue();
            }
            return LivePropertyUtility.GetValueRaw(obj, type);
        }

        public void SetValue(object value)
        {
            SetValue(value, captureDefault: true);
        }

        public void SetValue(object value, bool captureDefault)
        {
            // PropertyRef: 参照先に委譲しつつ、owner (= FusionPage 側) にも onPropertyChanged を発火して変更記録を走らせる
            if (type != null && type.isLivePropertyReference)
            {
                var resolved = _ResolveRef();
                if (resolved.HasValue)
                {
                    var refOldValue = resolved.Value.GetValue();
                    // 参照先の captureDefault は resolved 側で管理
                    resolved.Value.SetValue(value, captureDefault);
                    // owner (FusionPage) のイベントも発火 → LivePropertyBroadcast が FusionPage.smoothness 更新を送る
                    owner.targetType?.RaisePropertyChanged(this, refOldValue);
                }
                return;
            }

            // 古い値を取得
            var oldValue = GetValue();

            // デフォルト値のキャプチャ（オプション）
            if (captureDefault)
            {
                owner.EnsureDefaultCaptured(path);
            }

            // onPropertyChangingイベントを呼び出す
            owner.targetType?.RaisePropertyChanging(this, value);

            // 値を設定
            LivePropertyUtility.SetValueRaw(obj, type, value);

            // objがstruct（ValueType）の場合、親にも反映する必要がある
            if (obj != null && obj.GetType().IsValueType && !obj.GetType().IsPrimitive && !obj.GetType().IsEnum)
            {
                // objはstructのコピーなので、親プロパティに書き戻す必要がある
                // パスから親プロパティを特定
                if (path.HasParent())
                {
                    var parentPath = path.GetParent();
                    var parentProperty = owner.FindProperty(parentPath);
                    if (parentProperty != null)
                    {
                        // objを親プロパティに設定（デフォルト値キャプチャはスキップ）
                        parentProperty.Value.SetValue(obj, captureDefault: false);
                    }
                }
            }


            // Editorに変更を通知
            _NotifyUnityDirty();

            // onPropertyChangedイベントを呼び出す
            owner.targetType?.RaisePropertyChanged(this, oldValue);
        }

        // 値変更を Editor に通知する (SetValue / TrySetValue 共通)。
        private void _NotifyUnityDirty()
        {
            if (obj is UnityEngine.Object unityObj)
            {
                PropertyUtility.Apply(unityObj);
            }
            else if (owner.target is UnityEngine.Object ownerUnityObj)
            {
                PropertyUtility.Apply(ownerUnityObj);
            }
            else
            {
                PropertyUtility.Apply();
            }
        }

        /// <summary>
        /// 値型プロパティを boxing なしで読む。SG が登録した型付き getter (<see cref="LivePropertyType.typedGetter"/>)
        /// が T に一致すれば object を経由せず読み取り、true を返す。型付き getter が無い・型不一致・
        /// アクセス不可の場合は object 経路 (<see cref="GetValue"/> 相当、値型は box) にフォールバックする。
        /// <see cref="GetValue"/> と違い例外は投げず、読めなければ false を返す。
        /// この API は将来のパイプライン全面型付き化 (B 案) の中核となる。
        /// </summary>
        public bool TryGetValue<T>(out T value)
        {
            value = default;
            if (type == null) return false;

            // PropertyRef は参照先に委譲 (GetValue と同じ)。
            if (type.isLivePropertyReference)
            {
                var resolved = _ResolveRef();
                return resolved.HasValue && resolved.Value.TryGetValue(out value);
            }

            if (!LivePropertyUtility.CanAccess(obj, type)) return false;

            // 型付き getter が T に一致すれば box ゼロで読む。
            if (type.typedGetter is Func<object, T> typed)
            {
                value = typed(type.isStatic ? null : obj);
                return true;
            }

            // フォールバック: object 経路 (値型は box するが従来挙動)。
            var raw = LivePropertyUtility.GetValueRaw(obj, type);
            if (raw is T t) { value = t; return true; }
            // 参照型 T で raw==null は「値あり (null)」として成功扱い。
            if (raw == null && !typeof(T).IsValueType) return true;
            return false;
        }

        /// <summary>
        /// 値型プロパティを boxing なしで書く。SG が登録した型付き setter が T に一致し、書込可・アクセス可能で
        /// owner が struct でない場合、object を経由せず書き込む。副作用 (EnsureDefaultCaptured / Editor dirty 通知 /
        /// onPropertyChanging・Changed) は <see cref="SetValue(object,bool)"/> と一致させる。ただし購読者
        /// (<see cref="LiveClass.hasPropertyChangeListeners"/>) がいない場合は oldValue 読み取りとイベント発火を
        /// 省略し box を出さない。fast path が使えない場合は従来 <see cref="SetValue(object,bool)"/> にフォールバック。
        /// 書き込めなかった (readOnly / 未解決 PropertyRef など) 場合は false を返す。
        /// </summary>
        public bool TrySetValue<T>(T value) => TrySetValue(value, captureDefault: true);

        /// <inheritdoc cref="TrySetValue{T}(T)"/>
        public bool TrySetValue<T>(T value, bool captureDefault)
        {
            if (type == null) return false;

            // PropertyRef: 参照先に委譲しつつ owner にも onPropertyChanged を発火 (SetValue と同じ)。
            if (type.isLivePropertyReference)
            {
                var resolved = _ResolveRef();
                if (!resolved.HasValue) return false;
                bool refNotify = owner.targetType != null && owner.targetType.hasPropertyChangeListeners;
                object refOldValue = refNotify ? resolved.Value.GetValue() : null;
                if (!resolved.Value.TrySetValue(value, captureDefault)) return false;
                if (refNotify) owner.targetType.RaisePropertyChanged(this, refOldValue);
                return true;
            }

            if (type.isReadOnly) return false;

            // struct owner は親への書き戻しが必要なので typed fast path 不可 (従来 SetValue に委ねる)。
            // (struct 宣言メンバーは SG がアクセサを登録しないので typedSetter は元々 null。防御的ガード)
            bool structOwner = obj != null && obj.GetType().IsValueType;

            if (!structOwner
                && type.typedSetter is Action<object, T> typed
                && LivePropertyUtility.CanAccess(obj, type))
            {
                bool notify = owner.targetType != null && owner.targetType.hasPropertyChangeListeners;
                // oldValue の読み取り・イベント発火は購読者がいる時だけ (box を出さない)。
                object oldValue = notify ? GetValue() : null;

                if (captureDefault) owner.EnsureDefaultCaptured(path);
                if (notify) owner.targetType.RaisePropertyChanging(this, value);

                typed(type.isStatic ? null : obj, value);   // box ゼロの書き込み

                _NotifyUnityDirty();
                if (notify) owner.targetType.RaisePropertyChanged(this, oldValue);
                return true;
            }

            // フォールバック: 従来 SetValue (T を box して object 経路で書く、挙動不変)。
            SetValue(value, captureDefault);
            return true;
        }

        public void SetDefaultValue()
        {
            // PropertyRef: 自身は default を保持しない (参照先が管理)
            if (type != null && type.isLivePropertyReference)
            {
                return;
            }

            // 配列の場合：自身のデフォルト値を設定し、各要素を再帰処理
            if (isArray)
            {
                owner.SetDefault(path, GetValue());
                int len = arrayLength;
                for (int i = 0; i < len; i++)
                {
                    var childProp = GetPropertyIndex(i);
                    if (childProp != null)
                    {
                        childProp.Value.SetDefaultValue();
                    }
                }
                return;
            }

            // @ref判定：値がLiveObjectとして登録されていれば参照扱い（再帰しない）
            // 参照オブジェクトは別途LiveObjectとして登録・初期化されるため再帰不要
            var value = GetValue();
            if (value != null && LiveObjectRegistry.FindByTarget(value) != null)
            {
                owner.SetDefault(path, value);
                owner.ClearPropertyDirty(path);
                return;
            }

            // 構造体またはオブジェクトで LiveClass が登録されている場合
            var valueType = GetPolymorphicValueType();
            if (valueType != null)
            {
                var liveClass = LiveClass.Find(valueType);
                if (liveClass != null && liveClass.propertyTypes != null)
                {
                    // 構造体の場合：子プロパティにのみ伝搬
                    foreach (var childType in liveClass.propertyTypes)
                    {
                        var childProp = GetProperty(childType.name);
                        if (childProp != null)
                        {
                            childProp.Value.SetDefaultValue();
                        }
                    }
                    owner.ClearPropertyDirty(path);
                    return;
                }
            }

            // プリミティブ型や LiveClass が登録されていない型：自身のデフォルト値を設定
            owner.SetDefault(path, GetValue());
            owner.ClearPropertyDirty(path);
        }

        /// <summary>
        /// 値をデフォルト値に戻す
        /// </summary>
        public void RevertValue()
        {
            // PropertyRef: 参照先の RevertValue に委譲
            if (type != null && type.isLivePropertyReference)
            {
                var resolved = _ResolveRef();
                if (resolved.HasValue)
                {
                    var oldValue = resolved.Value.GetValue();
                    resolved.Value.RevertValue();
                    // owner (FusionPage) 側の PropertyChanged も発火して変更フィード経由で UI 更新を反映
                    owner.targetType?.RaisePropertyChanged(this, oldValue);
                }
                return;
            }

            // 自身をリセット
            owner.Revert(path);

            // 配列の場合：各要素を再帰処理
            if (isArray)
            {
                int len = arrayLength;
                for (int i = 0; i < len; i++)
                {
                    var childProp = GetPropertyIndex(i);
                    if (childProp != null)
                    {
                        childProp.Value.RevertValue();
                    }
                }
                return;
            }

            // オブジェクトの場合：子プロパティを再帰処理
            var valueType = GetPolymorphicValueType();
            if (valueType == null) return;

            var liveClass = LiveClass.Find(valueType);
            if (liveClass != null && liveClass.propertyTypes != null)
            {
                foreach (var childType in liveClass.propertyTypes)
                {
                    var childProp = GetProperty(childType.name);
                    if (childProp != null)
                    {
                        childProp.Value.RevertValue();
                    }
                }
            }
        }

        public bool Add(object element)
        {
            if (!isArray)
            {
                return false;
            }

            var value = GetValue();

            // 配列がnullの場合は配列を生成
            if (value == null)
            {
                // List<T>の場合
                if (type.valueType.IsGenericType && type.valueType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var elementType = type.valueType.GetGenericArguments()[0];
                    var listType = typeof(List<>).MakeGenericType(elementType);
                    value = Activator.CreateInstance(listType);
                    SetValue(value);
                }
                // 配列の場合
                else if (type.valueType.IsArray)
                {
                    var elementType = type.valueType.GetElementType();
                    value = Array.CreateInstance(elementType, 0);
                    SetValue(value);
                }
                else
                {
                    return false;
                }
            }


            // List<T>の場合
            if (value is System.Collections.IList list && !value.GetType().IsArray)
            {
                // 型チェック
                var elementType = type.valueType.GetGenericArguments()[0];
                if (element != null && !elementType.IsAssignableFrom(element.GetType()))
                {
                    return false;
                }

                list.Add(element);
                return true;
            }
            // 配列の場合
            else if (value.GetType().IsArray)
            {
                var array = (Array)value;
                var elementType = type.valueType.GetElementType();

                // 型チェック
                if (element != null && !elementType.IsAssignableFrom(element.GetType()))
                {
                    return false;
                }

                // 新しい配列を作成
                var newArray = Array.CreateInstance(elementType, array.Length + 1);
                Array.Copy(array, newArray, array.Length);
                newArray.SetValue(element, array.Length);

                // 新しい配列を設定
                SetValue(newArray);
                return true;
            }

            return false;
        }

        public bool RemoveAt(int index)
        {
            if (!isArray)
            {
                return false;
            }

            var value = GetValue();
            if (value == null)
            {
                return false;
            }

            // List<T>の場合
            if (value is System.Collections.IList list && !value.GetType().IsArray)
            {
                if (index < 0 || index >= list.Count)
                {
                    return false;
                }

                list.RemoveAt(index);
                return true;
            }
            // 配列の場合
            else if (value.GetType().IsArray)
            {
                var array = (System.Array)value;

                if (index < 0 || index >= array.Length)
                {
                    return false;
                }

                // 新しい配列を作成
                var elementType = type.valueType.GetElementType();
                var newArray = System.Array.CreateInstance(elementType, array.Length - 1);

                // 削除対象の前の要素をコピー
                if (index > 0)
                {
                    System.Array.Copy(array, 0, newArray, 0, index);
                }

                // 削除対象の後の要素をコピー
                if (index < array.Length - 1)
                {
                    System.Array.Copy(array, index + 1, newArray, index, array.Length - index - 1);
                }

                // 新しい配列を設定
                SetValue(newArray);
                return true;
            }

            return false;
        }

        public bool Reorder(int fromIndex, int toIndex)
        {
            if (!isArray)
            {
                return false;
            }

            var value = GetValue();
            if (value == null)
            {
                return false;
            }

            // List<T>の場合
            if (value is System.Collections.IList list && !value.GetType().IsArray)
            {
                if (fromIndex < 0 || fromIndex >= list.Count) return false;
                if (toIndex < 0 || toIndex >= list.Count) return false;

                var item = list[fromIndex];
                list.RemoveAt(fromIndex);
                list.Insert(toIndex, item);
                return true;
            }
            // 配列の場合
            else if (value.GetType().IsArray)
            {
                var array = (System.Array)value;

                if (fromIndex < 0 || fromIndex >= array.Length) return false;
                if (toIndex < 0 || toIndex >= array.Length) return false;

                // 要素を取得
                var item = array.GetValue(fromIndex);

                // 新しい配列を作成して要素を並び替え
                var elementType = type.valueType.GetElementType();
                var newArray = System.Array.CreateInstance(elementType, array.Length);

                int newIndex = 0;
                for (int i = 0; i < array.Length; i++)
                {
                    if (i == fromIndex) continue; // 移動元はスキップ

                    if (newIndex == toIndex)
                    {
                        newArray.SetValue(item, newIndex);
                        newIndex++;
                    }

                    newArray.SetValue(array.GetValue(i), newIndex);
                    newIndex++;
                }

                // toIndexが最後の場合
                if (newIndex == toIndex)
                {
                    newArray.SetValue(item, newIndex);
                }

                SetValue(newArray);
                return true;
            }

            return false;
        }


    }
}
