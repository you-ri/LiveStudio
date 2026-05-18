// Copyright (c) You-Ri, 2026
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// ExposedClass/ExposedEnum/ExposedPropertyType/ExposedFunctionType の型スキーマ JSON 出力
    /// (REST `/exposed/types|enums` および関数引数スキーマ)。ExposedPropertySerializer から分離。
    /// コア(値直列化 SerializeUnityType)と ObjectSelectorSerializer への一方向依存のみ。
    /// </summary>
    public static class ExposedTypeInfoSerializer
    {
        internal static string ToJson(ExposedClass type)
        {
            if (type == null) return "{}";
            var jObject = new JObject
            {
                ["type"] = type.typeName,
                ["properties"] = new JArray(type.propertyTypes.Select(p => JsonConvert.DeserializeObject<JObject>(ToJson(p))))
            };

            // カテゴリを追加（nullでない場合のみ）
            if (!string.IsNullOrEmpty(type.category))
            {
                jObject["category"] = LocalizationSystem.Translate(type.category);
            }

            // 関数を functions 配列に追加 (引数あり / なし 両方)
            // RemoteApp 側は parameters の有無で実行ボタンと引数入力モーダルを切り替える。
            if (type.functionTypes != null && type.functionTypes.Length > 0)
            {
                jObject["functions"] = new JArray(type.functionTypes.Select(f => JsonConvert.DeserializeObject<JObject>(ToJson(f))));
            }

            // help項目を追加（nullでない場合のみ）
            if (!string.IsNullOrEmpty(type.help))
            {
                jObject["help"] = LocalizationSystem.Translate(type.help);
            }

            // icon項目を追加（nullでない場合のみ）
            if (!string.IsNullOrEmpty(type.icon))
            {
                jObject["icon"] = type.icon;
            }

            // hideInScene項目を追加（trueの場合のみ）
            if (type.hideInScene)
            {
                jObject["hideInScene"] = true;
            }

            return JsonConvert.SerializeObject(jObject, Formatting.None);
        }

        internal static string ToJson(IEnumerable<ExposedClass> types)
        {
            return _ToJsonCollection(types, "types", ToJson);
        }

        internal static string ToJson(ExposedEnum enumType)
        {
            if (enumType == null) return "{}";

            var jObject = new JObject
            {
                ["type"] = enumType.typeName,
                ["values"] = new JArray(enumType.values.Select(v => new JObject
                {
                    ["name"] = v.name,
                    ["value"] = v.value,
                    ["displayName"] = v.displayName
                }))
            };

            // help項目を追加（nullでない場合のみ）
            if (!string.IsNullOrEmpty(enumType.help))
            {
                jObject["help"] = LocalizationSystem.Translate(enumType.help);
            }

            return JsonConvert.SerializeObject(jObject, Formatting.None);
        }

        internal static string ToJson(IEnumerable<ExposedEnum> enumTypes)
        {
            return _ToJsonCollection(enumTypes, "enums", ToJson);
        }

        internal static string ToJson(ExposedPropertyType propertyType)
        {
            // ExposedPropertyRef の場合、RemoteApp には参照先の型 (例: float) を伝える
            var valueType = propertyType.resolvedValueType;
            bool isArray = ExposedPropertyUtility.IsArrayType(valueType);

            // コレクション型の場合、型名を "ElementType[]" 形式に正規化
            // (例: List`1 → String[], IEnumerable`1 → String[])
            string typeName;
            if (isArray && valueType != null)
            {
                var elementType = ExposedPropertyUtility.GetCollectionElementType(valueType);
                typeName = elementType != null ? elementType.Name + "[]" : (valueType.Name ?? "Unknown");
            }
            else
            {
                typeName = valueType?.Name ?? "Unknown";
            }

            var controllerJObject = propertyType.controlAttribute?.ToJObject() ?? new JObject();

            // ObjectSelector: フィールド型に代入可能な ExposedObject (id 登録済み) を列挙して options に埋め込む。
            // 候補 id は component 単位で "rootId.components[N]" 形式になる (GameObject 上の該当コンポーネントを指す)。
            // 直接登録済みの対象は rootId のみで出力する。
            if (propertyType.controlAttribute is ObjectSelectorAttribute)
            {
                var fieldType = propertyType.valueType;
                var optionsArray = new JArray();
                if (fieldType != null)
                {
                    foreach (var candidate in ExposedObjectRegistry.GetByTargetType(fieldType))
                    {
                        // 直接 target マッチ
                        if (candidate.target != null && fieldType.IsAssignableFrom(candidate.target.GetType()))
                        {
                            optionsArray.Add(new JObject
                            {
                                ["id"] = candidate.id,
                                ["name"] = candidate.name,
                                ["type"] = candidate.targetTypeName,
                            });
                            continue;
                        }

                        // GameObject 経由: 該当コンポーネントの path 付き id
                        var gameObject = ExposedObjectRegistry.ResolveGameObject(candidate.target);
                        if (gameObject == null) continue;
                        if (!typeof(Component).IsAssignableFrom(fieldType)) continue;

                        var components = gameObject.GetComponents<Component>();
                        int filteredIndex = 0;
                        for (int i = 0; i < components.Length; i++)
                        {
                            var c = components[i];
                            if (c == null || !ExposedClass.Has(c.GetType())) continue;
                            if (fieldType.IsAssignableFrom(c.GetType()))
                            {
                                var compTypeName = ExposedClass.Find(c.GetType())?.typeName ?? c.GetType().Name;
                                optionsArray.Add(new JObject
                                {
                                    ["id"] = ObjectSelectorSerializer.ComposeObjectSelectorRef(candidate.id, $"components[{filteredIndex}]"),
                                    ["name"] = candidate.name,
                                    ["type"] = compTypeName,
                                });
                            }
                            filteredIndex++;
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"[RemoteControl] [ObjectSelector] property '{propertyType.name}' has null valueType");
                }
                controllerJObject["options"] = optionsArray;
            }

            var jObject = new JObject
            {
                ["name"] = propertyType.name,
                ["type"] = typeName,
                ["isReadOnly"] = propertyType.resolvedIsReadOnly,
                ["isArray"] = isArray,
                ["isPersistable"] = propertyType.isPersistable,
                ["controller"] = controllerJObject,
                ["order"] = propertyType.order
            };

            // ExposedPropertyRef: 参照先メタデータ (targetTypeName / propertyPath) を emit する。
            // RemoteApp 側はこれを見て、表示時に参照先 ExposedObject のストアエントリから値を読み、
            // SSE による参照先プロパティ更新に自動追従する。
            if (propertyType.isExposedPropertyReference
                && propertyType.fieldInfo != null
                && propertyType.fieldInfo.IsStatic)
            {
                var refValue = propertyType.fieldInfo.GetValue(null);
                if (refValue is ExposedPropertyRef pr && pr.isValid)
                {
                    jObject["ref"] = new JObject
                    {
                        ["targetTypeName"] = pr.targetTypeName,
                        ["propertyPath"] = pr.propertyPath,
                    };
                }
            }

            // label項目を追加（nullでない場合のみ）
            if (!string.IsNullOrEmpty(propertyType.label))
            {
                jObject["label"] = LocalizationSystem.Translate(propertyType.label);
            }

            // help項目を追加（nullでない場合のみ）
            if (!string.IsNullOrEmpty(propertyType.help))
            {
                jObject["help"] = LocalizationSystem.Translate(propertyType.help);
            }

            // visibility条件を追加（条件がある場合のみ）
            if (propertyType.hasVisibilityCondition)
            {
                var visibility = new JObject
                {
                    ["property"] = propertyType.visibilityConditionProperty,
                    ["value"] = JToken.FromObject(propertyType.visibilityConditionValue),
                    ["showWhenMatch"] = propertyType.visibilityShowWhenMatch,
                };
                jObject["visibility"] = visibility;
            }

            // section情報を追加（セクションがある場合のみ）
            if (propertyType.sectionAttribute != null)
            {
                var sectionAttr = propertyType.sectionAttribute;
                var section = new JObject
                {
                    ["icon"] = sectionAttr.icon,
                    ["title"] = LocalizationSystem.Translate(sectionAttr.title),
                    ["accessLevel"] = (int)propertyType.sectionAccessLevel,
                };
                if (!string.IsNullOrEmpty(sectionAttr.subtitle))
                {
                    section["subtitle"] = LocalizationSystem.Translate(sectionAttr.subtitle);
                }
                jObject["section"] = section;
            }

            return JsonConvert.SerializeObject(jObject, Formatting.None);
        }

        internal static string ToJson(ExposedFunctionType functionType)
        {
            if (functionType == null) return "{}";

            var parameters = functionType.parameters;
            var jParams = new JArray();

            if (parameters != null)
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    jParams.Add(_BuildParameterJObject(parameters[i], i));
                }
            }

            var jObject = new JObject
            {
                ["name"] = functionType.name,
                ["returnType"] = functionType.returnType?.Name ?? "Void",
                ["parameters"] = jParams,
                ["order"] = functionType.order
            };

            // controller項目を追加（nullでない場合のみ）
            if (functionType.controlAttribute != null)
            {
                jObject["controller"] = functionType.controlAttribute.ToJObject();
            }

            // label項目を追加（nullでない場合のみ）
            if (!string.IsNullOrEmpty(functionType.label))
            {
                jObject["label"] = LocalizationSystem.Translate(functionType.label);
            }

            // help項目を追加（nullでない場合のみ）
            if (!string.IsNullOrEmpty(functionType.help))
            {
                jObject["help"] = LocalizationSystem.Translate(functionType.help);
            }

            return JsonConvert.SerializeObject(jObject, Formatting.None);
        }

        /// <summary>
        /// ExposedFunction の引数 ParameterInfo を ExposedPropertyType と同等のスキーマで JObject 化する。
        /// RemoteApp 側の DynamicPropertyControl がそのまま入力 UI として描画できる形を狙う。
        /// </summary>
        private static JObject _BuildParameterJObject(ParameterInfo param, int order)
        {
            var paramType = param.ParameterType;
            bool isArray = ExposedPropertyUtility.IsArrayType(paramType);

            string typeName;
            if (isArray)
            {
                var elementType = ExposedPropertyUtility.GetCollectionElementType(paramType);
                typeName = elementType != null ? elementType.Name + "[]" : paramType.Name;
            }
            else
            {
                typeName = paramType.Name;
            }

            var controlAttr = param.GetCustomAttribute<ControlAttribute>();
            var controllerJObject = controlAttr?.ToJObject() ?? new JObject { ["type"] = "default" };

            var jObject = new JObject
            {
                ["name"] = param.Name,
                ["type"] = typeName,
                ["isReadOnly"] = false,
                ["isArray"] = isArray,
                ["isPersistable"] = false,
                ["controller"] = controllerJObject,
                ["order"] = order
            };

            // Enum 型の場合は enumType 名を emit (ExposedPropertyType と同じ運用)
            if (paramType.IsEnum)
            {
                jObject["enumType"] = paramType.Name;
            }

            // ExposedHelp 属性を help として emit (翻訳済み)
            var helpAttr = param.GetCustomAttribute<ExposedHelpAttribute>();
            if (helpAttr != null && !string.IsNullOrEmpty(helpAttr.text))
            {
                jObject["help"] = LocalizationSystem.Translate(helpAttr.text);
            }

            // 既定値 (param.HasDefaultValue が true のときのみ)
            if (param.HasDefaultValue && param.DefaultValue != null && param.DefaultValue != DBNull.Value)
            {
                jObject["defaultValue"] = JToken.FromObject(param.DefaultValue);
            }

            return jObject;
        }

        internal static string ToJson(IEnumerable<ExposedFunctionType> functionTypes)
        {
            return _ToJsonCollection(functionTypes, "functions", ToJson);
        }

        /// <summary>
        /// IEnumerableのToJsonを共通化するヘルパー。
        /// </summary>
        private static string _ToJsonCollection<T>(IEnumerable<T> items, string key, Func<T, string> toJson)
        {
            if (items == null || !items.Any()) return "[]";

            var jArray = new JArray();
            foreach (var item in items)
            {
                if (item == null) continue;
                jArray.Add(JObject.Parse(toJson(item)));
            }
            var jRoot = new JObject { [key] = jArray };
            return JsonConvert.SerializeObject(jRoot, Formatting.None);
        }

        //TODO: 引数の数分用意する
        public static string ToJsonForFunctionArgs(object value, IExposedObjectResolver resolver)
        {
            var jObject = new JObject
            {
                ["args"] = new JArray(ExposedPropertySerializer.SerializeUnityType(resolver, value))
            };

            return JsonConvert.SerializeObject(jObject, Formatting.None);
        }
    }
}
