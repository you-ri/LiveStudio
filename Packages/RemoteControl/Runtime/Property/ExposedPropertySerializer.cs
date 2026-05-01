// Copyright (c) You-Ri, 2026
using System;
using System.Linq;
using System.Collections.Generic;

using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// ExposedPropertyのシリアライズ/デシリアライズを担当するユーティリティクラス。
    /// JTokenレベルの変換、JSON文字列API (ToJson/FromJson)、配列操作を提供する。
    /// </summary>
    public static class ExposedPropertySerializer
    {
        // -------------------------------------------------------
        // Common helpers
        // -------------------------------------------------------

        /// <summary>
        /// ObjectSelector の @ref を rootId と path に分解する。
        /// 例: "guid.components[0]" → rootId="guid", path="components[0]"
        /// </summary>
        private static void _ParseObjectSelectorRef(string refKey, out string rootId, out string path)
        {
            rootId = refKey;
            path = string.Empty;
            if (string.IsNullOrEmpty(refKey)) return;

            int dotIndex = refKey.IndexOf('.');
            int bracketIndex = refKey.IndexOf('[');
            int splitIndex = -1;
            if (dotIndex >= 0 && bracketIndex >= 0) splitIndex = System.Math.Min(dotIndex, bracketIndex);
            else if (dotIndex >= 0) splitIndex = dotIndex;
            else if (bracketIndex >= 0) splitIndex = bracketIndex;

            if (splitIndex < 0) return;
            rootId = refKey.Substring(0, splitIndex);
            path = refKey[splitIndex] == '.' ? refKey.Substring(splitIndex + 1) : refKey.Substring(splitIndex);
        }

        /// <summary>
        /// ObjectSelector の @ref を組み立てる。path が空なら rootId のみ。
        /// </summary>
        private static string _ComposeObjectSelectorRef(string rootId, string path)
        {
            if (string.IsNullOrEmpty(path)) return rootId;
            return path[0] == '[' ? rootId + path : rootId + "." + path;
        }

        /// <summary>
        /// GameObject の components (ExposedClass フィルタ済み) の中で target コンポーネントの index を返す。
        /// ExposedGameObject._components と同じフィルタで計算する (RemoteApp 表示と一致させるため)。
        /// 見つからなければ -1。
        /// </summary>
        private static int _FindFilteredComponentIndex(GameObject gameObject, Component target)
        {
            if (gameObject == null || target == null) return -1;
            var components = gameObject.GetComponents<Component>();
            int index = 0;
            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) continue;
                if (!ExposedClass.Has(c.GetType())) continue;
                if (c == target) return index;
                index++;
            }
            return -1;
        }

        /// <summary>
        /// GameObject を包む ExposedObject (ExposedGameObject など) を検索する。
        /// </summary>
        private static ExposedObject _FindGameObjectWrapper(GameObject gameObject)
        {
            if (gameObject == null) return null;
            foreach (var candidate in ExposedObjectRegistry.instances)
            {
                if (candidate == null || !candidate.hasId) continue;
                var wrappedGO = ExposedObjectRegistry.ResolveGameObject(candidate.target);
                if (wrappedGO == gameObject) return candidate;
            }
            return null;
        }

        /// <summary>
        /// ObjectSelector フィールドの値を @ref 形式にシリアライズする。
        /// - 直接登録済み (value 自身が ExposedObject.target) → その id を @ref に使用
        /// - Component かつ所属 GameObject を包む ExposedObject があれば → "rootId.components[N]" 形式で @ref
        /// - wrapper が見つからなければ null (未選択扱い)
        /// </summary>
        internal static JToken SerializeObjectSelectorValue(object value, bool forPersistence)
        {
            if (value == null) return JValue.CreateNull();
            if (value is UnityEngine.Object uo && uo == null) return JValue.CreateNull();

            // 1) 直接登録済み: value 自身が ExposedObject.target なら、その id を @ref に使う
            var direct = ExposedObjectRegistry.FindByTarget(value);
            if (direct != null && direct.hasId)
            {
                var directResult = new JObject
                {
                    ["@type"] = direct.targetTypeName,
                    ["@ref"] = direct.id,
                };
                if (!forPersistence)
                {
                    directResult["@name"] = direct.name;
                    if (value is UnityEngine.Object unityValue && unityValue != null)
                    {
                        directResult["@instanceID"] = unityValue.GetInstanceID().ToString();
                    }
                }
                return directResult;
            }

            // 2) GameObject 経由: components[N] path 付き @ref を生成する
            if (value is Component component)
            {
                var gameObject = component.gameObject;
                var wrapper = _FindGameObjectWrapper(gameObject);
                if (wrapper == null) return JValue.CreateNull();

                int index = _FindFilteredComponentIndex(gameObject, component);
                if (index < 0) return JValue.CreateNull();

                var refKey = _ComposeObjectSelectorRef(wrapper.id, $"components[{index}]");
                var componentTypeName = ExposedClass.Find(component.GetType())?.typeName ?? component.GetType().Name;
                var result = new JObject
                {
                    ["@type"] = componentTypeName,
                    ["@ref"] = refKey,
                };
                if (!forPersistence)
                {
                    result["@name"] = component.name;
                    result["@instanceID"] = component.GetInstanceID().ToString();
                }
                return result;
            }

            return JValue.CreateNull();
        }

        /// <summary>
        /// ObjectSelector フィールドへの代入: token (@ref JObject) を解決して fieldType に沿う値を返す。
        /// @ref が path 付き ("rootId.components[N]" など) なら rootExposed.FindProperty(path) で辿る。
        /// 解決値が fieldType に代入不可なら GameObject.GetComponent(fieldType) でフォールバック。
        /// </summary>
        internal static object DeserializeObjectSelectorValue(IExposedObjectResolver resolver, JToken token, Type fieldType)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (!(token is JObject jObj)) return null;
            var refKey = jObj["@ref"]?.Value<string>();
            if (string.IsNullOrEmpty(refKey)) return null;

            _ParseObjectSelectorRef(refKey, out var rootId, out var path);
            var rootExposed = resolver.FindById(rootId);
            if (rootExposed == null) return null;

            // path 無し: ルート target
            if (string.IsNullOrEmpty(path))
            {
                var rootTarget = rootExposed.target;
                if (rootTarget != null && fieldType.IsAssignableFrom(rootTarget.GetType())) return rootTarget;
                if (typeof(Component).IsAssignableFrom(fieldType))
                {
                    var gameObject = ExposedObjectRegistry.ResolveGameObject(rootTarget);
                    if (gameObject != null) return gameObject.GetComponent(fieldType);
                }
                return null;
            }

            // path 付き: FindProperty で辿る (components[N] 等)
            var property = rootExposed.FindProperty(path);
            if (!property.HasValue) return null;
            var resolved = property.Value.GetValue();
            if (resolved != null && fieldType.IsAssignableFrom(resolved.GetType())) return resolved;
            return null;
        }

        /// <summary>
        /// JObject から propType に対応する値 JToken を取得する。現在の name で見つからない場合は
        /// [FormerlyExposedAs] で宣言された旧名も順に試す。リネーム後の旧シーンファイル読み込みに使用。
        /// </summary>
        private static JToken _GetTokenByPropertyName(JObject jObject, ExposedPropertyType propType)
        {
            var token = jObject[propType.name];
            if (token != null) return token;
            var formers = propType.formerNames;
            if (formers == null) return null;
            for (int i = 0; i < formers.Length; i++)
            {
                token = jObject[formers[i]];
                if (token != null) return token;
            }
            return null;
        }

        /// <summary>
        /// シリアライズ時にプロパティをスキップすべきか判定する。
        /// </summary>
        private static bool _ShouldSkipProperty(ExposedPropertyType propType, bool forPersistence)
        {
            if (!propType.isValid) return true;
            if (forPersistence && !propType.isPersistable) return true;
            if (forPersistence && propType.isReadOnly && !propType.containsExposedObjectReference) return true;
            return false;
        }

        /// <summary>
        /// 値型のように inline 展開すべき Unity 型（Texture2D はバイナリ埋め込み用途のため含む）。
        /// これらは file-scope でも fileid 参照にしない。
        /// </summary>
        private static bool _IsInlineUnityValueType(Type type)
        {
            return type == typeof(Texture2D);
        }

        /// <summary>
        /// ExposedObjectのメタデータ(@type, @id, @name)を含むJObjectを生成する。
        /// </summary>
        private static JObject _CreateMetadataJObject(ExposedObject exposedObject, bool forPersistence = false)
        {
            var jObject = new JObject();
            jObject["@type"] = exposedObject.targetTypeName;
            if (exposedObject.hasId)
            {
                jObject["@id"] = exposedObject.id;
            }
            // UnityEngine.Object ターゲットは instanceID を別メタフィールドとして付与する。
            // RemoteApp 側の副次インデックスでルーティング翻訳に使う。
            // 永続化 (scene/studio.json など) には含めない — instanceID はセッション依存。
            if (!forPersistence
                && exposedObject.target is UnityEngine.Object unityObj
                && unityObj != null)
            {
                jObject["@instanceID"] = unityObj.GetInstanceID().ToString();
            }
            // @name は RemoteApp 側の表示用。永続化には不要。
            if (!forPersistence)
            {
                jObject["@name"] = exposedObject.name;
            }

            // インスタンス単位のアイコン上書き。未設定(null/empty)ならクラス側のアイコンにフォールバックされる。
            // 永続化時は不要 — アイコンはランタイムで再解決される。
            if (exposedObject.target is ExposedUnityObjectBase proxyBase)
            {
                if (!forPersistence)
                {
                    var iconOverride = proxyBase.GetIconOverride();
                    if (!string.IsNullOrEmpty(iconOverride))
                    {
                        jObject["@icon"] = iconOverride;
                    }
                }

                // 親ExposedObject id。ルートなら出力しない。永続化時も含める。
                if (!string.IsNullOrEmpty(proxyBase.parentId))
                {
                    jObject["@parent"] = proxyBase.parentId;
                }
            }

            return jObject;
        }

        // -------------------------------------------------------
        // JToken Serialization
        // -------------------------------------------------------

        // Unity型をJTokenにシリアライズする共通メソッド
        internal static JToken SerializeUnityType(IExposedObjectResolver resolver, object value, bool forceValue = false, bool forPersistence = false)
        {
            Debug.Assert(resolver != null, "Resolver cannot be null");

            // Unity型の場合は Unity の == 演算子で null チェック（破壊されたオブジェクトを検出）
            if (value is UnityEngine.Object unityObj && unityObj == null)
                return JValue.CreateNull();

            if (value == null) return JValue.CreateNull();

            // ExposedObjectインスタンスが直接渡された場合は参照としてシリアライズ
            // （静的クラスのExposedObjectはtargetがnullのため、FindByTargetで解決できない）
            if (value is ExposedObject exposedObj && exposedObj.hasId)
            {
                var refObj = new JObject
                {
                    ["@type"] = exposedObj.targetType.typeName,
                    ["@ref"] = exposedObj.id,
                };
                if (!forPersistence) refObj["@name"] = exposedObj.name;
                return refObj;
            }

            // ファイル（シーン）スコープ時は UnityEngine.Object 参照を fileid ベースの
            // @ref に置き換え、実体は別エントリとして objects[] に書き出す。
            // REST API 等の通常スコープでは従来どおりインライン展開/既存 @ref を維持する。
            if (value is UnityEngine.Object fileScopeUnityObj
                && resolver is IFileScopedResolver fileResolver
                && !_IsInlineUnityValueType(fileScopeUnityObj.GetType()))
            {
                return fileResolver.EncodeUnityObjectReference(fileScopeUnityObj);
            }

            var valueType = value.GetType();

            // Unity型の判定（高速化のため最初に実行）
            if (valueType == typeof(Color)) return _SerializeColor((Color)value);
            if (valueType == typeof(Vector3)) return _SerializeVector3((Vector3)value);
            if (valueType == typeof(Vector2)) return _SerializeVector2((Vector2)value);
            if (valueType == typeof(Quaternion)) return _SerializeQuaternion((Quaternion)value);
            if (valueType == typeof(Rect)) return _SerializeRect((Rect)value);
            if (valueType == typeof(TransformValue)) return _SerializeTransformValue((TransformValue)value);
            if (valueType == typeof(Texture2D)) return _SerializeTexture2D((Texture2D)value);

            // プリミティブ型とEnum
            if (value is string or int or float or bool or double)
                return JToken.FromObject(value);

            if (value is System.Enum)
                return JToken.FromObject(value.ToString());

            if (value is System.Collections.IEnumerable collection && !(value is string))
                return _SerializeArray(resolver, collection, forPersistence);

            return SerializeExposedObject(resolver, value, forceValue, forPersistence);
        }

        // コレクションをJArrayにシリアライズ
        private static JToken _SerializeArray(IExposedObjectResolver resolver, System.Collections.IEnumerable collection, bool forPersistence = false)
        {
            Debug.Assert(resolver != null, "Resolver cannot be null");

            var jArray = new JArray();
            var fileResolver = resolver as IFileScopedResolver;
            int index = 0;
            foreach (var item in collection)
            {
                fileResolver?.PushPath($"[{index}]");
                if (item == null)
                {
                    jArray.Add(JValue.CreateNull());
                }
                else
                {
                    var serializedItem = SerializeUnityType(resolver, item, forPersistence: forPersistence);
                    jArray.Add(serializedItem ?? JValue.CreateNull());
                }
                fileResolver?.PopPath();
                index++;
            }
            return jArray;
        }

        public static JToken SerializeExposedObject(IExposedObjectResolver resolver, object value, bool forceValue, bool forPersistence = false)
        {
            Debug.Assert(resolver != null, "Resolver cannot be null");
            Debug.Assert(value != null, "Value cannot be null");

            var valueType = value.GetType();
            var exposedClass = ExposedClass.Find(valueType);

            // ExposedClassが存在する場合は、定義されたプロパティのみをシリアライズ
            if (exposedClass != null)
            {
                var jObject = new JObject();
                jObject["@type"] = exposedClass.typeName;
                var valueExposedObject = resolver.FindByTarget(value);

                // ID付きExposedObject（コンテナ管理）の場合は参照情報のみをシリアライズ
                // IDなしまたは未登録の場合はプロパティをインライン展開
                if (valueExposedObject != null && valueExposedObject.hasId && forceValue == false)
                {
                    jObject["@ref"] = valueExposedObject.id;
                    if (!forPersistence)
                    {
                        jObject["@name"] = valueExposedObject.name;
                        if (value is UnityEngine.Object refUnityObj && refUnityObj != null)
                        {
                            jObject["@instanceID"] = refUnityObj.GetInstanceID().ToString();
                        }
                    }
                    return jObject;
                }

                // インライン展開時も UnityEngine.Object であれば instanceID を付与する。
                // RemoteApp 側の副次インデックスで後続 SSE のルーティング翻訳に使う。
                // 永続化にはセッション依存の instanceID を含めない。
                if (!forPersistence
                    && value is UnityEngine.Object inlineUnityObj
                    && inlineUnityObj != null)
                {
                    jObject["@instanceID"] = inlineUnityObj.GetInstanceID().ToString();
                }

                if (!forPersistence && valueExposedObject != null)
                {
                    jObject["@name"] = valueExposedObject.name;
                }
                var fileResolver = resolver as IFileScopedResolver;
                foreach (var propType in exposedClass.propertyTypes)
                {
                    if (_ShouldSkipProperty(propType, forPersistence)) continue;

                    // ExposedPropertyRef は参照先の値に展開。baseline には含めない
                    if (propType.isExposedPropertyReference)
                    {
                        if (forPersistence) continue;
                        var refRaw = ExposedPropertyUtility.GetValueRaw(value, propType);
                        if (refRaw is ExposedPropertyRef pr)
                        {
                            var resolved = pr.Resolve();
                            var resolvedValue = resolved?.GetValue();
                            fileResolver?.PushPath(propType.name);
                            var serializedRef = SerializeUnityType(resolver, resolvedValue, propType.forceValue, forPersistence);
                            fileResolver?.PopPath();
                            if (serializedRef != null)
                            {
                                jObject[propType.name] = serializedRef;
                            }
                        }
                        continue;
                    }

                    var propValue = ExposedPropertyUtility.GetValueRaw(value, propType);
                    fileResolver?.PushPath(propType.name);
                    var serializedValue = SerializeUnityType(resolver, propValue, propType.forceValue, forPersistence);
                    fileResolver?.PopPath();

                    if (serializedValue != null)
                    {
                        jObject[propType.name] = serializedValue;
                    }
                }
                return jObject;
            }

            // ExposedClass 未登録のプレーンな [Serializable] POCO は
            // UnityEngine.JsonUtility で汎用的にシリアライズする
            // (AvatarInputSettings のようなデータクラス用)
            try
            {
                var json = JsonUtility.ToJson(value);
                if (!string.IsNullOrEmpty(json))
                {
                    var jToken = JToken.Parse(json);
                    if (jToken is JObject jObject)
                    {
                        jObject["@type"] = valueType.Name;
                        return jObject;
                    }
                    return jToken;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[RemoteControl] Failed to serialize {valueType.Name} with JsonUtility: {ex.Message}");
            }

            Debug.LogError($"[RemoteControl] ExposedClass not found for type {valueType.Name} and JsonUtility serialization failed.");
            return JValue.CreateNull();
        }

        // -------------------------------------------------------
        // JToken Deserialization
        // -------------------------------------------------------

        // JTokenからUnity型にデシリアライズする共通メソッド
        // 既存のインスタンスに上書き、インスタンスがnullの場合は新規作成
        public static object DeserializeUnityType(IExposedObjectResolver resolver, JToken token, System.Type type, object instance = null)
        {
            Debug.Assert(resolver != null, "Resolver cannot be null");

            if (type == null) throw new System.ArgumentNullException(nameof(type));
            if (token == null || token.Type == JTokenType.Null) return instance;

            // 基本型の処理
            if (type == typeof(string) || type == typeof(int) || type == typeof(bool) ||
                type == typeof(float) || type == typeof(double) || type == typeof(long) ||
                type == typeof(short) || type == typeof(byte))
            {
                return token.ToObject(type);
            }
            // Unity型の処理
            // Unity型の処理: デルタロード時はinstanceの既存値をフォールバックに使用
            // （JSONに含まれないフィールドはデフォルト値ではなく既存値を保持する）
            else if (type == typeof(Color) && token is JObject colorObj)
            {
                var existing = instance is Color ec ? ec : new Color(0f, 0f, 0f, 1f);
                float r = colorObj["r"]?.Value<float>() ?? existing.r;
                float g = colorObj["g"]?.Value<float>() ?? existing.g;
                float b = colorObj["b"]?.Value<float>() ?? existing.b;
                float a = colorObj["a"]?.Value<float>() ?? existing.a;
                return new Color(r, g, b, a);
            }
            else if (type == typeof(Vector3) && token is JObject vec3Obj)
            {
                var existing = instance is Vector3 ev3 ? ev3 : Vector3.zero;
                float x = vec3Obj["x"]?.Value<float>() ?? existing.x;
                float y = vec3Obj["y"]?.Value<float>() ?? existing.y;
                float z = vec3Obj["z"]?.Value<float>() ?? existing.z;
                return new Vector3(x, y, z);
            }
            else if (type == typeof(Vector2) && token is JObject vec2Obj)
            {
                var existing = instance is Vector2 ev2 ? ev2 : Vector2.zero;
                float x = vec2Obj["x"]?.Value<float>() ?? existing.x;
                float y = vec2Obj["y"]?.Value<float>() ?? existing.y;
                return new Vector2(x, y);
            }
            else if (type == typeof(Quaternion) && token is JObject quatObj)
            {
                var existing = instance is Quaternion eq ? eq : Quaternion.identity;
                float x = quatObj["x"]?.Value<float>() ?? existing.x;
                float y = quatObj["y"]?.Value<float>() ?? existing.y;
                float z = quatObj["z"]?.Value<float>() ?? existing.z;
                float w = quatObj["w"]?.Value<float>() ?? existing.w;
                return new Quaternion(x, y, z, w);
            }
            else if (type == typeof(Rect) && token is JObject rectObj)
            {
                var existing = instance is Rect er ? er : Rect.zero;
                float x = rectObj["x"]?.Value<float>() ?? existing.x;
                float y = rectObj["y"]?.Value<float>() ?? existing.y;
                float width = rectObj["width"]?.Value<float>() ?? existing.width;
                float height = rectObj["height"]?.Value<float>() ?? existing.height;
                return new Rect(x, y, width, height);
            }
            else if (type == typeof(TransformValue) && token is JObject trsObj)
            {
                var existing = instance is TransformValue et ? et : TransformValue.identity;
                var pos = (Vector3)DeserializeUnityType(resolver, trsObj["position"], typeof(Vector3), existing.position);
                var rot = (Quaternion)DeserializeUnityType(resolver, trsObj["rotation"], typeof(Quaternion), existing.rotation);
                var scl = (Vector3)DeserializeUnityType(resolver, trsObj["scale"], typeof(Vector3), existing.scale);
                return new TransformValue(pos, rot, scl);
            }
            else if (type == typeof(Texture2D) && token is JObject texObj)
            {
                int width = texObj["width"]?.Value<int>() ?? 0;
                int height = texObj["height"]?.Value<int>() ?? 0;
                string formatStr = texObj["format"]?.Value<string>() ?? "RGBA32";
                string base64Image = texObj["image"]?.Value<string>();

                if (width <= 0 || height <= 0 || string.IsNullOrEmpty(base64Image))
                {
                    Debug.LogWarning("[RemoteControl] Invalid Texture2D data in JSON");
                    return null;
                }

                try
                {
                    // TextureFormatをパース
                    TextureFormat format = TextureFormat.RGBA32;
                    if (!System.Enum.TryParse(formatStr, out format))
                    {
                        Debug.LogWarning($"[RemoteControl] Unknown texture format '{formatStr}', using RGBA32");
                        format = TextureFormat.RGBA32;
                    }

                    // Texture2Dを作成
                    var texture = new Texture2D(width, height, format, false);

                    // Base64デコード
                    byte[] imageData = System.Convert.FromBase64String(base64Image);

                    // 画像データを読み込み
                    if (!texture.LoadImage(imageData))
                    {
                        Debug.LogError("[RemoteControl] Failed to load image data into Texture2D");
                        UnityEngine.Object.Destroy(texture);
                        return null;
                    }

                    return texture;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[RemoteControl] Error deserializing Texture2D: {ex.Message}");
                    return null;
                }
            }
            else
            {
                // 配列・コレクション型の処理
                if (ExposedPropertyUtility.IsArrayType(type))
                {
                    return _DeserializeCollection(resolver, token, type, instance);
                }
                // Enum型の処理（文字列形式と整数形式の両方をサポート）
                else if (type.IsEnum)
                {
                    try
                    {
                        if (token.Type == JTokenType.String)
                        {
                            return System.Enum.Parse(type, token.Value<string>());
                        }
                        else if (token.Type == JTokenType.Integer)
                        {
                            return System.Enum.ToObject(type, token.Value<int>());
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[RemoteControl] Failed to parse enum {type.Name}: {ex.Message}");
                    }
                    return System.Activator.CreateInstance(type);
                }
                // カスタムクラス（Unity型用Converterで処理）
                else
                {
                    return DeserializeExposedObject(resolver, token, type, instance);
                }
            }
        }

        // Array/List共通のデシリアライズ
        private static object _DeserializeCollection(IExposedObjectResolver resolver, JToken token, System.Type collectionType, object instance = null)
        {
            Debug.Assert(resolver != null, "Resolver cannot be null");

            if (token.Type != JTokenType.Array) return instance;

            var jArray = (JArray)token;
            var elementType = ExposedPropertyUtility.GetCollectionElementType(collectionType);
            bool isArray = collectionType.IsArray;

            // デルタ形式判定
            if (IsArrayDeltaFormat(jArray))
            {
                return _DeserializeCollectionDelta(resolver, jArray, elementType, isArray, instance);
            }

            int count = jArray.Count;

            if (isArray)
            {
                var existingArray = instance as System.Array;
                var array = System.Array.CreateInstance(elementType, count);

                for (int i = 0; i < count; i++)
                {
                    var existing = existingArray != null && i < existingArray.Length ? existingArray.GetValue(i) : ExposedPropertyUtility.CreateDefaultElement(elementType);
                    var element = DeserializeExposedObject(resolver, jArray[i], elementType, existing);
                    array.SetValue(element, i);
                }
                return array;
            }
            else
            {
                var existingList = instance as System.Collections.IList;
                var listType = typeof(List<>).MakeGenericType(elementType);
                var list = System.Activator.CreateInstance(listType) as System.Collections.IList;

                for (int i = 0; i < count; i++)
                {
                    var existing = existingList != null && i < existingList.Count ? existingList[i] : ExposedPropertyUtility.CreateDefaultElement(elementType);
                    var element = DeserializeExposedObject(resolver, jArray[i], elementType, existing);
                    list.Add(element);
                }
                return list;
            }
        }

        // Array/List共通のデルタ形式デシリアライズ
        private static object _DeserializeCollectionDelta(IExposedObjectResolver resolver, JArray jArray, System.Type elementType, bool isArray, object existingCollection)
        {
            // @op: "new" 要素のカウント
            int newCount = 0;
            foreach (var element in jArray)
            {
                if (element is JObject obj && obj["@op"]?.ToString() == "new")
                    newCount++;
            }

            // 既存コレクションの情報取得
            int existingLength;
            Func<int, object> getExisting;
            if (isArray)
            {
                var existingArray = existingCollection as System.Array;
                existingLength = existingArray != null ? existingArray.Length : 0;
                getExisting = i => existingArray?.GetValue(i);
            }
            else
            {
                var existingList = existingCollection as System.Collections.IList;
                existingLength = existingList != null ? existingList.Count : 0;
                getExisting = i => existingList?[i];
            }

            int totalLength = existingLength + newCount;

            // 結果コレクション生成と既存要素コピー + デフォルト初期化
            Action<int, object> setElement;
            Action<object> addElement;
            object result;

            if (isArray)
            {
                var array = System.Array.CreateInstance(elementType, totalLength);
                for (int i = 0; i < existingLength; i++)
                    array.SetValue(getExisting(i), i);
                for (int i = existingLength; i < totalLength; i++)
                    array.SetValue(ExposedPropertyUtility.CreateDefaultElement(elementType), i);
                setElement = (i, v) => array.SetValue(v, i);
                addElement = null; // Arrayでは不使用
                result = array;
            }
            else
            {
                var listType = typeof(List<>).MakeGenericType(elementType);
                var list = System.Activator.CreateInstance(listType) as System.Collections.IList;
                for (int i = 0; i < existingLength; i++)
                    list.Add(getExisting(i));
                setElement = (i, v) => list[i] = v;
                addElement = v => list.Add(v);
                result = list;
            }

            // デルタ適用
            int existingIndex = 0;
            int addedIndex = existingLength;
            for (int i = 0; i < jArray.Count; i++)
            {
                var element = jArray[i];
                if (element is JObject obj && obj["@op"]?.ToString() == "new")
                {
                    if (isArray)
                    {
                        if (addedIndex < totalLength)
                        {
                            var newElement = DeserializeExposedObject(resolver, element, elementType, ((System.Array)result).GetValue(addedIndex));
                            setElement(addedIndex, newElement);
                            addedIndex++;
                        }
                    }
                    else
                    {
                        var newElement = ExposedPropertyUtility.CreateDefaultElement(elementType);
                        newElement = DeserializeExposedObject(resolver, element, elementType, newElement);
                        addElement(newElement);
                    }
                }
                else
                {
                    if (existingIndex < existingLength)
                    {
                        // 空オブジェクトまたは@refのみでない場合のみマージ
                        if (element is JObject deltaObj && !IsEmptyOrRefOnly(deltaObj))
                        {
                            var existing = isArray ? ((System.Array)result).GetValue(existingIndex) : ((System.Collections.IList)result)[existingIndex];
                            var merged = DeserializeExposedObject(resolver, element, elementType, existing);
                            setElement(existingIndex, merged);
                        }
                    }
                    existingIndex++;
                }
            }

            return result;
        }

        /// <summary>
        /// ExposedClassとインスタンスを解決する。
        /// instanceがnullの場合はtypeからExposedClassを検索しデフォルトインスタンスを生成。
        /// instanceがある場合はその実行時型からExposedClassを検索。
        /// </summary>
        private static ExposedClass _ResolveExposedClassAndInstance(Type type, ref object instance)
        {
            if (instance == null)
            {
                instance = ExposedPropertyUtility.CreateDefaultElement(type);
                return ExposedClass.Find(type);
            }
            return ExposedClass.Find(instance.GetType());
        }

        // カスタムオブジェクトのデシリアライズ
        public static object DeserializeExposedObject(IExposedObjectResolver resolver, JToken token, System.Type type, object instance = null)
        {
            Debug.Assert(resolver != null, "Resolver cannot be null");

            if (token == null || token.Type == JTokenType.Null) return instance;

            // @ref フィールドをチェック（参照型の場合）
            if (token is JObject refObj)
            {
                var referenceId = refObj["@ref"]?.Value<string>();
                if (!string.IsNullOrEmpty(referenceId))
                {
                    // file-scope @ref: @type を持たず、id 文字列で参照する形式
                    // ExposedObjectFileRegistry から UnityEngine.Object を復元
                    if (refObj["@type"] == null)
                    {
                        if (ExposedObjectFileRegistry.TryGetObject(referenceId, out var fileObj))
                        {
                            return fileObj;
                        }
                        Debug.LogWarning($"[RemoteControl] File-scope @ref not resolved: id={referenceId}");
                        return instance;
                    }

                    // @refがある場合は@typeも必ず存在する
                    var typeName = refObj["@type"]?.Value<string>();
                    Debug.Assert(!string.IsNullOrEmpty(typeName), "@ref requires @type field");

                    var refExposedClass = ExposedClass.Find(typeName);
                    if (refExposedClass == null)
                    {
                        Debug.LogWarning($"ExposedClass not found for type: {typeName}");
                        return null;
                    }

                    // resolver を使って参照を解決する
                    var referencedObject = resolver.FindById(referenceId);
                    if (referencedObject != null)
                    {
                        return referencedObject.target;
                    }

                    // ExposedObjectがまだ生成されていない場合
                    if (referencedObject == null && instance != null)
                    {
                        ExposedObjectRegistry.Create(refExposedClass.type, instance, referenceId);
                        return instance;
                    }

                    Debug.LogWarning($"[RemoteControl] Failed to resolve. reference: {referenceId} type: {typeName}");
                    return instance;
                }
            }

            ExposedClass exposedClass;

            // @type フィールドで型切り替え対応
            if (token is JObject typeCheckObj)
            {
                var requestedTypeName = typeCheckObj["@type"]?.Value<string>();
                if (!string.IsNullOrEmpty(requestedTypeName))
                {
                    var requestedExposedClass = ExposedClass.Find(requestedTypeName);
                    if (requestedExposedClass != null)
                    {
                        if (instance == null || instance.GetType() != requestedExposedClass.type)
                            instance = ExposedPropertyUtility.CreateDefaultElement(requestedExposedClass.type);
                        exposedClass = requestedExposedClass;
                    }
                    else
                    {
                        // フォールバック: ExposedClass未登録の型名
                        exposedClass = _ResolveExposedClassAndInstance(type, ref instance);
                    }
                }
                else
                {
                    // @typeなし: 既存の動作
                    exposedClass = _ResolveExposedClassAndInstance(type, ref instance);
                }
            }
            else
            {
                // JObjectでない場合: 既存の動作
                exposedClass = _ResolveExposedClassAndInstance(type, ref instance);
            }

            // ExposedClassが存在する場合は、定義されたプロパティのみをデシリアライズ
            if (exposedClass != null && token is JObject jObject)
            {
                // instanceがnullの場合（MonoBehaviour等、Activatorで生成不可の型）はスキップ
                if (instance == null) return null;

                // 定義されたプロパティのみをデシリアライズして設定
                foreach (var propType in exposedClass.propertyTypes)
                {
                    if (!propType.isValid) continue;
                    if (propType.isReadOnly) continue;

                    var propToken = _GetTokenByPropertyName(jObject, propType);
                    if (propToken == null || propToken.Type == JTokenType.Null) continue;

                    // 既存のプロパティ値を取得して渡す
                    var existingValue = ExposedPropertyUtility.GetValueRaw(instance, propType);
                    var propValue = DeserializeUnityType(resolver, propToken, propType.valueType, existingValue);
                    if (propValue != null)
                    {
                        ExposedPropertyUtility.SetValueRaw(instance, propType, propValue);
                    }
                }

                return instance;
            }

            // ExposedClass 未登録のプレーンな [Serializable] POCO は
            // UnityEngine.JsonUtility で汎用的にデシリアライズする
            try
            {
                // @type フィールドを除去
                if (token is JObject jObj && jObj.ContainsKey("@type"))
                {
                    jObj = (JObject)jObj.DeepClone();
                    jObj.Remove("@type");
                    token = jObj;
                }

                var json = token.ToString();
                if (!string.IsNullOrEmpty(json))
                {
                    // 値型は常に新規作成（dirty検出のため参照を変える必要がある）
                    if (type.IsValueType)
                    {
                        return JsonUtility.FromJson(json, type);
                    }

                    // 参照型: 既存のインスタンスがあればそれを更新、なければ新規作成
                    if (instance != null)
                    {
                        JsonUtility.FromJsonOverwrite(json, instance);
                        return instance;
                    }
                    else
                    {
                        return JsonUtility.FromJson(json, type);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[RemoteControl] Failed to deserialize {type.Name} with JsonUtility: {ex.Message}");
            }

            // フォールバック：Activatorで新規インスタンスを作成
            if (instance == null && !type.IsAbstract && !type.IsInterface)
            {
                try
                {
                    return System.Activator.CreateInstance(type);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[RemoteControl] Failed to create instance of {type.Name}: {ex.Message}");
                }
            }

            return instance;
        }

        // -------------------------------------------------------
        // Delta format helpers
        // -------------------------------------------------------

        /// <summary>
        /// 配列JSONがデルタ形式かどうかを判定する。
        /// デルタ形式: 要素にJObjectが含まれ、かつ@op要素が存在するか、空の{}要素が存在する場合。
        /// </summary>
        internal static bool IsArrayDeltaFormat(JArray jArray)
        {
            if (jArray.Count == 0) return false;
            foreach (var element in jArray)
            {
                if (element is JObject obj)
                {
                    // @op がある → デルタ形式
                    if (obj["@op"] != null) return true;
                    // 空オブジェクト（未変更マーカー） → デルタ形式
                    // @ref/@name のみのオブジェクト（参照スタブ）もデルタ形式の未変更マーカー
                    // ただし、@typeがある場合は旧スタブ形式なので除外
                    if (obj["@type"] != null) continue;
                    if (IsEmptyOrRefOnly(obj)) return true;
                }
            }
            return false;
        }


        /// <summary>
        /// JObjectが空か、@ref/@nameのみのメタデータオブジェクト（デルタ形式の未変更マーカー）かを判定
        /// </summary>
        internal static bool IsEmptyOrRefOnly(JObject obj)
        {
            if (obj.Count == 0) return true;
            foreach (var prop in obj.Properties())
            {
                if (!prop.Name.StartsWith("@")) return false;
            }
            return true;
        }


        /// <summary>
        /// 結果ベースのdirty判定: @プレフィックス以外のプロパティが存在するか
        /// </summary>
        internal static bool HasNonMetaProperties(JObject jObj)
        {
            foreach (var p in jObj.Properties())
            {
                if (!p.Name.StartsWith("@")) return true;
            }
            return false;
        }

        // -------------------------------------------------------
        // Unity type serialize helpers
        // -------------------------------------------------------

        private static JToken _SerializeColor(Color c)
            => JObject.FromObject(new { r = c.r, g = c.g, b = c.b, a = c.a });

        private static JToken _SerializeVector3(Vector3 v)
            => JObject.FromObject(new { x = v.x, y = v.y, z = v.z });

        private static JToken _SerializeVector2(Vector2 v)
            => JObject.FromObject(new { x = v.x, y = v.y });

        private static JToken _SerializeQuaternion(Quaternion q)
            => JObject.FromObject(new { x = q.x, y = q.y, z = q.z, w = q.w });

        private static JToken _SerializeRect(Rect r)
            => JObject.FromObject(new { x = r.x, y = r.y, width = r.width, height = r.height });

        private static JToken _SerializeTransformValue(TransformValue t)
            => new JObject
            {
                ["position"] = _SerializeVector3(t.position),
                ["rotation"] = _SerializeQuaternion(t.rotation),
                ["scale"] = _SerializeVector3(t.scale),
            };

        private static JToken _SerializeTexture2D(Texture2D tex)
        {
            if (tex == null)
                return JValue.CreateNull();

            try
            {
                // 読み取り可能かチェック
                if (!tex.isReadable)
                {
                    Debug.LogWarning($"[RemoteControl] Texture2D '{tex.name}' is not readable and cannot be serialized");
                    return JObject.FromObject(new { width = tex.width, height = tex.height, format = tex.format.ToString(), image = "" });
                }

                // PNG形式でエンコード
                byte[] pngData = tex.EncodeToPNG();
                if (pngData == null || pngData.Length == 0)
                {
                    Debug.LogWarning($"[RemoteControl] Failed to encode Texture2D '{tex.name}' to PNG");
                    return JObject.FromObject(new { width = tex.width, height = tex.height, format = tex.format.ToString(), image = "" });
                }

                // Base64エンコード
                string base64Image = System.Convert.ToBase64String(pngData);

                return JObject.FromObject(new
                {
                    width = tex.width,
                    height = tex.height,
                    format = tex.format.ToString(),
                    image = base64Image
                });
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RemoteControl] Error serializing Texture2D '{tex.name}': {ex.Message}");
                return JObject.FromObject(new { width = 0, height = 0, format = "Unknown", image = "" });
            }
        }

        // -------------------------------------------------------
        // JSON Diff (Delta serialization)
        // -------------------------------------------------------

        /// <summary>
        /// ExposedObjectをフルシリアライズしてJObjectを返す（dirty判定なし）。
        /// ExposedObjectDefaultRegistryおよびdelta serialization用にinternalで公開。
        /// </summary>
        internal static JObject SerializeFullToJObject(ExposedObject exposedObject, IExposedObjectResolver resolver, bool forPersistence = false)
        {
            return SerializeFullToJObject(exposedObject, resolver, forPersistence, skipPropertyRef: forPersistence);
        }

        /// <summary>
        /// <see cref="SerializeFullToJObject"/> の拡張版。
        /// <paramref name="skipPropertyRef"/> が true の場合、<see cref="ExposedPropertyType.isExposedPropertyReference"/>
        /// のフィールドを出力から除外する。baseline/dirty比較用など、参照先の値を含めたくない用途に使う。
        /// </summary>
        internal static JObject SerializeFullToJObject(ExposedObject exposedObject, IExposedObjectResolver resolver, bool forPersistence, bool skipPropertyRef)
        {
            if (exposedObject == null) return new JObject();

            var properties = exposedObject.propertyTypes;
            var jObject = _CreateMetadataJObject(exposedObject, forPersistence);

            // persistentオブジェクトのマーク（永続化JSONには含めない）
            if (!forPersistence && resolver is ExposedObjectContainer container
                && exposedObject.hasId && container.IsPersistent(exposedObject.id))
            {
                jObject["@persistent"] = true;
            }

            var fileResolver = resolver as IFileScopedResolver;

            foreach (var propertyType in properties)
            {
                if (_ShouldSkipProperty(propertyType, forPersistence)) continue;

                // ExposedPropertyRef: 値は参照先の実プロパティから取る。
                // baseline / dirty比較 では含めない — 参照先が baseline を持つため。
                if (propertyType.isExposedPropertyReference)
                {
                    if (skipPropertyRef) continue;
                    var refRaw = ExposedPropertyUtility.GetValueRaw(exposedObject.target, propertyType);
                    if (refRaw is ExposedPropertyRef pr)
                    {
                        var resolved = pr.Resolve();
                        var resolvedValue = resolved?.GetValue();
                        fileResolver?.PushPath(propertyType.name);
                        var serializedRef = SerializeUnityType(resolver, resolvedValue, propertyType.forceValue, forPersistence);
                        fileResolver?.PopPath();
                        jObject[propertyType.name] = serializedRef ?? JValue.CreateNull();
                    }
                    else
                    {
                        jObject[propertyType.name] = JValue.CreateNull();
                    }
                    continue;
                }

                var value = ExposedPropertyUtility.GetValueRaw(exposedObject.target, propertyType);

                // ObjectSelector: GameObject 側の ExposedObject を @ref として出力する
                if (propertyType.controlAttribute is ObjectSelectorAttribute)
                {
                    jObject[propertyType.name] = SerializeObjectSelectorValue(value, forPersistence);
                    continue;
                }

                fileResolver?.PushPath(propertyType.name);
                var serializedValue = SerializeUnityType(resolver, value, propertyType.forceValue, forPersistence);
                fileResolver?.PopPath();

                if (serializedValue != null)
                {
                    jObject[propertyType.name] = serializedValue;
                }
                else
                {
                    jObject[propertyType.name] = "{}";
                }
            }

            return jObject;
        }

        /// <summary>
        /// 2つのJTokenを再帰比較し、差分のみを含むJTokenを返す。
        /// 差分がなければnullを返す。
        /// forPersistence=trueの場合、配列の末尾省略を行わない（永続化時のデータ消失防止）。
        /// </summary>
        internal static JToken JsonDiff(JToken defaultToken, JToken currentToken, bool forPersistence = false)
        {
            if (defaultToken == null && currentToken == null) return null;
            if (defaultToken == null) return currentToken.DeepClone();
            if (currentToken == null) return JValue.CreateNull();

            // 両方JObject
            if (defaultToken is JObject defaultObj && currentToken is JObject currentObj)
            {
                return _JsonDiffObject(defaultObj, currentObj, forPersistence);
            }

            // 両方JArray
            if (defaultToken is JArray defaultArr && currentToken is JArray currentArr)
            {
                return _JsonDiffArray(defaultArr, currentArr, forPersistence);
            }

            // プリミティブまたは型が異なる場合
            if (!JToken.DeepEquals(defaultToken, currentToken))
                return currentToken.DeepClone();

            return null;
        }

        /// <summary>
        /// JObject同士の差分を計算する。
        /// @プレフィックスのメタデータはcurrentからコピーする。
        /// </summary>
        private static JToken _JsonDiffObject(JObject defaultObj, JObject currentObj, bool forPersistence = false)
        {
            var result = new JObject();

            foreach (var prop in currentObj.Properties())
            {
                // メタデータキーはそのままコピー
                if (prop.Name.StartsWith("@"))
                {
                    result[prop.Name] = prop.Value.DeepClone();
                    continue;
                }

                var defaultValue = defaultObj[prop.Name];
                if (defaultValue == null)
                {
                    // default側にない → 追加されたプロパティ
                    result[prop.Name] = prop.Value.DeepClone();
                }
                else
                {
                    var diff = JsonDiff(defaultValue, prop.Value, forPersistence);
                    if (diff != null)
                    {
                        result[prop.Name] = diff;
                    }
                }
            }

            // メタデータ以外のプロパティが残っているか
            if (!HasNonMetaProperties(result)) return null;

            return result;
        }

        /// <summary>
        /// JArray同士の差分を計算する。
        /// 要素がJObjectの場合はデルタ形式（{}=未変更、@op:new=追加）で出力。
        /// プリミティブ配列はDeepEqualsで全体比較し、異なればcurrentをそのまま返す。
        /// </summary>
        private static JToken _JsonDiffArray(JArray defaultArr, JArray currentArr, bool forPersistence = false)
        {
            // 空配列同士
            if (defaultArr.Count == 0 && currentArr.Count == 0) return null;

            // 要素がJObjectかどうかで分岐（ExposedClass配列 vs プリミティブ配列）
            bool hasObjectElements = false;
            foreach (var elem in currentArr)
            {
                if (elem is JObject) { hasObjectElements = true; break; }
            }
            if (!hasObjectElements)
            {
                foreach (var elem in defaultArr)
                {
                    if (elem is JObject) { hasObjectElements = true; break; }
                }
            }

            // プリミティブ配列: 全体比較
            if (!hasObjectElements)
            {
                if (JToken.DeepEquals(defaultArr, currentArr)) return null;
                return currentArr.DeepClone();
            }

            // ExposedClass配列: デルタ形式
            var elements = new List<JToken>();
            int defaultCount = defaultArr.Count;
            int lastNonEmptyIndex = -1;
            int lastModifiedExistingIndex = -1;

            for (int i = 0; i < currentArr.Count; i++)
            {
                if (i < defaultCount)
                {
                    // 既存要素: 再帰比較
                    var diff = JsonDiff(defaultArr[i], currentArr[i], forPersistence);
                    if (diff == null)
                    {
                        // 未変更マーカー
                        elements.Add(new JObject());
                    }
                    else
                    {
                        elements.Add(diff);
                        lastNonEmptyIndex = i;
                        lastModifiedExistingIndex = i;
                    }
                }
                else
                {
                    // 新規要素: @op付き
                    var newElement = currentArr[i].DeepClone();
                    if (newElement is JObject newObj)
                    {
                        // デフォルトテンプレートと比較して差分のみ出力
                        var templateDefault = _GetDefaultTemplate(defaultArr, currentArr[i]);
                        if (templateDefault != null)
                        {
                            var elementDiff = _JsonDiffNewElement(templateDefault, currentArr[i]);
                            elementDiff["@op"] = "new";
                            elements.Add(elementDiff);
                        }
                        else
                        {
                            newObj["@op"] = "new";
                            elements.Add(newObj);
                        }
                    }
                    else
                    {
                        elements.Add(newElement);
                    }
                    lastNonEmptyIndex = i;
                }
            }

            // 全要素が未変更なら差分なし
            if (lastNonEmptyIndex < 0) return null;

            // 位置マーカー（空{}）は「最後に変更された既存要素」より後ろに出力しても意味がない。
            // @op:"new" 要素はデシリアライズ時に常に末尾へ追加されるため、
            // 先行する未変更マーカーで位置合わせする必要はない。
            // これにより append-only ケースでは既存要素分の空マーカーがすべて省略され、
            // forPersistence=true でも出力が簡潔になる。
            //
            // forPersistence=false ではさらに末尾の未変更要素を省略する（従来挙動）。
            var jArray = new JArray();
            int upperBound = forPersistence ? elements.Count : lastNonEmptyIndex + 1;
            for (int i = 0; i < upperBound; i++)
            {
                // 最後に変更された既存要素より後ろの空マーカーは位置合わせに不要なので落とす。
                if (i > lastModifiedExistingIndex
                    && elements[i] is JObject emptyCheck
                    && emptyCheck.Count == 0)
                {
                    continue;
                }
                jArray.Add(elements[i]);
            }

            // 省略でデルタ形式の識別子（@op/空{}）がすべて失われた場合、
            // 空マーカーを1つ追加してデルタ形式として認識可能にする
            if (jArray.Count > 0 && !IsArrayDeltaFormat(jArray))
            {
                jArray.Add(new JObject());
            }

            return jArray;
        }

        /// <summary>
        /// 新規配列要素のデフォルトテンプレートを取得する。
        /// defaultArrayに要素があればその最初の要素を使用し、
        /// なければ@typeからExposedClassのデフォルトインスタンスをシリアライズして返す。
        /// </summary>
        private static JToken _GetDefaultTemplate(JArray defaultArr, JToken currentElement)
        {
            // defaultに既存要素があればそれをテンプレートとして使用
            if (defaultArr.Count > 0) return defaultArr[0];

            // @typeからデフォルトインスタンスを生成
            if (currentElement is JObject currentObj)
            {
                var typeName = currentObj["@type"]?.Value<string>();
                if (!string.IsNullOrEmpty(typeName))
                {
                    var exposedClass = ExposedClass.Find(typeName);
                    if (exposedClass != null)
                    {
                        var defaultInstance = ExposedPropertyUtility.CreateDefaultElement(exposedClass.type);
                        if (defaultInstance != null)
                        {
                            return SerializeUnityType(DefaultExposedObjectResolver.Instance, defaultInstance);
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 新規配列要素のデルタを計算する。
        /// テンプレート（デフォルト値のJSON）と比較し、差分プロパティのみを含むJObjectを返す。
        /// </summary>
        private static JObject _JsonDiffNewElement(JToken templateDefault, JToken current)
        {
            if (templateDefault is JObject templateObj && current is JObject currentObj)
            {
                var result = new JObject();
                foreach (var prop in currentObj.Properties())
                {
                    if (prop.Name.StartsWith("@"))
                    {
                        result[prop.Name] = prop.Value.DeepClone();
                        continue;
                    }

                    var templateValue = templateObj[prop.Name];
                    if (templateValue == null || !JToken.DeepEquals(templateValue, prop.Value))
                    {
                        result[prop.Name] = prop.Value.DeepClone();
                    }
                }
                return result;
            }

            // フォールバック: currentをそのまま返す
            return current.DeepClone() as JObject ?? new JObject();
        }

        // -------------------------------------------------------
        // ToJson (string JSON API)
        // -------------------------------------------------------

        internal static string ToJson(ExposedObject exposedObject, IExposedObjectResolver resolver, bool isDirtyOnly = false, bool forPersistence = false)
        {
            if (exposedObject == null) return "{}";

            // Delta mode: フルシリアライズ + JsonDiff方式
            if (isDirtyOnly)
            {
                return _ToJsonDelta(exposedObject, resolver, forPersistence);
            }

            // Snapshot mode: 従来のフルシリアライズ
            return _ToJsonFull(exposedObject, resolver, forPersistence);
        }

        /// <summary>
        /// Delta mode: デフォルトJSONとcurrentJSONを比較し、差分のみを出力する。
        /// </summary>
        private static string _ToJsonDelta(ExposedObject exposedObject, IExposedObjectResolver resolver, bool forPersistence)
        {
            // 1. currentのフルシリアライズ
            var currentJson = SerializeFullToJObject(exposedObject, resolver, forPersistence);

            // 2. デフォルトJSONの取得（ExposedObjectDefaultRegistryから）
            // デフォルトがない場合は差分なし（currentをデフォルトとみなす）。
            var defaultJson = ExposedObjectDefaultRegistry.GetDefaults(exposedObject);
            if (defaultJson == null)
            {
                defaultJson = currentJson;
            }

            // 3. JSON差分（forPersistence時は配列の末尾省略を行わない）
            var deltaToken = JsonDiff(defaultJson, currentJson, forPersistence);

            // 4. dirty追跡外の参照型プロパティ（非ExposedClass）は
            //    getter が動的にインスタンスを生成するケースで JSON Diff をすり抜ける可能性があるため、
            //    current と default を直接比較して差分があれば強制的にデルタに含める
            deltaToken = _ForceIncludeUntrackedProperties(exposedObject, currentJson, defaultJson, deltaToken, forPersistence);
            if (deltaToken == null || (deltaToken is JObject deltaCheck && !HasNonMetaProperties(deltaCheck)))
            {
                // 差分なし: メタデータのみ出力
                return JsonConvert.SerializeObject(_CreateMetadataJObject(exposedObject, forPersistence), Formatting.None);
            }

            // 4. メタデータを確保（JsonDiffが@typeなどを保持しているはずだが念のため）
            var deltaJson = deltaToken as JObject ?? new JObject();
            var metaTemplate = _CreateMetadataJObject(exposedObject, forPersistence);
            foreach (var metaProp in metaTemplate.Properties())
            {
                if (deltaJson[metaProp.Name] == null)
                    deltaJson.AddFirst(new JProperty(metaProp.Name, metaProp.Value));
            }

            return JsonConvert.SerializeObject(deltaJson, Formatting.None);
        }

        /// <summary>
        /// dirty追跡外の参照型プロパティ（ExposedClassでないクラス型）を、
        /// current と default を JSON レベルで直接比較し、差分があればデルタに強制含める。
        /// これらのプロパティは getter が動的にインスタンスを生成するケース等で
        /// 通常の JsonDiff をすり抜ける可能性があるため、念のため直接比較する。
        /// readOnlyプロパティは除外する。差分が無ければデルタには含めない
        /// （未操作の再生終了で objects[] が空になるようにするため）。
        /// </summary>
        private static JToken _ForceIncludeUntrackedProperties(ExposedObject exposedObject, JObject currentJson, JObject defaultJson, JToken deltaToken, bool forPersistence)
        {
            var properties = exposedObject.propertyTypes;
            JObject deltaObj = null;

            foreach (var propertyType in properties)
            {
                if (!propertyType.isValid) continue;
                if (propertyType.isReadOnly) continue;
                if (forPersistence && !propertyType.isPersistable) continue;

                var value = ExposedPropertyUtility.GetValueRaw(exposedObject.target, propertyType);
                if (value == null) continue;

                // 値型・string・ExposedClass登録型はJSON Diffで正確に検出されるのでスキップ
                var valueType = value.GetType();
                if (valueType.IsValueType || value is string) continue;
                if (ExposedClass.Find(valueType) != null) continue;

                // コレクション型で要素型がExposedClassの場合もスキップ
                // （空コレクションでも要素型で判定する。実行時の要素だけで判定すると空配列がすり抜ける）
                if (value is System.Collections.IEnumerable collection && !(value is string))
                {
                    bool isExposedClassCollection = false;

                    // まず要素型から判定（空コレクションでも正しく判定できる）
                    var elementType = ExposedPropertyUtility.GetCollectionElementType(propertyType.valueType);
                    if (elementType != null && ExposedClass.Find(elementType) != null)
                    {
                        isExposedClassCollection = true;
                    }

                    // 要素型で判定できない場合は実際の要素から判定
                    if (!isExposedClassCollection)
                    {
                        foreach (var item in collection)
                        {
                            if (item != null && ExposedClass.Find(item.GetType()) != null)
                            {
                                isExposedClassCollection = true;
                                break;
                            }
                        }
                    }
                    if (isExposedClassCollection) continue;
                }

                // dirty追跡外の参照型: current と default を比較して差分があれば含める
                var currentValue = currentJson[propertyType.name];
                if (currentValue == null) continue;

                var defaultValue = defaultJson?[propertyType.name];
                if (defaultValue != null && JToken.DeepEquals(currentValue, defaultValue))
                    continue;

                if (deltaObj == null)
                {
                    deltaObj = (deltaToken as JObject)?.DeepClone() as JObject ?? new JObject();
                    // メタデータをコピー
                    if (deltaObj["@type"] == null && currentJson["@type"] != null)
                        deltaObj["@type"] = currentJson["@type"].DeepClone();
                    if (deltaObj["@id"] == null && currentJson["@id"] != null)
                        deltaObj["@id"] = currentJson["@id"].DeepClone();
                    if (deltaObj["@name"] == null && currentJson["@name"] != null)
                        deltaObj["@name"] = currentJson["@name"].DeepClone();
                    if (deltaObj["@parent"] == null && currentJson["@parent"] != null)
                        deltaObj["@parent"] = currentJson["@parent"].DeepClone();
                }
                deltaObj[propertyType.name] = currentValue.DeepClone();
            }

            return deltaObj ?? deltaToken;
        }

        /// <summary>
        /// Snapshot mode: 全プロパティをシリアライズする（dirty判定なし）。
        /// </summary>
        private static string _ToJsonFull(ExposedObject exposedObject, IExposedObjectResolver resolver, bool forPersistence)
        {
            var jObject = SerializeFullToJObject(exposedObject, resolver, forPersistence);
            return JsonConvert.SerializeObject(jObject, Formatting.None);
        }

        internal static string ToJson(IEnumerable<ExposedObject> exposedObjects, IExposedObjectResolver resolver)
        {
            if (exposedObjects == null || !exposedObjects.Any()) return "[]";

            var jArray = new JArray();

            foreach (var exposedObj in exposedObjects)
            {
                if (exposedObj == null) continue;
                if (exposedObj.propertyTypes == null) continue;

                // 既存のToJsonを使用してプロパティをシリアライズ
                var jsonString = ToJson(exposedObj, resolver);
                var jObject = JsonConvert.DeserializeObject<JObject>(jsonString);
                jArray.Add(jObject);
            }

            var jResult = new JObject
            {
                ["objects"] = jArray
            };
            return JsonConvert.SerializeObject(jResult, Formatting.None);
        }

        public static string ToJson(object value, IExposedObjectResolver resolver)
        {
            var jObject = new JObject
            {
                ["value"] = SerializeUnityType(resolver, value),
            };

            return JsonConvert.SerializeObject(jObject, Formatting.None);
        }

        public static string ToJson(object value)
        {
            return ToJson(value, DefaultExposedObjectResolver.Instance);
        }

        public static string ToJson(ExposedProperty property, IExposedObjectResolver resolver)
        {
            JToken valueToken;
            if (property.type.controlAttribute is ObjectSelectorAttribute)
            {
                valueToken = SerializeObjectSelectorValue(property.GetValue(), forPersistence: false);
            }
            else
            {
                valueToken = SerializeUnityType(resolver, property.GetValue());
            }

            var jObject = new JObject
            {
                ["value"] = valueToken,
                ["id"] = property.owner.id,
                ["path"] = property.path.ToSlash(),
                ["changed"] = new JValue(property.isDirty)
            };

            return JsonConvert.SerializeObject(jObject, Formatting.None);
        }

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

            // 引数なしの関数のみをfunctions配列に追加
            var parameterlessFunctions = type.functionTypes
                .Where(f => f.parameters == null || f.parameters.Length == 0)
                .ToArray();
            if (parameterlessFunctions.Length > 0)
            {
                jObject["functions"] = new JArray(parameterlessFunctions.Select(f => JsonConvert.DeserializeObject<JObject>(ToJson(f))));
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
                                    ["id"] = _ComposeObjectSelectorRef(candidate.id, $"components[{filteredIndex}]"),
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
                foreach (var param in parameters)
                {
                    jParams.Add(new JObject
                    {
                        ["name"] = param.Name,
                        ["type"] = param.ParameterType.Name
                    });
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
                ["args"] = new JArray(SerializeUnityType(resolver, value))
            };

            return JsonConvert.SerializeObject(jObject, Formatting.None);
        }

        // -------------------------------------------------------
        // FromJson (string JSON API)
        // -------------------------------------------------------

        public static object FromJson(string json, System.Type type, IExposedObjectResolver resolver)
        {
            if (string.IsNullOrEmpty(json)) return null;

            var jObject = JsonConvert.DeserializeObject<JObject>(json);
            if (jObject == null) return null;

            var token = jObject["value"];
            if (token == null || token.Type == JTokenType.Null) return null;

            return DeserializeUnityType(resolver, token, type);
        }

        public static object FromJson(string json, System.Type type)
        {
            return FromJson(json, type, DefaultExposedObjectResolver.Instance);
        }

        public static T FromJson<T>(string json, IExposedObjectResolver resolver)
        {
            return (T)FromJson(json, typeof(T), resolver);
        }

        public static T FromJson<T>(string json)
        {
            return (T)FromJson(json, typeof(T), DefaultExposedObjectResolver.Instance);
        }

        internal static bool FromJson(string json, in ExposedProperty property, IExposedObjectResolver resolver)
        {
            if (string.IsNullOrEmpty(json)) return false;

            var jObject = JsonConvert.DeserializeObject<JObject>(json);
            Debug.Assert(jObject != null, "Failed to parse JSON");

            var token = jObject["value"];

            // UnityEngine.Object 派生フィールドは null 代入を許容する (ObjectSelector の None 選択など)
            var valueType = property.type.valueType;
            bool isUnityObjectField = valueType != null
                && typeof(UnityEngine.Object).IsAssignableFrom(valueType);

            if (token == null || token.Type == JTokenType.Null)
            {
                if (!isUnityObjectField) return false;
                // _FromJsonProperty 内の UnityEngine.Object null 分岐に委譲する
            }

            // _FromJsonProperty を使用して、子プロパティのみをdirtyにマーク
            return _FromJsonProperty(resolver, token, property);
        }

        internal static bool FromJson(string json, in ExposedProperty property)
        {
            return FromJson(json, property, DefaultExposedObjectResolver.Instance);
        }

        /// <summary>
        /// 新規追加された配列要素のデフォルト値キャプチャと参照型インスタンス置き換えを行う。
        /// SetDefaultValueでデフォルト値をキャプチャ後、参照型は新しいインスタンスに置き換える
        /// （デフォルト値参照がデシリアライズで変更されないようにする）。
        /// </summary>
        private static void _InitializeNewArrayElements(ExposedProperty property, Type elementType, int startIndex)
        {
            for (int i = startIndex; i < property.arrayLength; i++)
            {
                var elementProp = property.GetPropertyIndex(i);
                if (elementProp != null)
                {
                    elementProp.Value.SetDefaultValue();
                }
            }

            if (!elementType.IsValueType)
            {
                for (int i = startIndex; i < property.arrayLength; i++)
                {
                    var elementProp = property.GetPropertyIndex(i);
                    if (elementProp != null)
                    {
                        var freshInstance = ExposedPropertyUtility.CreateDefaultElement(elementType);
                        elementProp.Value.SetValue(freshInstance, captureDefault: false);
                    }
                }
            }
        }

        /// <summary>
        /// プロパティに対して再帰的にデシリアライズとSetValueを行う
        /// 子プロパティも含めて全てSetValue経由で設定し、EnsureDefaultCapturedが呼ばれるようにする
        /// </summary>
        private static bool _FromJsonProperty(IExposedObjectResolver resolver, JToken token, ExposedProperty property)
        {
            return _FromJsonProperty(resolver, token, property, captureDefaults: true);
        }

        private static bool _FromJsonProperty(IExposedObjectResolver resolver, JToken token, ExposedProperty property, bool captureDefaults)
        {
            // PropertyRef: 参照先の型でデシリアライズし、SetValue で参照先に委譲する
            if (property.type != null && property.type.isExposedPropertyReference)
            {
                var resolvedType = property.type.resolvedValueType;
                if (resolvedType == null || resolvedType == typeof(ExposedPropertyRef))
                {
                    return false;
                }
                if (token == null || token.Type == JTokenType.Null) return false;
                var refExistingValue = property.GetValue();
                var deserialized = DeserializeUnityType(resolver, token, resolvedType, refExistingValue);
                property.SetValue(deserialized, captureDefault: captureDefaults);
                return true;
            }

            var valueTypeForNull = property.type.valueType;

            // UnityEngine.Object 派生フィールドに null token を受け取った場合は明示的に null 代入する。
            // (ObjectSelector の None 選択で RemoteApp が送ってくるケース)
            if ((token == null || token.Type == JTokenType.Null)
                && valueTypeForNull != null
                && typeof(UnityEngine.Object).IsAssignableFrom(valueTypeForNull))
            {
                property.SetValue(null, captureDefault: captureDefaults);
                return true;
            }

            if (token == null || token.Type == JTokenType.Null) return false;

            var existingValue = property.GetValue();
            var valueType = property.type.valueType;

            // ObjectSelector: @ref を解決して fieldType に沿う値 (Component など) を代入する。
            // wrapper が GameObject を指す場合は GetComponent(fieldType) で取り出す。
            if (property.type.controlAttribute is ObjectSelectorAttribute
                && valueType != null
                && typeof(UnityEngine.Object).IsAssignableFrom(valueType))
            {
                var resolvedValue = DeserializeObjectSelectorValue(resolver, token, valueType);
                property.SetValue(resolvedValue, captureDefault: captureDefaults);
                return true;
            }

            // 配列の場合: 配列全体を設定後、各要素も再帰処理
            if (property.isArray && token is JArray jArray)
            {
                // デルタ形式判定: @op要素があるか確認
                bool isDelta = IsArrayDeltaFormat(jArray);

                if (isDelta)
                {
                    return _FromJsonPropertyArrayDelta(resolver, jArray, property, captureDefaults);
                }

                // 既存配列の長さを記録
                int existingLength = property.arrayLength;

                // 配列のサイズを調整 - デシリアライズ前に行う
                var elementType = ExposedPropertyUtility.GetCollectionElementType(property.type.valueType);

                // 読み取り専用でない場合のみ配列サイズを調整
                if (!property.type.isReadOnly)
                {
                    // 配列のサイズを拡張（必要な場合）
                    while (property.arrayLength < jArray.Count)
                    {
                        // CreateDefaultElementで新規要素を追加
                        var defaultElement = ExposedPropertyUtility.CreateDefaultElement(elementType);
                        property.Add(defaultElement);
                    }

                    // 配列のサイズを縮小（必要な場合）
                    while (property.arrayLength > jArray.Count)
                    {
                        property.RemoveAt(property.arrayLength - 1);
                    }

                    // 新規要素のデフォルト値キャプチャとインスタンス置き換え
                    _InitializeNewArrayElements(property, elementType, existingLength);
                }

                // 各要素をデシリアライズ
                for (int i = 0; i < property.arrayLength && i < jArray.Count; i++)
                {
                    var elementProp = property.GetPropertyIndex(i);
                    if (elementProp != null)
                    {
                        _FromJsonProperty(resolver, jArray[i], elementProp.Value, captureDefaults);
                    }
                }
                return true;
            }

            // オブジェクト/構造体の場合: 子プロパティも再帰処理
            if (!property.type.isUnityPrimtive && token is JObject jObject)
            {
                // デフォルト値キャプチャ（SceneFromJson経由ではスキップ：
                // デフォルトはContainer.Initializeで既にキャプチャ済み。
                // DeserializeExposedObject内のSetValueRawが先に値を変更するため、
                // ここでキャプチャすると変更後の値がデフォルトになってしまう）
                if (captureDefaults)
                {
                    _EnsureDefaultsCapturedRecursive(jObject, property);
                }

                var value = DeserializeUnityType(resolver, token, valueType, existingValue);

                // SetValueを子プロパティ再帰の前に実行（型切り替え後の参照を親に反映するため）
                property.SetValue(value, captureDefault: captureDefaults);

                // 実際の型でExposedClassを取得（型切り替え後の型を使用）
                var actualType = value != null ? value.GetType() : null;
                var exposedClass = actualType != null ? ExposedClass.Find(actualType) : null;

                // 子プロパティを再帰処理
                if (exposedClass != null && exposedClass.propertyTypes != null)
                {
                    // ExposedClassが登録されている場合はpropertyTypesを使用
                    foreach (var childType in exposedClass.propertyTypes)
                    {
                        var childToken = jObject[childType.name];
                        if (childToken != null && childToken.Type != JTokenType.Null)
                        {
                            var childProp = property.GetProperty(childType.name);
                            if (childProp != null)
                            {
                                _FromJsonProperty(resolver, childToken, childProp.Value, captureDefaults);
                            }
                        }
                    }
                }
                else
                {
                    // ExposedClassがない場合はJSONキーに基づいて子プロパティを処理
                    foreach (var jsonProperty in jObject.Properties())
                    {
                        // メタデータキー（@type, @id, @ref, @name）はスキップ
                        if (jsonProperty.Name.StartsWith("@")) continue;

                        var childProp = property.GetProperty(jsonProperty.Name);
                        if (childProp != null && jsonProperty.Value != null && jsonProperty.Value.Type != JTokenType.Null)
                        {
                            _FromJsonProperty(resolver, jsonProperty.Value, childProp.Value, captureDefaults);
                        }
                    }
                }

                return true;
            }

            // プリミティブ/Unity型の場合: 単純にSetValue（dirtyマーキングあり）
            var simpleValue = DeserializeUnityType(resolver, token, valueType, existingValue);
            if (simpleValue != null)
            {
                property.SetValue(simpleValue);
                return true;
            }

            return false;
        }

        /// <summary>
        /// デルタ形式の配列をデシリアライズする。
        /// @opなし要素: 既存要素に対しJSONにあるプロパティのみ上書き（部分マージ）。
        /// @op: "new" 要素: 新規インスタンスを生成し全プロパティを適用して末尾に追加。
        /// デルタ配列長 &lt; 既存配列長: 残りはデフォルト値をそのまま使用。
        /// </summary>
        private static bool _FromJsonPropertyArrayDelta(IExposedObjectResolver resolver, JArray jArray, ExposedProperty property, bool captureDefaults = true)
        {
            var elementType = ExposedPropertyUtility.GetCollectionElementType(property.type.valueType);

            int existingLength = property.arrayLength;

            // まず@op: "new" 要素の数をカウント
            int newCount = 0;
            foreach (var element in jArray)
            {
                if (element is JObject obj && obj["@op"]?.ToString() == "new")
                    newCount++;
            }

            // 既存要素のデルタ更新数（@opでない要素数）
            int deltaExistingCount = jArray.Count - newCount;

            // 冪等性確保: 以前のデルタロードで追加された@op:new要素を除去してから追加
            // デフォルト配列長を超えた要素は以前のロードで追加されたものなので除去する
            if (newCount > 0 && !property.type.isReadOnly)
            {
                var defaultValue = property.owner.GetDefaultValue(property.path.Value);
                int defaultLength = 0;
                if (defaultValue is System.Collections.ICollection defaultCol)
                    defaultLength = defaultCol.Count;
                else if (defaultValue is System.Array defaultArr)
                    defaultLength = defaultArr.Length;

                if (existingLength > defaultLength)
                {
                    for (int i = existingLength - 1; i >= defaultLength; i--)
                    {
                        property.RemoveAt(i);
                    }
                    existingLength = property.arrayLength;
                }
            }

            // @op: "new" 要素分だけ配列を拡張
            if (!property.type.isReadOnly)
            {
                for (int i = 0; i < newCount; i++)
                {
                    var defaultElement = ExposedPropertyUtility.CreateDefaultElement(elementType);
                    property.Add(defaultElement);
                }

                // 新規要素のデフォルト値キャプチャとインスタンス置き換え
                _InitializeNewArrayElements(property, elementType, existingLength);
            }

            // デルタ適用
            int existingIndex = 0;
            int addedIndex = existingLength; // 追加要素は元の配列末尾から
            for (int i = 0; i < jArray.Count; i++)
            {
                var element = jArray[i];
                if (element is JObject obj && obj["@op"]?.ToString() == "new")
                {
                    // 追加要素: 全プロパティを適用
                    if (addedIndex < property.arrayLength)
                    {
                        var elementProp = property.GetPropertyIndex(addedIndex);
                        if (elementProp != null)
                        {
                            _FromJsonProperty(resolver, element, elementProp.Value, captureDefaults);
                        }
                        addedIndex++;
                    }
                }
                else
                {
                    // 既存要素: 部分マージ（空オブジェク���{}の場合は何もしない = デフォルト値保持）
                    if (existingIndex < existingLength)
                    {
                        var elementProp = property.GetPropertyIndex(existingIndex);
                        if (elementProp != null && element is JObject deltaObj && !IsEmptyOrRefOnly(deltaObj))
                        {
                            _FromJsonProperty(resolver, element, elementProp.Value, captureDefaults);
                        }
                    }
                    existingIndex++;
                }
            }

            return true;
        }

        /// <summary>
        /// 親のSetValueで値が変わる前に、JSONで更新される子プロパティのデフォルト値を再帰的にキャプチャする
        /// </summary>
        private static void _EnsureDefaultsCapturedRecursive(JObject jObject, ExposedProperty property)
        {
            foreach (var jsonProperty in jObject.Properties())
            {
                if (jsonProperty.Name.StartsWith("@")) continue;
                if (jsonProperty.Value == null || jsonProperty.Value.Type == JTokenType.Null) continue;

                var childProp = property.GetProperty(jsonProperty.Name);
                if (childProp == null) continue;

                childProp.Value.owner.EnsureDefaultCaptured(childProp.Value.path);

                // 子がオブジェクト/構造体の場合、さらに再帰
                if (jsonProperty.Value is JObject childJObject)
                {
                    _EnsureDefaultsCapturedRecursive(childJObject, childProp.Value);
                }
            }
        }

        internal static bool FromJson(string json, ExposedObject exposedObject, IExposedObjectResolver resolver, bool captureDefaults = true)
        {
            if (string.IsNullOrEmpty(json)) return false;
            if (exposedObject == null) throw new System.ArgumentNullException(nameof(exposedObject));

            var jObject = JsonConvert.DeserializeObject<JObject>(json);
            if (jObject == null)
            {
                Debug.LogWarning("[RemoteControl] Failed to parse JSON");
                return false;
            }

            // type と id フィールドの検証（オプション）
            var jsonType = jObject["@type"]?.Value<string>();
            var jsonId = jObject["@id"]?.Value<string>();

            if (!string.IsNullOrEmpty(jsonType) && jsonType != exposedObject.targetTypeName)
            {
                Debug.LogWarning($"[RemoteControl] Type mismatch: expected '{exposedObject.targetTypeName}', got '{jsonType}'");
            }

            // exposedObject.id が空のケース (CreateUnregistered した pending エントリ用の一時オブジェクト等)
            // は比較対象が無いためスキップする。
            if (!string.IsNullOrEmpty(jsonId) && exposedObject.hasId && jsonId != exposedObject.id)
            {
                Debug.LogWarning($"[RemoteControl] ID mismatch: expected '{exposedObject.id}', got '{jsonId}'");
            }

            // @name を name プロパティに復元
            var jsonName = jObject["@name"]?.Value<string>();
            if (!string.IsNullOrEmpty(jsonName))
            {
                var nameProperty = exposedObject.FindProperty("name");
                if (nameProperty != null)
                {
                    var currentName = nameProperty.Value.GetValue() as string;
                    if (string.IsNullOrEmpty(currentName))
                    {
                        nameProperty.Value.SetValue(jsonName);
                    }
                }
            }

            // @parent を Unity hierarchy へ復元する (Registry.SetParent が Transform.parent を書き換え)
            if (jObject.TryGetValue("@parent", out var parentToken)
                && exposedObject.target is ExposedUnityObjectBase proxyBase)
            {
                var parentValue = parentToken.Type == JTokenType.Null ? null : parentToken.Value<string>();
                ExposedObjectRegistry.SetParent(proxyBase.id, parentValue, out _);
            }

            // 各プロパティをデシリアライズして設定
            var properties = exposedObject.propertyTypes;
            bool hasUpdates = false;

            foreach (var propType in properties)
            {
                if (!propType.isValid) continue;

                // JSONからプロパティ値を取得 (旧名 [FormerlyExposedAs] でも引けるようにする)
                var token = _GetTokenByPropertyName(jObject, propType);
                if (token == null || token.Type == JTokenType.Null) continue;

                // プロパティに値を設定（再帰的に子プロパティも処理）
                var property = new ExposedProperty(propType, exposedObject, exposedObject.target);
                if (_FromJsonProperty(resolver, token, property, captureDefaults))
                {
                    hasUpdates = true;
                }
            }

            return hasUpdates;
        }

        internal static bool FromJson(string json, ExposedObject exposedObject)
        {
            return FromJson(json, exposedObject, DefaultExposedObjectResolver.Instance);
        }

        // -------------------------------------------------------
        // Array element operations
        // -------------------------------------------------------

        internal static bool AddArrayElement(string json, in ExposedProperty property)
        {
            if (string.IsNullOrEmpty(json)) return false;
            if (!property.isArray) return false;

            var jObject = JsonConvert.DeserializeObject<JObject>(json);
            var token = jObject["value"];
            if (token == null || token.Type == JTokenType.Null) return false;

            // 配列の要素型を取得
            var elementType = ExposedPropertyUtility.GetCollectionElementType(property.type.valueType);

            // デフォルト値で要素を追加（比較ベースのdirty判定のため、JSONの値ではなくデフォルト値で追加）
            var defaultElement = ExposedPropertyUtility.CreateDefaultElement(elementType);
            if (!property.Add(defaultElement))
            {
                return false;
            }

            // 追加された要素のプロパティを取得し、JSONの値を設定（デフォルト値との比較でdirtyが判定される）
            var newIndex = property.arrayLength - 1;
            var elementProp = property.GetPropertyIndex(newIndex);
            if (elementProp != null)
            {
                _FromJsonProperty(DefaultExposedObjectResolver.Instance, token, elementProp.Value);
            }

            return true;
        }

        internal static bool RemoveArrayElement(string json, in ExposedProperty property)
        {
            if (string.IsNullOrEmpty(json)) return false;
            if (!property.isArray) return false;

            var jObject = JsonConvert.DeserializeObject<JObject>(json);
            var indexToken = jObject["index"];
            if (indexToken == null) return false;

            int index = indexToken.Value<int>();

            // -1の場合は最後の要素を削除
            if (index == -1)
            {
                index = property.arrayLength - 1;
            }

            if (index < 0 || index >= property.arrayLength) return false;

            return property.RemoveAt(index);
        }

        internal static bool ReorderArrayElement(string json, in ExposedProperty property)
        {
            if (string.IsNullOrEmpty(json)) return false;
            if (!property.isArray) return false;

            var jObject = JsonConvert.DeserializeObject<JObject>(json);
            var fromIndexToken = jObject["fromIndex"];
            var toIndexToken = jObject["toIndex"];
            if (fromIndexToken == null || toIndexToken == null) return false;

            int fromIndex = fromIndexToken.Value<int>();
            int toIndex = toIndexToken.Value<int>();

            if (fromIndex < 0 || fromIndex >= property.arrayLength) return false;
            if (toIndex < 0 || toIndex >= property.arrayLength) return false;
            if (fromIndex == toIndex) return true; // 移動不要

            return property.Reorder(fromIndex, toIndex);
        }
    }
}
