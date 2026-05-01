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


    public readonly struct ExposedProperty
    {
        public readonly ExposedPropertyType type;

        public readonly ExposedObject owner;

        public readonly object obj;

        public readonly bool isArray;

        public readonly PropertyPath path;

        public ExposedProperty(ExposedPropertyType type, ExposedObject owner, object obj, string path = null)
        {
            this.type = type;
            this.owner = owner;
            this.obj = obj;
            this.path = new PropertyPath(path ?? type?.name);
            this.isArray = ExposedPropertyUtility.IsArrayType(type?.valueType);
        }

        public ExposedProperty(ExposedPropertyType type, ExposedObject owner, object obj, PropertyPath path)
        {
            this.type = type;
            this.owner = owner;
            this.obj = obj;
            this.path = path;
            this.isArray = ExposedPropertyUtility.IsArrayType(type?.valueType);
        }

        /// <summary>
        /// パスに指定されたメンバ名が含まれているかどうか
        /// </summary>
        public bool PathContains(string memberName) => path.Value.Contains(memberName);

        public bool isValid => type != null && owner != null;

        public bool isDirty
        {
            get
            {
                if (type != null && type.isExposedPropertyReference)
                {
                    var resolved = _ResolveRef();
                    return resolved?.isDirty ?? false;
                }
                return owner?.IsPropertyDirty(path) ?? false;
            }
        }

        /// <summary>
        /// PropertyRef 型のフィールド値を読み、参照先 ExposedProperty を解決する。
        /// PropertyRef でない/解決失敗なら null。
        /// </summary>
        private ExposedProperty? _ResolveRef()
        {
            if (type == null || !type.isExposedPropertyReference) return null;
            var raw = ExposedPropertyUtility.GetValueRaw(obj, type);
            if (raw is ExposedPropertyRef pr) return pr.Resolve();
            return null;
        }

        //TODO: パフォーマンス改善の余地あり
        public bool isExposedObjectReference => type.isExposedObjectReference && ExposedClass.Has(GetPolymorphicValueType());

        public int arrayLength
        {
            get
            {
                if (!isArray) return 0;
                return ExposedPropertyUtility.GetCollectionLength(GetValue());
            }
        }

        /// <summary>
        /// ポリモーフィックな値の型を取得
        /// </summary>
        /// TODO: パフォーマンス改善の余地あり
        public Type GetPolymorphicValueType()
        {
            if (type != null && type.isExposedPropertyReference)
            {
                var resolved = _ResolveRef();
                return resolved?.GetPolymorphicValueType();
            }
            return ExposedPropertyUtility.GetValueRaw(obj, type)?.GetType();
        }

        public ExposedProperty? GetProperty(string name)
        {
            // 配列要素の場合
            if (isArray)
            {
                // 配列要素の中からtype名もしくはnameプロパティが一致するものを探す
                // TODO: パフォーマンス改善の余地あり
                for (int i = 0; i < arrayLength; i++)
                {
                    var element = GetPropertyIndex(i);
                    if (element == null) continue;

                    var polymorphicValueType = ExposedClass.Find(element?.GetPolymorphicValueType());
                    if (polymorphicValueType != null && string.Compare(polymorphicValueType.typeName, name, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        return element;
                    }

                    var nameProperty = element?.GetProperty("name");
                    if (nameProperty != null && nameProperty?.GetValue().ToString() == name)
                    {
                        return element;
                    }
                }

                return null;
            }
            // 参照型かつExposedClassの場合
            else if (this.isExposedObjectReference)
            {
                var polymorphicExposedType = ExposedClass.Find(GetPolymorphicValueType());
                Debug.Assert(polymorphicExposedType != null, "Polymorphic ExposedClass should not be null for ExposedObject reference");

                var value = this.GetValue();
                // 既存のExposedObjectがあればそれを使用、なければIDなしで生成
                var existingExposed = ExposedObjectRegistry.FindByTarget(value);
                ExposedObject newOwner = existingExposed ?? ExposedObject.CreateUnregistered(polymorphicExposedType, value);
                // FindProperty は [FormerlyExposedAs] の旧名もエイリアスとして解決する
                var propertyType = polymorphicExposedType.FindProperty(name);
                if (propertyType != null)
                {
                    // 取得子 path は属性解決後の canonical な name を使う
                    string childPath = propertyType.name;
                    return new ExposedProperty(propertyType, newOwner, this.GetValue(), childPath);
                }
                return null;
            }
            // struct, primitive, stringなどの実体型の場合
            else
            {
                ExposedObject newOwner = this.owner;

                var polymorphicType = GetPolymorphicValueType();
                if (polymorphicType == null) return null;

                ExposedClass polymorphicExposedType = ExposedClass.Find(polymorphicType);
                if (polymorphicExposedType == null) return null;

                // 参照型で既存のExposedObjectがあればオーナーを切り替え
                // (object[]などの宣言型がUnityEngine.Objectでなくても、実体が登録済みExposedObjectなら正しいオーナーを使用)
                bool ownerSwitched = false;
                if (!polymorphicType.IsValueType)
                {
                    var value = this.GetValue();
                    var existingExposed = ExposedObjectRegistry.FindByTarget(value);
                    if (existingExposed != null)
                    {
                        newOwner = existingExposed;
                        ownerSwitched = true;
                    }
                    // UnityEngine.Object（MonoBehaviour/ScriptableObject等）でレジストリ未登録だが
                    // ExposedClassが存在する場合は一時的なExposedObjectを生成する。
                    // Selector経由（object[]）で辿られるコンポーネント等が未登録でも、
                    // 正しいExposedClass上でonPropertyChangedイベントが発火するようにする。
                    else if (value is UnityEngine.Object && polymorphicExposedType != null)
                    {
                        newOwner = ExposedObject.CreateUnregistered(polymorphicExposedType, value);
                        ownerSwitched = true;
                    }
                }

                // FindProperty は [FormerlyExposedAs] の旧名もエイリアスとして解決する
                {
                    var propertyType = polymorphicExposedType.FindProperty(name);
                    if (propertyType != null)
                    {
                        // オーナー切替時はプロパティ名のみ、それ以外は親パスから連結
                        // 取得子 path は属性解決後の canonical な name を使う
                        string childPath = ownerSwitched
                            ? propertyType.name
                            : new PropertyPath(this.path).Append(propertyType.name);
                        return new ExposedProperty(propertyType, newOwner, this.GetValue(), childPath);
                    }
                }

                return null;
            }
        }
        
        public ExposedProperty? GetPropertyIndex(int index)
        {
            if (!isArray) return null;

            var currentLength = arrayLength;
            if (index < 0 || index >= currentLength) return null;

            // 配列要素の型を取得
            var elementType = ExposedPropertyUtility.GetCollectionElementType(type.valueType);

            //TODO: 配列要素用のExposedPropertyを動的生成しているのでパフォーマンス改善の余地あり
            var arrayElementEntity = new ExposedPropertyType(elementType, index);
            var arrayValue = this.GetValue();
            var childPath = this.path.AppendIndex(index);

            return new ExposedProperty(arrayElementEntity, owner, arrayValue, childPath);
        }

        public object GetValue()
        {
            if (type != null && type.isExposedPropertyReference)
            {
                var resolved = _ResolveRef();
                return resolved?.GetValue();
            }
            return ExposedPropertyUtility.GetValueRaw(obj, type);
        }

        public void SetValue(object value)
        {
            SetValue(value, captureDefault: true);
        }

        public void SetValue(object value, bool captureDefault)
        {
            // PropertyRef: 参照先に委譲しつつ、owner (= FusionPage 側) にも onPropertyChanged を発火して SSE Broadcast を走らせる
            if (type != null && type.isExposedPropertyReference)
            {
                var resolved = _ResolveRef();
                if (resolved.HasValue)
                {
                    var refOldValue = resolved.Value.GetValue();
                    // 参照先の captureDefault は resolved 側で管理
                    resolved.Value.SetValue(value, captureDefault);
                    // owner (FusionPage) のイベントも発火 → ExposedPropertyBroadcast が FusionPage.smoothness 更新を送る
                    owner?.targetType?.RaisePropertyChanged(this, refOldValue);
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
            ExposedPropertyUtility.SetValueRaw(obj, type, value);

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

            // onPropertyChangedイベントを呼び出す
            owner.targetType?.RaisePropertyChanged(this, oldValue);
        }

        public void SetDefaultValue()
        {
            // PropertyRef: 自身は default を保持しない (参照先が管理)
            if (type != null && type.isExposedPropertyReference)
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

            // @ref判定：値がExposedObjectとして登録されていれば参照扱い（再帰しない）
            // 参照オブジェクトは別途ExposedObjectとして登録・初期化されるため再帰不要
            var value = GetValue();
            if (value != null && ExposedObjectRegistry.FindByTarget(value) != null)
            {
                owner.SetDefault(path, value);
                owner.ClearPropertyDirty(path);
                return;
            }

            // 構造体またはオブジェクトで ExposedClass が登録されている場合
            var valueType = GetPolymorphicValueType();
            if (valueType != null)
            {
                var exposedClass = ExposedClass.Find(valueType);
                if (exposedClass != null && exposedClass.propertyTypes != null)
                {
                    // 構造体の場合：子プロパティにのみ伝搬
                    foreach (var childType in exposedClass.propertyTypes)
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

            // プリミティブ型や ExposedClass が登録されていない型：自身のデフォルト値を設定
            owner.SetDefault(path, GetValue());
            owner.ClearPropertyDirty(path);
        }

        /// <summary>
        /// 値をデフォルト値に戻す
        /// </summary>
        public void RevertValue()
        {
            // PropertyRef: 参照先の RevertValue に委譲
            if (type != null && type.isExposedPropertyReference)
            {
                var resolved = _ResolveRef();
                if (resolved.HasValue)
                {
                    var oldValue = resolved.Value.GetValue();
                    resolved.Value.RevertValue();
                    // owner (FusionPage) 側の PropertyChanged も発火して SSE で UI 更新を反映
                    owner?.targetType?.RaisePropertyChanged(this, oldValue);
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

            var exposedClass = ExposedClass.Find(valueType);
            if (exposedClass != null && exposedClass.propertyTypes != null)
            {
                foreach (var childType in exposedClass.propertyTypes)
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
