// Copyright (c) You-Ri, 2026
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// LivePropertyのシリアライズ/デシリアライズを担当するユーティリティクラス。
    /// JTokenレベルの変換、JSON文字列API (ToJson/FromJson)、配列操作を提供する。
    /// </summary>
    public static class LivePropertySerializer
    {
        /// <summary>
        /// Object-level serialization options threaded through the full / delta / persistence path.
        /// Replaces the (forPersistence, skipPropertyRef, scopeFilter) flag triple that used to be
        /// passed around individually.
        /// <list type="bullet">
        /// <item><c>forPersistence</c>: writing to a file (scene / project settings) rather than a
        /// REST / dirty-detection read.</item>
        /// <item><c>skipPropertyRef</c>: exclude <see cref="LivePropertyType.isLivePropertyReference"/>
        /// fields (baseline / dirty comparison does not want the referenced value inlined).</item>
        /// <item><c>scopeFilter</c>: when persisting, only members whose persistScope matches are written.</item>
        /// <item><c>maxDepth</c>: how many levels of composite (LiveClass) object expansion the top
        /// object is serialized with. <c>int.MaxValue</c> = unbounded (the default, and the only value
        /// used by persistence / dirty detection). REST reads may pass a finite depth so nested
        /// inline children beyond the limit are emitted as truncation stubs. Arrays do not consume depth
        /// (their elements sit at the array's depth) so element count and type stay visible. Ignored
        /// (forced unbounded) when <c>forPersistence</c> is true — see <see cref="SerializeFullToJObject"/>.</item>
        /// </list>
        /// </summary>
        internal readonly struct SerializeOptions
        {
            public readonly bool forPersistence;
            public readonly bool skipPropertyRef;
            public readonly PersistScope scopeFilter;
            public readonly int maxDepth;

            public SerializeOptions(bool forPersistence, bool skipPropertyRef, PersistScope scopeFilter, int maxDepth = int.MaxValue)
            {
                this.forPersistence = forPersistence;
                this.skipPropertyRef = skipPropertyRef;
                this.scopeFilter = scopeFilter;
                this.maxDepth = maxDepth;
            }
        }

        // JToken → JSON 文字列化用のスクラッチバッファ。スレッドごとに使い回して、毎回の
        // StringBuilder(256) 新規確保とその成長時の再確保を避ける (RemoteControl ハンドラは
        // ワーカースレッドで動くため ThreadStatic で衝突しない)。使用直前に必ず Clear するので
        // Domain Reload の状態を持ち越さない。
        [ThreadStatic] private static StringBuilder _jsonBuffer;

        /// <summary>
        /// JToken を <c>Formatting.None</c> の JSON 文字列にシリアライズする。
        /// <c>JsonConvert.SerializeObject(token, Formatting.None)</c> と出力バイトは同一だが、
        /// 内部バッファ (StringBuilder) をスレッドローカルに使い回すことで、シリアライズごとの
        /// バッファ確保・成長アロケーションを排除する (バッチ経路のように多数のオペレーションを
        /// 連続でシリアライズする用途で GC を削減する)。最終的な文字列 1 本のみ確保する。
        /// </summary>
        /// <summary>
        /// True for a member whose value IS a picture: [ImagePreview] on a <see cref="LiveImageData"/>
        /// member. Such members are folded to their own address on every JSON read (the getter renders
        /// a frame, so JSON paths must never invoke it); the bytes are served by the property GET route.
        /// [ImagePreview] on a string member is the older style — the getter returns an address and is
        /// safe to call — and keeps its plain serialization.
        /// </summary>
        internal static bool IsImageProperty(LivePropertyType propType)
            => propType != null
                && propType.controlAttribute is ImagePreviewAttribute
                && propType.valueType == typeof(LiveImageData);

        /// <summary>The address a folded image member points at: its own property GET route.</summary>
        internal static string ImageAddress(string id, string slashPath)
            => "/live/object/" + id + "/" + slashPath;

        internal static string SerializeToJson(JToken token)
        {
            var sb = _jsonBuffer ??= new StringBuilder(4096);
            sb.Clear();

            using (var writer = new StringWriter(sb, CultureInfo.InvariantCulture))
            using (var jsonWriter = new JsonTextWriter(writer) { Formatting = Formatting.None })
            {
                token.WriteTo(jsonWriter);
            }

            return sb.ToString();
        }

        /// <summary>
        /// JObject から propType に対応する値 JToken を取得する。現在の name で見つからない場合は
        /// [FormerlyNamedAs] で宣言された旧名も順に試す。リネーム後の旧シーンファイル読み込みに使用。
        /// </summary>
        private static JToken _GetTokenByPropertyName(JObject jObject, LivePropertyType propType)
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
        private static bool _ShouldSkipProperty(LivePropertyType propType, bool forPersistence, PersistScope scopeFilter = PersistScope.Scene)
        {
            if (!propType.isValid) return true;
            if (forPersistence && !propType.isPersistable) return true;
            // 永続化時のみ保存先 (scope) でフィルタする。REST / dirty 判定など非永続の読み出しは
            // scope に関わらず全プロパティを対象にする。
            if (forPersistence && propType.persistScope != scopeFilter) return true;
            if (forPersistence && propType.isReadOnly && !propType.containsLiveObjectReference) return true;
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
        /// LiveObjectのメタデータ(@type, @id, @name)を含むJObjectを生成する。
        /// </summary>
        private static JObject _CreateMetadataJObject(LiveObjectHandle liveObject, bool forPersistence = false)
        {
            var jObject = new JObject();
            jObject["@type"] = liveObject.targetTypeName;
            if (liveObject.hasId)
            {
                jObject["@id"] = liveObject.id;
            }
            // UnityEngine.Object ターゲットは instanceID を別メタフィールドとして付与する。
            // RemoteApp 側の副次インデックスでルーティング翻訳に使う。
            // 永続化 (scene/studio.json など) には含めない — instanceID はセッション依存。
            if (!forPersistence
                && liveObject.target is UnityEngine.Object unityObj
                && unityObj != null)
            {
                jObject["@instanceID"] = LiveObjectUtility.GetInstanceID(unityObj).ToString();
            }
            // @name は RemoteApp 側の表示用。永続化には不要。
            if (!forPersistence)
            {
                jObject["@name"] = liveObject.name;
            }

            // インスタンス単位のアイコン上書き。未設定(null/empty)ならクラス側のアイコンにフォールバックされる。
            // 永続化時は不要 — アイコンはランタイムで再解決される。
            if (liveObject.target is LiveUnityObjectBase proxyBase)
            {
                if (!forPersistence)
                {
                    var iconOverride = proxyBase.GetIconOverride();
                    if (!string.IsNullOrEmpty(iconOverride))
                    {
                        jObject["@icon"] = iconOverride;
                    }
                }

                // 親LiveObject id。ルートなら出力しない。永続化時も含める。
                if (!string.IsNullOrEmpty(proxyBase.parentId))
                {
                    jObject["@parent"] = proxyBase.parentId;
                }
            }

            return jObject;
        }

        // -------------------------------------------------------
        // [RawJson] support
        // -------------------------------------------------------
        // A string member tagged [RawJson] stores a JSON document. On write its value is embedded as
        // parsed JSON (object/array) instead of an escaped string; on read the embedded JSON is
        // re-stringified. Legacy files that stored the value as a JSON string still read (verbatim).

        // Returns the embedded-JSON token for a [RawJson] string value, or null when the member is not
        // RawJson or the value is not parseable JSON (caller falls back to normal serialization).
        internal static JToken TrySerializeRawJson(LivePropertyType propType, object value)
        {
            if (propType == null || !propType.isRawJson) return null;
            if (!(value is string s) || string.IsNullOrEmpty(s)) return null;
            try { return JToken.Parse(s); }
            catch { return null; } // not valid JSON -> fall back to plain string serialization
        }

        // Converts a [RawJson] member's stored token back to the string its field holds. Returns true and
        // sets `result` when the member is RawJson and the token is usable (object/array -> compact JSON
        // string; string -> verbatim legacy value). Returns false when not RawJson or token is unusable
        // (caller falls back to normal deserialization).
        internal static bool TryDeserializeRawJson(LivePropertyType propType, JToken token, out string result)
        {
            result = null;
            if (propType == null || !propType.isRawJson || token == null) return false;
            switch (token.Type)
            {
                case JTokenType.String:
                    result = token.Value<string>(); // legacy: stored as an escaped JSON string
                    return true;
                case JTokenType.Object:
                case JTokenType.Array:
                    result = token.ToString(Formatting.None);
                    return true;
                default:
                    return false; // null / unexpected -> let the caller handle it
            }
        }

        // -------------------------------------------------------
        // JToken Serialization
        // -------------------------------------------------------

        // Unity型をJTokenにシリアライズする共通メソッド
        // depth: remaining composite-expansion budget (int.MaxValue = unbounded). Only the REST read path
        // passes a finite value; every other caller keeps the default so behavior is byte-identical.
        internal static JToken SerializeUnityType(ILiveObjectResolver resolver, object value, bool forceValue = false, bool forPersistence = false, int depth = int.MaxValue)
        {
            Debug.Assert(resolver != null, "Resolver cannot be null");

            // Unity型の場合は Unity の == 演算子で null チェック（破壊されたオブジェクトを検出）
            if (value is UnityEngine.Object unityObj && unityObj == null)
                return JValue.CreateNull();

            if (value == null) return JValue.CreateNull();

            // LiveImageData reaching the generic path means an image member sits somewhere the fold
            // does not cover (a nested composite, where no address exists). Degrade to null instead of
            // letting Newtonsoft base64 the bytes into JSON.
            if (value is LiveImageData) return JValue.CreateNull();

            // LiveObjectインスタンスが直接渡された場合は参照としてシリアライズ
            // （静的クラスのLiveObjectはtargetがnullのため、FindByTargetで解決できない）
            if (value is LiveObjectHandle liveObj && liveObj.hasId)
            {
                var refObj = new JObject
                {
                    ["@type"] = liveObj.targetType.typeName,
                    ["@ref"] = liveObj.id,
                };
                if (!forPersistence) refObj["@name"] = liveObj.name;
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
            if (valueType == typeof(Color)) return UnityScalarSerializer.SerializeColor((Color)value);
            if (valueType == typeof(Vector3)) return UnityScalarSerializer.SerializeVector3((Vector3)value);
            if (valueType == typeof(Vector2)) return UnityScalarSerializer.SerializeVector2((Vector2)value);
            if (valueType == typeof(Quaternion)) return UnityScalarSerializer.SerializeQuaternion((Quaternion)value);
            if (valueType == typeof(Rect)) return UnityScalarSerializer.SerializeRect((Rect)value);
            if (valueType == typeof(TransformValue)) return UnityScalarSerializer.SerializeTransformValue((TransformValue)value);
            if (valueType == typeof(Texture2D)) return UnityScalarSerializer.SerializeTexture2D((Texture2D)value);

            // プリミティブ型とEnum: JToken.FromObject の JsonSerializer 経由を避け、型付き JValue を
            // 直接生成する (出力 JSON は同一。値ごとの一時アロケーションを削減)。
            switch (value)
            {
                case string s: return new JValue(s);
                case int i: return new JValue(i);
                case float f: return new JValue(f);
                case bool b: return new JValue(b);
                case double d: return new JValue(d);
            }

            if (value is System.Enum)
                return new JValue(value.ToString());

            if (value is System.Collections.IEnumerable collection && !(value is string))
                return _SerializeArray(resolver, collection, forPersistence, depth);

            return SerializeLiveObject(resolver, value, forceValue, forPersistence, depth);
        }

        // コレクションをJArrayにシリアライズ
        // Arrays do not consume depth budget: elements are serialized at the same depth as the array so
        // element count and per-element type stay visible even at a shallow depth.
        private static JToken _SerializeArray(ILiveObjectResolver resolver, System.Collections.IEnumerable collection, bool forPersistence = false, int depth = int.MaxValue)
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
                    var serializedItem = SerializeUnityType(resolver, item, forPersistence: forPersistence, depth: depth);
                    jArray.Add(serializedItem ?? JValue.CreateNull());
                }
                fileResolver?.PopPath();
                index++;
            }
            return jArray;
        }

        // depth: remaining composite-expansion budget (int.MaxValue = unbounded). When it reaches 0 an
        // inline (unregistered) child is emitted as a truncation stub instead of expanding its properties.
        public static JToken SerializeLiveObject(ILiveObjectResolver resolver, object value, bool forceValue, bool forPersistence = false, int depth = int.MaxValue)
        {
            Debug.Assert(resolver != null, "Resolver cannot be null");
            Debug.Assert(value != null, "Value cannot be null");

            var valueType = value.GetType();
            var liveClass = LiveClass.Find(valueType);

            // LiveClassが存在する場合は、定義されたプロパティのみをシリアライズ
            if (liveClass != null)
            {
                var jObject = new JObject();
                jObject["@type"] = liveClass.typeName;
                var valueLiveObject = resolver.FindByTarget(value);

                // ID付きLiveObject（コンテナ管理）の場合は参照情報のみをシリアライズ
                // IDなしまたは未登録の場合はプロパティをインライン展開
                if (valueLiveObject != null && valueLiveObject.Value.hasId && forceValue == false)
                {
                    jObject["@ref"] = valueLiveObject.Value.id;
                    if (!forPersistence)
                    {
                        jObject["@name"] = valueLiveObject.Value.name;
                        if (value is UnityEngine.Object refUnityObj && refUnityObj != null)
                        {
                            jObject["@instanceID"] = LiveObjectUtility.GetInstanceID(refUnityObj).ToString();
                        }
                    }
                    return jObject;
                }

                // Depth limit reached: emit a truncation stub for this inline (unregistered) child
                // instead of expanding its properties. @truncated marks that a child exists here so the
                // client can lazily fetch it via GET /live/object/{id}/{path}; @type/@name/@instanceID
                // keep the collapsed view labeled and addressable by the client. Registered children are handled by the
                // @ref branch above and are unaffected. forPersistence never reaches here with a finite
                // depth (SerializeFullToJObject forces unbounded), so scene/project files are byte-stable.
                if (depth <= 0)
                {
                    jObject["@truncated"] = true;
                    if (!forPersistence)
                    {
                        if (valueLiveObject != null)
                        {
                            jObject["@name"] = valueLiveObject.Value.name;
                        }
                        if (value is UnityEngine.Object truncatedUnityObj && truncatedUnityObj != null)
                        {
                            jObject["@instanceID"] = LiveObjectUtility.GetInstanceID(truncatedUnityObj).ToString();
                        }
                    }
                    return jObject;
                }

                // インライン展開時も UnityEngine.Object であれば instanceID を付与する。
                // RemoteApp 側は @id を持たないインライン要素をこれで名指しする (副次インデックス)。
                // 永続化にはセッション依存の instanceID を含めない。
                if (!forPersistence
                    && value is UnityEngine.Object inlineUnityObj
                    && inlineUnityObj != null)
                {
                    jObject["@instanceID"] = LiveObjectUtility.GetInstanceID(inlineUnityObj).ToString();
                }

                if (!forPersistence && valueLiveObject != null)
                {
                    jObject["@name"] = valueLiveObject.Value.name;
                }
                var fileResolver = resolver as IFileScopedResolver;
                foreach (var propType in liveClass.propertyTypes)
                {
                    if (_ShouldSkipProperty(propType, forPersistence)) continue;

                    // LivePropertyRef は参照先の値に展開。baseline には含めない
                    if (propType.isLivePropertyReference)
                    {
                        if (forPersistence) continue;
                        var refRaw = LivePropertyUtility.GetValueRaw(value, propType);
                        if (refRaw is LivePropertyRef pr)
                        {
                            var resolved = pr.Resolve();
                            var resolvedValue = resolved?.GetValue();
                            fileResolver?.PushPath(propType.name);
                            var serializedRef = SerializeUnityType(resolver, resolvedValue, propType.forceValue, forPersistence, depth - 1);
                            fileResolver?.PopPath();
                            if (serializedRef != null)
                            {
                                jObject[propType.name] = serializedRef;
                            }
                        }
                        continue;
                    }

                    var propValue = LivePropertyUtility.GetValueRaw(value, propType);

                    // ObjectSelector / AssetSelector are dispatched by control attribute the same way the
                    // top-level SerializeFullToJObject does. Without this branch here, such a property on a
                    // NESTED (inline-expanded) exposed object falls through to the generic UnityEngine.Object
                    // path and JsonUtility.ToJson throws on engine types (e.g. an [AssetSelector] AnimationClip
                    // on a component captured via the GameObject wrapper). Keeps both paths in parity.
                    if (propType.controlAttribute is ObjectSelectorAttribute)
                    {
                        jObject[propType.name] = ObjectSelectorSerializer.SerializeObjectSelectorValue(propValue, forPersistence);
                        continue;
                    }
                    if (propType.controlAttribute is AssetSelectorAttribute)
                    {
                        jObject[propType.name] = AssetSelectorSerializer.SerializeAssetSelectorValue(propValue, forPersistence);
                        continue;
                    }

                    fileResolver?.PushPath(propType.name);
                    var serializedValue = TrySerializeRawJson(propType, propValue)
                        ?? SerializeUnityType(resolver, propValue, propType.forceValue, forPersistence, depth - 1);
                    fileResolver?.PopPath();

                    if (serializedValue != null)
                    {
                        jObject[propType.name] = serializedValue;
                    }
                }
                return jObject;
            }

            // LiveClass 未登録のプレーンな [Serializable] POCO は
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

            Debug.LogError($"[RemoteControl] LiveClass not found for type {valueType.Name} and JsonUtility serialization failed.");
            return JValue.CreateNull();
        }

        // -------------------------------------------------------
        // JToken Deserialization
        // -------------------------------------------------------

        /// <summary>
        /// 関数の引数配列を JArray から構築する。パラメータ個数ベースで確保し、未指定/null の位置は
        /// HasDefaultValue があれば既定値、無ければ型の default を使う。REST invoke の "args" 配列と
        /// 保存済みオペレーション引数 (InvokeFunctionOperation.argsJson) の両方で共有する。
        /// resolver 省略時は <see cref="DefaultLiveObjectResolver.Instance"/> を使う。
        /// </summary>
        public static object[] BuildInvokeArguments(LiveFunctionType function, JArray jArray, ILiveObjectResolver resolver = null)
        {
            if (function == null || jArray == null) return null;
            if (resolver == null) resolver = DefaultLiveObjectResolver.Instance;

            var parameters = function.parameters;
            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                var jToken = i < jArray.Count ? jArray[i] : null;
                if (jToken == null || jToken.Type == JTokenType.Null)
                {
                    args[i] = param.HasDefaultValue
                        ? param.DefaultValue
                        : (param.ParameterType.IsValueType ? Activator.CreateInstance(param.ParameterType) : null);
                }
                else
                {
                    args[i] = DeserializeUnityType(resolver, jToken, param.ParameterType);
                }
            }
            return args;
        }

        /// <summary>
        /// JSON 配列文字列から関数の引数配列を構築する。空文字/null は null (= 引数なし)。オペレーションに
        /// 保存した位置引数 (argsJson) を実行時に型付き引数へ戻すために使う。
        /// </summary>
        public static object[] BuildInvokeArgumentsFromJson(LiveFunctionType function, string argsJson, ILiveObjectResolver resolver = null)
        {
            if (function == null || string.IsNullOrEmpty(argsJson)) return null;
            var jArray = JsonConvert.DeserializeObject<JArray>(argsJson);
            return BuildInvokeArguments(function, jArray, resolver);
        }

        // JTokenからUnity型にデシリアライズする共通メソッド
        // 既存のインスタンスに上書き、インスタンスがnullの場合は新規作成
        public static object DeserializeUnityType(ILiveObjectResolver resolver, JToken token, System.Type type, object instance = null)
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
                if (LivePropertyUtility.IsArrayType(type))
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
                    return DeserializeLiveObject(resolver, token, type, instance);
                }
            }
        }

        // Array/List共通のデシリアライズ
        private static object _DeserializeCollection(ILiveObjectResolver resolver, JToken token, System.Type collectionType, object instance = null)
        {
            Debug.Assert(resolver != null, "Resolver cannot be null");

            if (token.Type != JTokenType.Array) return instance;

            var jArray = (JArray)token;
            var elementType = LivePropertyUtility.GetCollectionElementType(collectionType);
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
                    var existing = existingArray != null && i < existingArray.Length ? existingArray.GetValue(i) : LivePropertyUtility.CreateDefaultElement(elementType);
                    var element = DeserializeLiveObject(resolver, jArray[i], elementType, existing);
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
                    var existing = existingList != null && i < existingList.Count ? existingList[i] : LivePropertyUtility.CreateDefaultElement(elementType);
                    var element = DeserializeLiveObject(resolver, jArray[i], elementType, existing);
                    list.Add(element);
                }
                return list;
            }
        }

        // Array/List共通のデルタ形式デシリアライズ
        private static object _DeserializeCollectionDelta(ILiveObjectResolver resolver, JArray jArray, System.Type elementType, bool isArray, object existingCollection)
        {
            // @op: "new" 要素のカウント
            int newCount = 0;
            foreach (var element in jArray)
            {
                if (_IsNewArrayElement(element))
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
                    array.SetValue(LivePropertyUtility.CreateDefaultElement(elementType), i);
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
                if (_IsNewArrayElement(element))
                {
                    if (isArray)
                    {
                        if (addedIndex < totalLength)
                        {
                            var newElement = DeserializeLiveObject(resolver, element, elementType, ((System.Array)result).GetValue(addedIndex));
                            setElement(addedIndex, newElement);
                            addedIndex++;
                        }
                    }
                    else
                    {
                        var newElement = LivePropertyUtility.CreateDefaultElement(elementType);
                        newElement = DeserializeLiveObject(resolver, element, elementType, newElement);
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
                            var merged = DeserializeLiveObject(resolver, element, elementType, existing);
                            setElement(existingIndex, merged);
                        }
                    }
                    existingIndex++;
                }
            }

            return result;
        }

        /// <summary>
        /// LiveClassとインスタンスを解決する。
        /// instanceがnullの場合はtypeからLiveClassを検索しデフォルトインスタンスを生成。
        /// instanceがある場合はその実行時型からLiveClassを検索。
        /// </summary>
        private static LiveClass _ResolveLiveClassAndInstance(Type type, ref object instance)
        {
            if (instance == null)
            {
                instance = LivePropertyUtility.CreateDefaultElement(type);
                return LiveClass.Find(type);
            }
            return LiveClass.Find(instance.GetType());
        }

        // カスタムオブジェクトのデシリアライズ
        public static object DeserializeLiveObject(ILiveObjectResolver resolver, JToken token, System.Type type, object instance = null)
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
                    // LiveObjectFileRegistry から UnityEngine.Object を復元
                    if (refObj["@type"] == null)
                    {
                        if (LiveObjectFileRegistry.TryGetObject(referenceId, out var fileObj))
                        {
                            return fileObj;
                        }
                        Debug.LogWarning($"[RemoteControl] File-scope @ref not resolved: id={referenceId}");
                        return instance;
                    }

                    // @refがある場合は@typeも必ず存在する
                    var typeName = refObj["@type"]?.Value<string>();
                    Debug.Assert(!string.IsNullOrEmpty(typeName), "@ref requires @type field");

                    var refLiveClass = LiveClass.Find(typeName);
                    if (refLiveClass == null)
                    {
                        Debug.LogWarning($"LiveClass not found for type: {typeName}");
                        return null;
                    }

                    // resolver を使って参照を解決する
                    var referencedObject = resolver.FindById(referenceId);
                    if (referencedObject != null)
                    {
                        return referencedObject.Value.target;
                    }

                    // LiveObjectがまだ生成されていない場合
                    if (referencedObject == null && instance != null)
                    {
                        LiveObjectRegistry.Create(refLiveClass.type, instance, referenceId);
                        return instance;
                    }

                    Debug.LogWarning($"[RemoteControl] Failed to resolve. reference: {referenceId} type: {typeName}");
                    return instance;
                }
            }

            LiveClass liveClass;

            // @type フィールドで型切り替え対応
            if (token is JObject typeCheckObj)
            {
                var requestedTypeName = typeCheckObj["@type"]?.Value<string>();
                if (!string.IsNullOrEmpty(requestedTypeName))
                {
                    var requestedLiveClass = LiveClass.Find(requestedTypeName);
                    if (requestedLiveClass != null)
                    {
                        if (instance == null || instance.GetType() != requestedLiveClass.type)
                            instance = LivePropertyUtility.CreateDefaultElement(requestedLiveClass.type);
                        liveClass = requestedLiveClass;
                    }
                    else
                    {
                        // フォールバック: LiveClass未登録の型名
                        liveClass = _ResolveLiveClassAndInstance(type, ref instance);
                    }
                }
                else
                {
                    // @typeなし: 既存の動作
                    liveClass = _ResolveLiveClassAndInstance(type, ref instance);
                }
            }
            else
            {
                // JObjectでない場合: 既存の動作
                liveClass = _ResolveLiveClassAndInstance(type, ref instance);
            }

            // LiveClassが存在する場合は、定義されたプロパティのみをデシリアライズ
            if (liveClass != null && token is JObject jObject)
            {
                // instanceがnullの場合（MonoBehaviour等、Activatorで生成不可の型）はスキップ
                if (instance == null) return null;

                // 定義されたプロパティのみをデシリアライズして設定
                foreach (var propType in liveClass.propertyTypes)
                {
                    if (!propType.isValid) continue;
                    if (propType.isReadOnly && propType.shadowField == null) continue;

                    var propToken = _GetTokenByPropertyName(jObject, propType);
                    if (propToken == null || propToken.Type == JTokenType.Null) continue;

                    // Shadow Field がある場合は backing field から既存値を読み、書き込みも shadow field に直接行う
                    // (Property setter の副作用 Apply はデシリアライズ後の ILiveDeserializeCallback に委譲)。
                    var existingValue = propType.shadowField != null
                        ? propType.shadowField.GetValue(instance)
                        : LivePropertyUtility.GetValueRaw(instance, propType);
                    var propValue = TryDeserializeRawJson(propType, propToken, out var rawJsonValue)
                        ? rawJsonValue
                        : DeserializeUnityType(resolver, propToken, propType.valueType, existingValue);
                    if (propValue != null)
                    {
                        if (propType.shadowField != null)
                        {
                            propType.shadowField.SetValue(instance, propValue);
                        }
                        else
                        {
                            LivePropertyUtility.SetValueRaw(instance, propType, propValue);
                        }
                    }
                }

                // SetValueRaw bypasses property setters, so types that need to apply
                // deserialized field values to external state (Unity components,
                // engine state, etc.) opt in via ILiveDeserializeCallback.
                (instance as ILiveDeserializeCallback)?.OnAfterLiveDeserialize();

                return instance;
            }

            // LiveClass 未登録のプレーンな [Serializable] POCO は
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
        /// デルタ配列の要素が「新規追加」マーカー (@op:"new") かどうかを判定する。
        /// </summary>
        private static bool _IsNewArrayElement(JToken element)
            => element is JObject obj && obj["@op"]?.ToString() == "new";

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
        /// 結果ベースのdirty判定: @プレフィックス以外のプロパティが存在するか。
        /// public: 他アセンブリ (LiveStudio の AssetStateSnapshot 等) と同一述語を共有するため。
        /// </summary>
        public static bool HasNonMetaProperties(JObject jObj)
        {
            foreach (var p in jObj.Properties())
            {
                if (!p.Name.StartsWith("@")) return true;
            }
            return false;
        }

        // -------------------------------------------------------
        // JSON Diff (Delta serialization)
        // -------------------------------------------------------

        /// <summary>
        /// LiveObjectをフルシリアライズしてJObjectを返す（dirty判定なし）。
        /// LiveObjectDefaultRegistryおよびdelta serialization用にinternalで公開。
        /// </summary>
        internal static JObject SerializeFullToJObject(LiveObjectHandle liveObject, ILiveObjectResolver resolver, bool forPersistence = false, PersistScope scopeFilter = PersistScope.Scene)
        {
            return SerializeFullToJObject(liveObject, resolver,
                new SerializeOptions(forPersistence, skipPropertyRef: forPersistence, scopeFilter));
        }

        /// <summary>
        /// <see cref="SerializeFullToJObject"/> の拡張版。
        /// <paramref name="skipPropertyRef"/> が true の場合、<see cref="LivePropertyType.isLivePropertyReference"/>
        /// のフィールドを出力から除外する。baseline/dirty比較用など、参照先の値を含めたくない用途に使う。
        /// </summary>
        internal static JObject SerializeFullToJObject(LiveObjectHandle liveObject, ILiveObjectResolver resolver, bool forPersistence, bool skipPropertyRef, PersistScope scopeFilter = PersistScope.Scene)
        {
            return SerializeFullToJObject(liveObject, resolver,
                new SerializeOptions(forPersistence, skipPropertyRef, scopeFilter));
        }

        internal static JObject SerializeFullToJObject(LiveObjectHandle liveObject, ILiveObjectResolver resolver, in SerializeOptions options)
        {
            if (liveObject == null) return new JObject();

            // Owner-side hook: refresh shadow fields whose canonical value is
            // derived from external state (e.g. AvatarInput.settings snapshot).
            // Fires once per object before persistence so the shadow field path
            // used below sees a fresh value. Skipped for non-persistence reads
            // (dirty detection, API responses) because property
            // setters keep the shadow in sync with user-driven changes and the
            // refresh itself can be expensive (CreateSettingsFromAvatarInput).
            if (options.forPersistence)
            {
                (liveObject.target as ILiveSerializeCallback)?.OnBeforeLiveSerialize();
                if (liveObject.target == null)
                {
                    liveObject.targetType?.InvokeStaticBeforeSerialize();
                }
            }

            var properties = liveObject.propertyTypes;
            var jObject = _CreateMetadataJObject(liveObject, options.forPersistence);

            // persistentオブジェクトのマーク（永続化JSONには含めない）
            if (!options.forPersistence && resolver is LiveObjectContainer container
                && liveObject.hasId && container.IsPersistent(liveObject.id))
            {
                jObject["@persistent"] = true;
            }

            var fileResolver = resolver as IFileScopedResolver;

            // The requested (top) object is always fully serialized; its property VALUES are serialized
            // one level down. maxDepth == 1 therefore yields childDepth 0 so nested inline composites are
            // stubbed. Persistence forces unbounded depth so scene / project files stay byte-identical.
            int childDepth = options.forPersistence || options.maxDepth == int.MaxValue
                ? int.MaxValue
                : options.maxDepth - 1;

            foreach (var propertyType in properties)
            {
                if (_ShouldSkipProperty(propertyType, options.forPersistence, options.scopeFilter)) continue;

                // LivePropertyRef: 値は参照先の実プロパティから取る。
                // baseline / dirty比較 では含めない — 参照先が baseline を持つため。
                if (propertyType.isLivePropertyReference)
                {
                    if (options.skipPropertyRef) continue;
                    var refRaw = LivePropertyUtility.GetValueRaw(liveObject.target, propertyType);
                    if (refRaw is LivePropertyRef pr)
                    {
                        var resolved = pr.Resolve();
                        var resolvedValue = resolved?.GetValue();
                        fileResolver?.PushPath(propertyType.name);
                        var serializedRef = SerializeUnityType(resolver, resolvedValue, propertyType.forceValue, options.forPersistence, childDepth);
                        fileResolver?.PopPath();
                        jObject[propertyType.name] = serializedRef ?? JValue.CreateNull();
                    }
                    else
                    {
                        jObject[propertyType.name] = JValue.CreateNull();
                    }
                    continue;
                }

                // Image member: fold to its own address WITHOUT invoking the getter — the getter
                // renders a frame, and listing an object must not render its pictures. The bytes are
                // served when that address is GET directly. Derived state, so never persisted.
                if (IsImageProperty(propertyType))
                {
                    if (options.forPersistence) continue;
                    jObject[propertyType.name] = liveObject.hasId
                        ? (JToken)ImageAddress(liveObject.id, propertyType.name)
                        : JValue.CreateNull();
                    continue;
                }

                // Shadow Field がある場合は backing field から直接読む (Property getter をバイパス)。
                // Property getter が外部状態 (Screen.width 等) を返す Shadow パターンでも、保存対象は
                // ユーザーが設定した「内部の値」なので shadow field を信頼する。
                var value = propertyType.shadowField != null
                    ? propertyType.shadowField.GetValue(liveObject.target)
                    : LivePropertyUtility.GetValueRaw(liveObject.target, propertyType);

                // ObjectSelector: GameObject 側の LiveObjectHandle を @ref として出力する
                if (propertyType.controlAttribute is ObjectSelectorAttribute)
                {
                    jObject[propertyType.name] = ObjectSelectorSerializer.SerializeObjectSelectorValue(value, options.forPersistence);
                    continue;
                }

                // AssetSelector: アセット参照を @guid として出力する
                if (propertyType.controlAttribute is AssetSelectorAttribute)
                {
                    jObject[propertyType.name] = AssetSelectorSerializer.SerializeAssetSelectorValue(value, options.forPersistence);
                    continue;
                }

                fileResolver?.PushPath(propertyType.name);
                var serializedValue = TrySerializeRawJson(propertyType, value)
                    ?? SerializeUnityType(resolver, value, propertyType.forceValue, options.forPersistence, childDepth);
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

            // 要素がJObjectかどうかで分岐（LiveClass配列 vs プリミティブ配列）
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

            // LiveClass配列: デルタ形式
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
        /// なければ@typeからLiveClassのデフォルトインスタンスをシリアライズして返す。
        /// </summary>
        private static JToken _GetDefaultTemplate(JArray defaultArr, JToken currentElement)
        {
            // 新規要素（baseline 長を超える要素）は読み込み側で型デフォルト
            // (CreateDefaultElement, _FromJsonPropertyArrayDelta 参照) から再生成され、その上に
            // JSON デルタが適用される。したがって書き込み側の差分基準も同じ型デフォルトでなければ
            // ならない。defaultArr[0]（既存要素）を基準にすると、既存要素と同値だが型デフォルトと
            // 異なるフィールドが省略され、再読み込み時に失われる（例: StageManager で enabled=true が
            // 常時 ON の先頭ブートストラップ要素と一致して省略されるケース）。
            if (currentElement is JObject currentObj)
            {
                var typeName = currentObj["@type"]?.Value<string>();
                if (!string.IsNullOrEmpty(typeName))
                {
                    var liveClass = LiveClass.Find(typeName);
                    if (liveClass != null)
                    {
                        var defaultInstance = LivePropertyUtility.CreateDefaultElement(liveClass.type);
                        if (defaultInstance != null)
                        {
                            return SerializeUnityType(DefaultLiveObjectResolver.Instance, defaultInstance);
                        }
                    }
                }
            }

            // @type が取得できない場合のフォールバック: 既存 baseline 要素をベストエフォートで使用。
            if (defaultArr.Count > 0) return defaultArr[0];

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

        internal static string ToJson(LiveObjectHandle liveObject, ILiveObjectResolver resolver, bool isDirtyOnly = false, bool forPersistence = false, PersistScope scopeFilter = PersistScope.Scene, int maxDepth = int.MaxValue)
        {
            if (liveObject == null) return "{}";

            var options = new SerializeOptions(forPersistence, skipPropertyRef: forPersistence, scopeFilter, maxDepth);

            // Delta mode: フルシリアライズ + JsonDiff方式
            if (isDirtyOnly)
            {
                return _ToJsonDelta(liveObject, resolver, options);
            }

            // Snapshot mode: 従来のフルシリアライズ
            return _ToJsonFull(liveObject, resolver, options);
        }

        /// <summary>
        /// Delta mode: デフォルトJSONとcurrentJSONを比較し、差分のみを出力する。
        /// </summary>
        private static string _ToJsonDelta(LiveObjectHandle liveObject, ILiveObjectResolver resolver, in SerializeOptions options)
        {
            // 1. currentのフルシリアライズ
            var currentJson = SerializeFullToJObject(liveObject, resolver, options);

            // 2. デフォルトJSONの取得（LiveObjectDefaultRegistryから）
            // デフォルトがない場合は差分なし（currentをデフォルトとみなす）。
            var defaultJson = LiveObjectDefaultRegistry.GetDefaults(liveObject);
            if (defaultJson == null)
            {
                defaultJson = currentJson;
            }

            // 3. JSON差分（forPersistence時は配列の末尾省略を行わない）
            var deltaToken = JsonDiff(defaultJson, currentJson, options.forPersistence);

            // 4. dirty追跡外の参照型プロパティ（非LiveClass）は
            //    getter が動的にインスタンスを生成するケースで JSON Diff をすり抜ける可能性があるため、
            //    current と default を直接比較して差分があれば強制的にデルタに含める
            deltaToken = _ForceIncludeUntrackedProperties(liveObject, currentJson, defaultJson, deltaToken, options);
            if (deltaToken == null || (deltaToken is JObject deltaCheck && !HasNonMetaProperties(deltaCheck)))
            {
                // 差分なし: メタデータのみ出力
                return SerializeToJson(_CreateMetadataJObject(liveObject, options.forPersistence));
            }

            // 4. メタデータを確保（JsonDiffが@typeなどを保持しているはずだが念のため）
            var deltaJson = deltaToken as JObject ?? new JObject();
            var metaTemplate = _CreateMetadataJObject(liveObject, options.forPersistence);
            foreach (var metaProp in metaTemplate.Properties())
            {
                if (deltaJson[metaProp.Name] == null)
                    deltaJson.AddFirst(new JProperty(metaProp.Name, metaProp.Value));
            }

            return SerializeToJson(deltaJson);
        }

        /// <summary>
        /// dirty追跡外の参照型プロパティ（LiveClassでないクラス型）を、
        /// current と default を JSON レベルで直接比較し、差分があればデルタに強制含める。
        /// これらのプロパティは getter が動的にインスタンスを生成するケース等で
        /// 通常の JsonDiff をすり抜ける可能性があるため、念のため直接比較する。
        /// readOnlyプロパティは除外する。差分が無ければデルタには含めない
        /// （未操作の再生終了で objects[] が空になるようにするため）。
        /// </summary>
        private static JToken _ForceIncludeUntrackedProperties(LiveObjectHandle liveObject, JObject currentJson, JObject defaultJson, JToken deltaToken, in SerializeOptions options)
        {
            var properties = liveObject.propertyTypes;
            JObject deltaObj = null;

            foreach (var propertyType in properties)
            {
                if (!propertyType.isValid) continue;
                if (propertyType.isReadOnly) continue;
                if (options.forPersistence && !propertyType.isPersistable) continue;
                // Project-scoped 参照プロパティを Scene delta に強制混入させない (逆も同様)。
                if (options.forPersistence && propertyType.persistScope != options.scopeFilter) continue;

                var value = LivePropertyUtility.GetValueRaw(liveObject.target, propertyType);
                if (value == null) continue;

                // 値型・string・LiveClass登録型はJSON Diffで正確に検出されるのでスキップ
                var valueType = value.GetType();
                if (valueType.IsValueType || value is string) continue;
                if (LiveClass.Find(valueType) != null) continue;

                // コレクション型で要素型がLiveClassの場合もスキップ
                // （空コレクションでも要素型で判定する。実行時の要素だけで判定すると空配列がすり抜ける）
                if (value is System.Collections.IEnumerable collection && !(value is string))
                {
                    bool isLiveClassCollection = false;

                    // まず要素型から判定（空コレクションでも正しく判定できる）
                    var elementType = LivePropertyUtility.GetCollectionElementType(propertyType.valueType);
                    if (elementType != null && LiveClass.Find(elementType) != null)
                    {
                        isLiveClassCollection = true;
                    }

                    // 要素型で判定できない場合は実際の要素から判定
                    if (!isLiveClassCollection)
                    {
                        foreach (var item in collection)
                        {
                            if (item != null && LiveClass.Find(item.GetType()) != null)
                            {
                                isLiveClassCollection = true;
                                break;
                            }
                        }
                    }
                    if (isLiveClassCollection) continue;
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
        private static string _ToJsonFull(LiveObjectHandle liveObject, ILiveObjectResolver resolver, in SerializeOptions options)
        {
            var jObject = SerializeFullToJObject(liveObject, resolver, options);
            return SerializeToJson(jObject);
        }

        internal static string ToJson(IEnumerable<LiveObjectHandle> liveObjects, ILiveObjectResolver resolver, int maxDepth = int.MaxValue)
        {
            if (liveObjects == null || !liveObjects.Any()) return "[]";

            // ToJson(liveObj) の「JObject → 文字列 → DeserializeObject で再パース」の往復を避け、
            // 各オブジェクトの JObject を直接構築して配列に詰める (isDirtyOnly=false / forPersistence=false の
            // フルシリアライズと同一)。最終シリアライズはプール済みバッファ経由。
            var options = new SerializeOptions(forPersistence: false, skipPropertyRef: false, PersistScope.Scene, maxDepth);

            var jArray = new JArray();

            foreach (var liveObj in liveObjects)
            {
                if (liveObj == null) continue;
                if (liveObj.propertyTypes == null) continue;

                jArray.Add(SerializeFullToJObject(liveObj, resolver, options));
            }

            var jResult = new JObject
            {
                ["objects"] = jArray
            };
            return SerializeToJson(jResult);
        }

        public static string ToJson(object value, ILiveObjectResolver resolver)
        {
            var jObject = new JObject
            {
                ["value"] = SerializeUnityType(resolver, value),
            };

            return SerializeToJson(jObject);
        }

        public static string ToJson(object value)
        {
            return ToJson(value, DefaultLiveObjectResolver.Instance);
        }

        /// <summary>
        /// プロパティを {value, id, path, changed} の JObject に変換する。
        /// 文字列化してから再パースする往復を避けたい呼び出し側はこちらを使う。
        /// </summary>
        public static JObject ToJObject(LiveProperty property, ILiveObjectResolver resolver)
        {
            // Image member: the JSON value is its own address, the getter is never invoked, and
            // "changed" is a constant — a picture is derived state with no baseline, and comparing
            // one would render a frame per poll.
            if (IsImageProperty(property.type))
            {
                var slashPath = property.path.ToSlash();
                return new JObject
                {
                    ["value"] = ImageAddress(property.owner.id, slashPath),
                    ["id"] = property.owner.id,
                    ["path"] = slashPath,
                    ["changed"] = false
                };
            }

            JToken valueToken;
            if (property.type.controlAttribute is ObjectSelectorAttribute)
            {
                valueToken = ObjectSelectorSerializer.SerializeObjectSelectorValue(property.GetValue(), forPersistence: false);
            }
            else if (property.type.controlAttribute is AssetSelectorAttribute)
            {
                valueToken = AssetSelectorSerializer.SerializeAssetSelectorValue(property.GetValue(), forPersistence: false);
            }
            else
            {
                valueToken = SerializeUnityType(resolver, property.GetValue());
            }

            return new JObject
            {
                ["value"] = valueToken,
                ["id"] = property.owner.id,
                ["path"] = property.path.ToSlash(),
                // エディタ (非再生) はエディタ自身の規則で答える。再生中とビルドは
                // セッション基準 (登録時からの差分)。詳しくは LiveEditorProperty を参照。
                ["changed"] = new JValue(LiveEditorProperty.isEditorRuleActive
                    ? LiveEditorProperty.IsChanged(property)
                    : property.isDirty)
            };
        }

        public static string ToJson(LiveProperty property, ILiveObjectResolver resolver)
        {
            return SerializeToJson(ToJObject(property, resolver));
        }

        // -------------------------------------------------------
        // FromJson (string JSON API)
        // -------------------------------------------------------

        public static object FromJson(string json, System.Type type, ILiveObjectResolver resolver)
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
            return FromJson(json, type, DefaultLiveObjectResolver.Instance);
        }

        public static T FromJson<T>(string json, ILiveObjectResolver resolver)
        {
            return (T)FromJson(json, typeof(T), resolver);
        }

        public static T FromJson<T>(string json)
        {
            return (T)FromJson(json, typeof(T), DefaultLiveObjectResolver.Instance);
        }

        internal static bool FromJson(string json, in LiveProperty property, ILiveObjectResolver resolver)
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
            var result = _FromJsonProperty(resolver, token, property);

            // For shadow-pair properties (Phase 3): _FromJsonProperty -> property.SetValue
            // -> propertyInfo.SetValue invokes the Property setter, which already runs
            // the side-effect Apply. Firing the owner's ILiveDeserializeCallback here
            // would double-apply (heavy work for callbacks like AvatarInput.ApplySettings).
            //
            // For non-shadow properties (e.g. nested LiveObjectHandle rewritten via
            // DeserializeLiveObject), the parent setter does NOT run, so the owner
            // callback is the only place that can re-apply parent-side state.
            if (result && property.type.shadowField == null)
            {
                (property.owner.target as ILiveDeserializeCallback)?.OnAfterLiveDeserialize();
                if (property.owner.targetType != null && property.owner.target == null)
                {
                    property.owner.targetType?.InvokeStaticAfterDeserialize();
                }
            }

            return result;
        }

        internal static bool FromJson(string json, in LiveProperty property)
        {
            return FromJson(json, property, DefaultLiveObjectResolver.Instance);
        }

        /// <summary>
        /// 新規追加された配列要素のデフォルト値キャプチャと参照型インスタンス置き換えを行う。
        /// SetDefaultValueでデフォルト値をキャプチャ後、参照型は新しいインスタンスに置き換える
        /// （デフォルト値参照がデシリアライズで変更されないようにする）。
        /// </summary>
        private static void _InitializeNewArrayElements(LiveProperty property, Type elementType, int startIndex)
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
                        var freshInstance = LivePropertyUtility.CreateDefaultElement(elementType);
                        elementProp.Value.SetValue(freshInstance, captureDefault: false);
                    }
                }
            }
        }

        /// <summary>
        /// プロパティに対して再帰的にデシリアライズとSetValueを行う
        /// 子プロパティも含めて全てSetValue経由で設定し、EnsureDefaultCapturedが呼ばれるようにする
        /// </summary>
        private static bool _FromJsonProperty(ILiveObjectResolver resolver, JToken token, LiveProperty property)
        {
            return _FromJsonProperty(resolver, token, property, captureDefaults: true);
        }

        // Phase 10: 段別 private ヘルパに逐語分解した dispatcher。
        // 判定順・SetValue 引数・captureDefaults 伝播・existingValue の単一取得位置・
        // 子再帰順は元実装と完全一致(挙動 byte 不変)。
        private static bool _FromJsonProperty(ILiveObjectResolver resolver, JToken token, LiveProperty property, bool captureDefaults)
        {
            // ① PropertyRef: 参照先の型でデシリアライズし、SetValue で参照先に委譲する
            if (property.type != null && property.type.isLivePropertyReference)
            {
                return _FromJsonRefProperty(resolver, token, property, captureDefaults);
            }

            // ② ポリモーフィック型不一致 (保存時の @type と実体型が兄弟サブクラス等で異なる) を
            // 入口で検出し短絡する。後続の Get/Set 経路 (existingValue 取得 / oldValue 取得 /
            // EnsureDefaultCaptured / SetValueRaw) が同じ警告を繰り返し出すのを防ぐ。
            if (LivePropertyUtility.WarnIfInstanceMismatch(property.obj, property.type))
            {
                return false;
            }

            var valueTypeForNull = property.type.valueType;

            // ③ UnityEngine.Object 派生フィールドに null token を受け取った場合は明示的に null 代入する。
            // (ObjectSelector の None 選択で RemoteApp が送ってくるケース)
            if ((token == null || token.Type == JTokenType.Null)
                && valueTypeForNull != null
                && typeof(UnityEngine.Object).IsAssignableFrom(valueTypeForNull))
            {
                property.SetValue(null, captureDefault: captureDefaults);
                return true;
            }

            if (token == null || token.Type == JTokenType.Null) return false;

            // ③b [RawJson]: a string member storing a JSON document. Take the embedded JSON
            // (object/array) as a compact string, or a legacy escaped-string value verbatim.
            if (property.type.isRawJson
                && TryDeserializeRawJson(property.type, token, out var rawJsonValue))
            {
                property.SetValue(rawJsonValue, captureDefault: captureDefaults);
                return true;
            }

            var existingValue = property.GetValue();
            var valueType = property.type.valueType;

            // ④a ObjectSelector: @ref を解決して fieldType に沿う値 (Component など) を代入する。
            // wrapper が GameObject を指す場合は GetComponent(fieldType) で取り出す。
            if (property.type.controlAttribute is ObjectSelectorAttribute
                && valueType != null
                && typeof(UnityEngine.Object).IsAssignableFrom(valueType))
            {
                var resolvedValue = ObjectSelectorSerializer.DeserializeObjectSelectorValue(resolver, token, valueType);
                property.SetValue(resolvedValue, captureDefault: captureDefaults);
                return true;
            }

            // ④a' AssetSelector: @guid (または素の guid 文字列) を AssetRegistry で解決して代入する。
            if (property.type.controlAttribute is AssetSelectorAttribute
                && valueType != null
                && typeof(UnityEngine.Object).IsAssignableFrom(valueType))
            {
                var resolvedAsset = AssetSelectorSerializer.DeserializeAssetSelectorValue(token, valueType);
                property.SetValue(resolvedAsset, captureDefault: captureDefaults);
                return true;
            }

            // ④b/④c 配列
            if (property.isArray && token is JArray jArray)
            {
                return _FromJsonArrayProperty(resolver, jArray, property, captureDefaults);
            }

            // ⑤ オブジェクト/構造体
            if (!property.type.isUnityPrimtive && token is JObject jObject)
            {
                return _FromJsonObjectProperty(resolver, jObject, property, valueType, existingValue, captureDefaults);
            }

            // ⑥ プリミティブ/Unity型
            return _FromJsonPrimitiveProperty(resolver, token, property, valueType, existingValue, captureDefaults);
        }

        // ① PropertyRef 段。元 _FromJsonProperty L1546-1555 を逐語抽出。
        private static bool _FromJsonRefProperty(ILiveObjectResolver resolver, JToken token, LiveProperty property, bool captureDefaults)
        {
            var resolvedType = property.type.resolvedValueType;
            if (resolvedType == null || resolvedType == typeof(LivePropertyRef))
            {
                return false;
            }
            if (token == null || token.Type == JTokenType.Null) return false;
            var refExistingValue = property.GetValue();
            var deserialized = DeserializeUnityType(resolver, token, resolvedType, refExistingValue);
            property.SetValue(deserialized, captureDefault: captureDefaults);
            return true;
        }

        // ④b/④c 配列段。delta は _FromJsonPropertyArrayDelta へ委譲。元 L1597-1641 を逐語抽出。
        private static bool _FromJsonArrayProperty(ILiveObjectResolver resolver, JArray jArray, LiveProperty property, bool captureDefaults)
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
            var elementType = LivePropertyUtility.GetCollectionElementType(property.type.valueType);

            // 読み取り専用でない場合のみ配列サイズを調整
            if (!property.type.isReadOnly)
            {
                // 配列のサイズを拡張（必要な場合）
                while (property.arrayLength < jArray.Count)
                {
                    // CreateDefaultElementで新規要素を追加
                    var defaultElement = LivePropertyUtility.CreateDefaultElement(elementType);
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

        // ⑤ オブジェクト/構造体段。元 L1646-1698 を逐語抽出 (valueType/existingValue は
        // dispatcher の単一取得値をそのまま受け取る)。
        // 不変条件: _EnsureDefaultsCapturedRecursive は SetValue より前 / 子再帰は SetValue より後 /
        // captureDefaults を子へ伝播。
        private static bool _FromJsonObjectProperty(ILiveObjectResolver resolver, JObject jObject, LiveProperty property, System.Type valueType, object existingValue, bool captureDefaults)
        {
            // デフォルト値キャプチャ（LiveSceneFromJson経由ではスキップ：
            // デフォルトはContainer.Initializeで既にキャプチャ済み。
            // DeserializeLiveObject内のSetValueRawが先に値を変更するため、
            // ここでキャプチャすると変更後の値がデフォルトになってしまう）
            if (captureDefaults)
            {
                _EnsureDefaultsCapturedRecursive(jObject, property);
            }

            var value = DeserializeUnityType(resolver, jObject, valueType, existingValue);

            // SetValueを子プロパティ再帰の前に実行（型切り替え後の参照を親に反映するため）
            property.SetValue(value, captureDefault: captureDefaults);

            // 実際の型でLiveClassを取得（型切り替え後の型を使用）
            var actualType = value != null ? value.GetType() : null;
            var liveClass = actualType != null ? LiveClass.Find(actualType) : null;

            // 子プロパティを再帰処理
            if (liveClass != null && liveClass.propertyTypes != null)
            {
                // LiveClassが登録されている場合はpropertyTypesを使用
                foreach (var childType in liveClass.propertyTypes)
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
                // LiveClassがない場合はJSONキーに基づいて子プロパティを処理
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

        // ⑥ プリミティブ/Unity型段。
        // Phase 11 是正: 旧実装は SetValue の captureDefault 引数を省略し常に true だった
        // (段①③④a⑤ は captureDefaults を明示伝播)。現行フローでは Container 取得済みパスは
        // 冪等吸収、新規配列要素は @op:"new" 完全出力のため観測可能な実害は未再現だが、
        // 段間の伝播一貫性回復(default 二重キャプチャ抑止指示の尊重)のため captureDefaults を伝播する。
        private static bool _FromJsonPrimitiveProperty(ILiveObjectResolver resolver, JToken token, LiveProperty property, System.Type valueType, object existingValue, bool captureDefaults)
        {
            // プリミティブ/Unity型の場合: 単純にSetValue（dirtyマーキングあり）
            var simpleValue = DeserializeUnityType(resolver, token, valueType, existingValue);
            if (simpleValue != null)
            {
                property.SetValue(simpleValue, captureDefault: captureDefaults);
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
        private static bool _FromJsonPropertyArrayDelta(ILiveObjectResolver resolver, JArray jArray, LiveProperty property, bool captureDefaults = true)
        {
            var elementType = LivePropertyUtility.GetCollectionElementType(property.type.valueType);

            int existingLength = property.arrayLength;

            // まず@op: "new" 要素の数をカウント
            int newCount = 0;
            foreach (var element in jArray)
            {
                if (_IsNewArrayElement(element))
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
                    var defaultElement = LivePropertyUtility.CreateDefaultElement(elementType);
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
                if (_IsNewArrayElement(element))
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
        private static void _EnsureDefaultsCapturedRecursive(JObject jObject, LiveProperty property)
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

        internal static bool FromJson(string json, LiveObjectHandle liveObject, ILiveObjectResolver resolver, bool captureDefaults = true)
        {
            if (string.IsNullOrEmpty(json)) return false;
            if (liveObject == null) throw new System.ArgumentNullException(nameof(liveObject));

            var jObject = JsonConvert.DeserializeObject<JObject>(json);
            if (jObject == null)
            {
                Debug.LogWarning("[RemoteControl] Failed to parse JSON");
                return false;
            }

            // type と id フィールドの検証（オプション）
            var jsonType = jObject["@type"]?.Value<string>();
            var jsonId = jObject["@id"]?.Value<string>();

            // A file written before a type rename carries the old @type. [FormerlyNamedAs] aliases
            // resolve to the same LiveClass, so they are not a mismatch. A null targetType has no
            // name to compare against and still reports, as before.
            if (!string.IsNullOrEmpty(jsonType) && liveObject.targetType?.IsCurrentOrFormerName(jsonType) != true)
            {
                Debug.LogWarning($"[RemoteControl] Type mismatch: expected '{liveObject.targetTypeName}', got '{jsonType}'");
            }

            // liveObject.id が空のケース (CreateUnregistered した pending エントリ用の一時オブジェクト等)
            // は比較対象が無いためスキップする。
            if (!string.IsNullOrEmpty(jsonId) && liveObject.hasId && jsonId != liveObject.id)
            {
                Debug.LogWarning($"[RemoteControl] ID mismatch: expected '{liveObject.id}', got '{jsonId}'");
            }

            // @name を name プロパティに復元
            var jsonName = jObject["@name"]?.Value<string>();
            if (!string.IsNullOrEmpty(jsonName))
            {
                var nameProperty = liveObject.FindProperty("name");
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
                && liveObject.target is LiveUnityObjectBase proxyBase)
            {
                var parentValue = parentToken.Type == JTokenType.Null ? null : parentToken.Value<string>();
                LiveObjectRegistry.SetParent(proxyBase.id, parentValue, out _);
            }

            // 各プロパティをデシリアライズして設定
            var properties = liveObject.propertyTypes;
            bool hasUpdates = false;

            foreach (var propType in properties)
            {
                if (!propType.isValid) continue;

                // JSONからプロパティ値を取得 (旧名 [FormerlyNamedAs] でも引けるようにする)
                var token = _GetTokenByPropertyName(jObject, propType);
                if (token == null || token.Type == JTokenType.Null) continue;

                if (propType.shadowField != null)
                {
                    // Shadow Field: backing field に直接書き込んで Property setter をバイパスする
                    // (Phase 1 の round-trip 決定性ゴール)。Apply 系副作用は ILiveDeserializeCallback で
                    // まとめて再実行する。
                    var existingValue = propType.shadowField.GetValue(liveObject.target);
                    var propValue = DeserializeUnityType(resolver, token, propType.valueType, existingValue);
                    if (propValue != null)
                    {
                        propType.shadowField.SetValue(liveObject.target, propValue);
                        hasUpdates = true;
                    }
                    continue;
                }

                // プロパティに値を設定（再帰的に子プロパティも処理）
                var property = new LiveProperty(propType, liveObject, liveObject.target);
                if (_FromJsonProperty(resolver, token, property, captureDefaults))
                {
                    hasUpdates = true;
                }
            }

            // _FromJsonProperty bypasses property setters when writing fields,
            // so types that need to apply deserialized values to external state
            // opt in via ILiveDeserializeCallback. Static-classed targets
            // (target == null) are handled by the convention-based static method.
            (liveObject.target as ILiveDeserializeCallback)?.OnAfterLiveDeserialize();
            if (liveObject.target == null)
            {
                liveObject.targetType?.InvokeStaticAfterDeserialize();
            }

            return hasUpdates;
        }

        internal static bool FromJson(string json, LiveObjectHandle liveObject)
        {
            return FromJson(json, liveObject, DefaultLiveObjectResolver.Instance);
        }

        // -------------------------------------------------------
        // Array element operations
        // 留置判断(Phase 9/10 再評価): Add はコア _FromJsonProperty に不可避依存のため
        // このクラスに留置。Remove/Reorder は _FromJsonProperty 非依存で独立化自体は
        // 可能だが薄いコマンドラッパで利得が無く直接テストも無いため同居維持。
        // -------------------------------------------------------

        internal static bool AddArrayElement(string json, in LiveProperty property)
        {
            if (string.IsNullOrEmpty(json)) return false;
            if (!property.isArray) return false;

            var jObject = JsonConvert.DeserializeObject<JObject>(json);
            var token = jObject["value"];
            if (token == null || token.Type == JTokenType.Null) return false;

            // 配列の要素型を取得
            var elementType = LivePropertyUtility.GetCollectionElementType(property.type.valueType);

            // デフォルト値で要素を追加（比較ベースのdirty判定のため、JSONの値ではなくデフォルト値で追加）
            var defaultElement = LivePropertyUtility.CreateDefaultElement(elementType);
            if (!property.Add(defaultElement))
            {
                return false;
            }

            // 追加された要素のプロパティを取得し、JSONの値を設定（デフォルト値との比較でdirtyが判定される）
            var newIndex = property.arrayLength - 1;
            var elementProp = property.GetPropertyIndex(newIndex);
            if (elementProp != null)
            {
                _FromJsonProperty(DefaultLiveObjectResolver.Instance, token, elementProp.Value);
            }

            return true;
        }

        internal static bool RemoveArrayElement(string json, in LiveProperty property)
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

        internal static bool ReorderArrayElement(string json, in LiveProperty property)
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
