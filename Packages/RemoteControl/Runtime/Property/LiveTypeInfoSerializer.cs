// Copyright (c) You-Ri, 2026
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// LiveClass/LiveEnum/LivePropertyType/LiveFunctionType の型スキーマ JSON 出力
    /// (REST `/live/types|enums` および関数引数スキーマ)。LivePropertySerializer から分離。
    /// コア(値直列化 SerializeUnityType)と ObjectSelectorSerializer への一方向依存のみ。
    /// </summary>
    public static class LiveTypeInfoSerializer
    {
        internal static string ToJson(LiveClass type)
        {
            if (type == null) return "{}";
            return LivePropertySerializer.SerializeToJson(ToJObject(type));
        }

        // ToJson(LiveClass) の JObject 構築部。/live/types のコレクション構築が各要素を
        // 「文字列化 → 再パース」する往復を避けるため、JObject を直接返す経路を公開する。
        internal static JObject ToJObject(LiveClass type)
        {
            var jObject = new JObject
            {
                ["type"] = type.typeName,
                // The block this type's state actually travels in, so each member can be asked
                // whether it is carried rather than what it declared (see the lane note below).
                ["properties"] = new JArray(type.propertyTypes.Select(p =>
                {
                    var pj = ToJObject(p, StateBridgeRegistry.Find(type.type));
                    // [LiveKey] のプロパティに印を付け、RemoteApp が配列要素を index でなく
                    // このプロパティ値で安定参照 ("arr[Joy].weight") できるようにする。
                    if (type.keyProperty != null && ReferenceEquals(p, type.keyProperty)) pj["isKey"] = true;
                    return pj;
                }))
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
                jObject["functions"] = new JArray(type.functionTypes.Select(f => ToJObject(f)));
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

            // baseTypes: LiveClass が公開する基底クラス連鎖（最も近い祖先が先頭）。クライアントが
            // 「この型は AvatarAssetBase の派生か？」のような多態グループ判定を、具象サブクラスを
            // 列挙せずに行えるようにする。判定ルールの定義元は LiveClass.baseTypeNames（Studio 側の
            // IsSubclassOf と共有）。hideInScene 等と同じ「条件付き追加キー」で、ユーザー定義の祖先を
            // 持つ型（AssetBase 派生など）にのみ末尾追記する。祖先が無い型（大多数、特に MonoBehaviour 系）
            // では付与しないので出力は不変のまま。scene.json は型スキーマを含まないため影響なし。
            var baseTypeNames = type.baseTypeNames;
            if (baseTypeNames.Count > 0)
            {
                jObject["baseTypes"] = new JArray(baseTypeNames);
            }

            return jObject;
        }

        internal static string ToJson(IEnumerable<LiveClass> types)
        {
            return _ToJsonCollection(types, "types", ToJObject);
        }

        internal static string ToJson(LiveEnum enumType)
        {
            if (enumType == null) return "{}";
            return LivePropertySerializer.SerializeToJson(ToJObject(enumType));
        }

        internal static JObject ToJObject(LiveEnum enumType)
        {
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

            return jObject;
        }

        internal static string ToJson(IEnumerable<LiveEnum> enumTypes)
        {
            return _ToJsonCollection(enumTypes, "enums", ToJObject);
        }

        internal static string ToJson(LivePropertyType propertyType)
            => LivePropertySerializer.SerializeToJson(ToJObject(propertyType));

        internal static JObject ToJObject(LivePropertyType propertyType, StateBridge stateBridge = null)
        {
            // LivePropertyRef の場合、RemoteApp には参照先の型 (例: float) を伝える
            var valueType = propertyType.resolvedValueType;
            bool isArray = LivePropertyUtility.IsArrayType(valueType);

            // コレクション型の場合、型名を "ElementType[]" 形式に正規化
            // (例: List`1 → String[], IEnumerable`1 → String[])
            string typeName;
            if (isArray && valueType != null)
            {
                var elementType = LivePropertyUtility.GetCollectionElementType(valueType);
                typeName = elementType != null ? elementType.Name + "[]" : (valueType.Name ?? "Unknown");
            }
            else
            {
                typeName = valueType?.Name ?? "Unknown";
            }

            var controllerJObject = propertyType.controlAttribute?.ToJObject() ?? new JObject();

            // ObjectSelector: フィールド型に代入可能な LiveObjectHandle (id 登録済み) を列挙して options に埋め込む。
            // 候補 id は component 単位で "rootId.components[N]" 形式になる (GameObject 上の該当コンポーネントを指す)。
            // 直接登録済みの対象は rootId のみで出力する。
            if (propertyType.controlAttribute is ObjectSelectorAttribute)
            {
                var fieldType = propertyType.valueType;
                var optionsArray = new JArray();
                if (fieldType != null)
                {
                    foreach (var candidate in LiveObjectRegistry.GetByTargetType(fieldType))
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
                        var gameObject = LiveObjectRegistry.ResolveGameObject(candidate.target);
                        if (gameObject == null) continue;
                        if (!typeof(Component).IsAssignableFrom(fieldType)) continue;

                        var components = gameObject.GetComponents<Component>();
                        int filteredIndex = 0;
                        for (int i = 0; i < components.Length; i++)
                        {
                            var c = components[i];
                            if (c == null || !LiveClass.Has(c.GetType())) continue;
                            if (fieldType.IsAssignableFrom(c.GetType()))
                            {
                                var compTypeName = LiveClass.Find(c.GetType())?.typeName ?? c.GetType().Name;
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

            // AssetSelector: expose the field's asset type so the client can source its options from the
            // type-filtered asset list (GET /live/assets?type=<assetType>) — one endpoint covering baked and
            // external assets — instead of resolving a fixed sourceProperty GUID list one name at a time.
            if (propertyType.controlAttribute is AssetSelectorAttribute && propertyType.valueType != null)
            {
                controllerJObject["assetType"] = propertyType.valueType.Name;
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

            // Say so when the value is written to the project settings rather than to the live
            // scene. An editor session runs neither write, so the client treats it as out of reach.
            // ⚠ Scene is left unsaid: it is the default destination, and spelling it out on every
            // member would only make the table bigger.
            if (propertyType.isPersistable && propertyType.persistScope == PersistScope.Project)
            {
                jObject["persistScope"] = "project";
            }

            // Which lane of the live data carries this member, so a client can say whether a take
            // remembers it -- and, when it does, whether it is paid for every frame or only when it
            // changes.
            //
            // ⚠ **The question is put to whatever is carrying the member, not to its declaration**,
            // which is the same rule the write path follows (see <see cref="LiveStateCarriage"/>).
            // The two disagree often enough that reading the declaration was simply wrong: a
            // [LiveField] that says nothing about its lane is carried by the generated block (the
            // generator's default for a field) while the attribute reports Event, and a member that
            // asked for State but could not reach a block falls back to events. Both were reported
            // the wrong way round.
            //
            // Event is left unsaid, on the same reasoning as Scene above: it is the default, and
            // saying it on every member would grow the table for no news.
            if (propertyType.lane == FrameLane.None)
            {
                jObject["lane"] = "none";
            }
            else if (LiveStateCarriage.IsCarriedByState(propertyType, stateBridge)
                     || LiveStructureSystem.IsRecordedCollection(propertyType))
            {
                // A collection of exposed objects is the second way onto the state lane, and it
                // does not go through the owner's block: the inventory carries the shape and each
                // element's own block carries its values. Nothing about it is ever an event, so
                // calling it one told the operator the opposite of the truth.
                jObject["lane"] = "state";
            }

            // 多態配列: 要素型に代入可能な具象 [LiveClass] 型名を列挙し、クライアントの
            // 「型を選んで要素追加」UI に渡す。プリミティブ等 (候補 0 件) では付与しない。
            if (isArray && valueType != null)
            {
                var elementType = LivePropertyUtility.GetCollectionElementType(valueType);
                if (elementType != null)
                {
                    var elementOptions = new JArray();
                    if (!elementType.IsAbstract && !elementType.IsInterface)
                    {
                        var selfClass = LiveClass.Find(elementType);
                        if (selfClass != null) elementOptions.Add(selfClass.typeName);
                    }
                    foreach (var derived in Reflection.TypeReflectionSystem.FindDerivedTypes(elementType))
                    {
                        if (derived.IsAbstract) continue;
                        var ec = LiveClass.Find(derived);
                        if (ec != null) elementOptions.Add(ec.typeName);
                    }
                    if (elementOptions.Count > 0)
                    {
                        jObject["elementTypeOptions"] = elementOptions;
                    }
                }
            }

            // LivePropertyRef: 参照先メタデータ (targetTypeName / propertyPath) を emit する。
            // RemoteApp 側はこれを見て、表示時に参照先 LiveObjectHandle のストアエントリから値を読み、
            // 変更フィードによる参照先プロパティ更新に自動追従する。
            if (propertyType.isLivePropertyReference
                && propertyType.fieldInfo != null
                && propertyType.fieldInfo.IsStatic)
            {
                var refValue = propertyType.fieldInfo.GetValue(null);
                if (refValue is LivePropertyRef pr && pr.isValid)
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

            // visibility条件を追加（条件がある場合のみ）。
            // 単一条件は従来どおり "visibility" のみを出力し（既存出力と byte 互換）、
            // 複数条件のときだけ "visibilityConditions" 配列を追加する（RemoteApp 側が AND 評価する）。
            _WriteVisibility(jObject, propertyType.visibilityConditions);

            // section情報を追加（セクションがある場合のみ）
            _WriteSection(jObject, propertyType.sectionAttribute, propertyType.sectionAccessLevel);

            // layout: セクション内の縦横グループ（[Layout] がある場合のみ付与）
            _WriteLayout(jObject, propertyType.layoutAttribute, propertyType.name);

            // collapsed: RemoteApp が配列・構造体を初期折りたたみで描画するヒント（true のときのみ付与）
            if (propertyType.collapsed)
            {
                jObject["collapsed"] = true;
            }

            return jObject;
        }

        internal static string ToJson(LiveFunctionType functionType)
        {
            if (functionType == null) return "{}";
            return LivePropertySerializer.SerializeToJson(ToJObject(functionType));
        }

        internal static JObject ToJObject(LiveFunctionType functionType)
        {
            if (functionType == null) return new JObject();

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

            // Say so when a recording does not keep this call. Event is left unsaid, as on a property:
            // it is the default, and every existing response stays byte-identical.
            // ⚠ State never reaches here -- LiveFunctionType corrects it at construction.
            if (functionType.lane == FrameLane.None)
            {
                jObject["lane"] = "none";
            }

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

            // icon項目を追加（nullでない場合のみ）。ボタンのアイコン表示に使う。
            if (!string.IsNullOrEmpty(functionType.icon))
            {
                jObject["icon"] = functionType.icon;
            }

            // help項目を追加（nullでない場合のみ）
            if (!string.IsNullOrEmpty(functionType.help))
            {
                jObject["help"] = LocalizationSystem.Translate(functionType.help);
            }

            // visibility条件を追加（ShowIf/HideIf。プロパティと同じスキーマで出力）
            _WriteVisibility(jObject, functionType.visibilityConditions);

            // layout: セクション内の縦横グループ（プロパティと同じスキーマ。ボタンの横並びに使う）
            _WriteLayout(jObject, functionType.layoutAttribute, functionType.name);

            // section: ボタンだけで構成されるセクション（プロパティと同じスキーマ）
            _WriteSection(jObject, functionType.sectionAttribute, functionType.sectionAccessLevel);

            return jObject;
        }

        /// <summary>
        /// セクション情報を JObject に書き込む。属性が無ければ何も書かない
        /// （= セクションを持たないメンバーの出力は従来と byte 単位で一致する）。
        /// プロパティと関数で同じスキーマを共有する。
        /// </summary>
        private static void _WriteSection(JObject jObject, SectionAttribute sectionAttr, AccessLevel accessLevel)
        {
            if (sectionAttr == null) return;

            var section = new JObject
            {
                ["icon"] = sectionAttr.icon,
                ["title"] = LocalizationSystem.Translate(sectionAttr.title),
                ["accessLevel"] = (int)accessLevel,
            };
            if (!string.IsNullOrEmpty(sectionAttr.subtitle))
            {
                section["subtitle"] = LocalizationSystem.Translate(sectionAttr.subtitle);
            }
            jObject["section"] = section;
        }

        /// <summary>
        /// [Layout] のグループ情報を JObject に書き込む。属性が無い / パスが空なら何も書かない
        /// （= 未使用の型の出力は従来と byte 単位で一致する）。
        /// direction の Auto はここで深さから解決して確定値を出し、解決ロジックを一箇所に閉じる。
        /// </summary>
        private static void _WriteLayout(JObject jObject, LayoutAttribute layout, string memberName)
        {
            if (layout == null) return;

            var path = _NormalizeLayoutPath(layout.path, out var depth);
            if (path == null)
            {
                Debug.LogWarning($"[RemoteControl] [Layout] on '{memberName}' has an empty path and is ignored.");
                return;
            }

            var jLayout = new JObject
            {
                ["path"] = path,
                ["direction"] = _ResolveLayoutDirection(layout.direction, depth),
            };
            if (layout.columns > 0)
            {
                jLayout["columns"] = layout.columns;
            }
            if (layout.grow != 1)
            {
                jLayout["grow"] = layout.grow;
            }
            jObject["layout"] = jLayout;
        }

        /// <summary>
        /// グループパスを正規化する。区切りごとに空白と空セグメントを落とし、"/" で連結し直す
        /// ("/row//left/" → "row/left")。有効なセグメントが無ければ null を返す。
        /// </summary>
        private static string _NormalizeLayoutPath(string path, out int depth)
        {
            depth = 0;
            if (string.IsNullOrEmpty(path)) return null;

            var segments = path.Split('/');
            var kept = new List<string>(segments.Length);
            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i].Trim();
                if (segment.Length == 0) continue;
                kept.Add(segment);
            }
            if (kept.Count == 0) return null;

            depth = kept.Count;
            return string.Join("/", kept);
        }

        /// <summary>
        /// Auto を深さから解決する。セクション直下（深さ0）は縦積みなので、深さ1で横、深さ2で縦、と交互になる。
        /// </summary>
        private static string _ResolveLayoutDirection(LayoutDirection direction, int depth)
        {
            switch (direction)
            {
                case LayoutDirection.Horizontal: return "horizontal";
                case LayoutDirection.Vertical: return "vertical";
                default: return (depth % 2) == 1 ? "horizontal" : "vertical";
            }
        }

        /// <summary>
        /// visibility 条件を JObject に書き込む。単一条件は "visibility" のみ（従来出力と byte 互換）、
        /// 複数条件のときは "visibilityConditions" 配列も併記する。条件が無ければ何も書かない。
        /// </summary>
        private static void _WriteVisibility(JObject jObject, LiveVisibilityCondition[] conditions)
        {
            if (conditions == null || conditions.Length == 0) return;

            jObject["visibility"] = _SerializeVisibilityCondition(conditions[0]);

            if (conditions.Length > 1)
            {
                var arr = new JArray();
                for (int i = 0; i < conditions.Length; i++)
                {
                    arr.Add(_SerializeVisibilityCondition(conditions[i]));
                }
                jObject["visibilityConditions"] = arr;
            }
        }

        private static JObject _SerializeVisibilityCondition(LiveVisibilityCondition condition)
        {
            return new JObject
            {
                ["property"] = condition.property,
                ["value"] = JToken.FromObject(condition.value),
                ["showWhenMatch"] = condition.showWhenMatch,
            };
        }

        /// <summary>
        /// LiveFunction の引数 ParameterInfo を LivePropertyType と同等のスキーマで JObject 化する。
        /// RemoteApp 側の DynamicPropertyControl がそのまま入力 UI として描画できる形を狙う。
        /// </summary>
        private static JObject _BuildParameterJObject(ParameterInfo param, int order)
        {
            var paramType = param.ParameterType;
            bool isArray = LivePropertyUtility.IsArrayType(paramType);

            string typeName;
            if (isArray)
            {
                var elementType = LivePropertyUtility.GetCollectionElementType(paramType);
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

            // Enum 型の場合は enumType 名を emit (LivePropertyType と同じ運用)
            if (paramType.IsEnum)
            {
                jObject["enumType"] = paramType.Name;
            }

            // Help 属性を help として emit (翻訳済み)
            var helpAttr = param.GetCustomAttribute<HelpAttribute>();
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

        internal static string ToJson(IEnumerable<LiveFunctionType> functionTypes)
        {
            return _ToJsonCollection(functionTypes, "functions", ToJObject);
        }

        /// <summary>
        /// IEnumerable の要素を JObject 化して { key: [...] } を返す共通ヘルパー。各要素を
        /// 「文字列化 → JObject.Parse で再パース」する往復を避け、<paramref name="toJObject"/> が返す
        /// JObject を直接配列に詰める。最終シリアライズはプール済みバッファ経由。
        /// </summary>
        private static string _ToJsonCollection<T>(IEnumerable<T> items, string key, Func<T, JObject> toJObject)
        {
            if (items == null || !items.Any()) return "[]";

            var jArray = new JArray();
            foreach (var item in items)
            {
                if (item == null) continue;
                jArray.Add(toJObject(item));
            }
            var jRoot = new JObject { [key] = jArray };
            return LivePropertySerializer.SerializeToJson(jRoot);
        }

        //TODO: 引数の数分用意する
        public static string ToJsonForFunctionArgs(object value, ILiveObjectResolver resolver)
        {
            var jObject = new JObject
            {
                ["args"] = new JArray(LivePropertySerializer.SerializeUnityType(resolver, value))
            };

            return JsonConvert.SerializeObject(jObject, Formatting.None);
        }
    }
}
