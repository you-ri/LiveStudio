using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;
using Lilium.RemoteControl.Utility;
using Lilium.RemoteControl.Reflection;

using PropertyPath = Lilium.RemoteControl.Reflection.PropertyPath;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Lilium.RemoteControl
{


    public class ExposedObjectHandler : BaseRemoteControlApiHandler
    {
        [System.Serializable]
        struct SetPropertyRequest
        {
            public object value;
        }

        [System.Serializable]
        struct DeletePropertyRequest
        {
            public int index;
        }

        [System.Serializable]
        struct ReorderPropertyRequest
        {
            public int fromIndex;
            public int toIndex;
        }

        private readonly EndpointRoute[] _getRoutes;
        private readonly EndpointRoute[] _putRoutes;
        private readonly EndpointRoute[] _postRoutes;
        private readonly EndpointRoute[] _deleteRoutes;
        private readonly EndpointRoute[] _patchRoutes;

        public ExposedObjectHandler(RemoteControlServerCore server) : base(server)
        {
            // 内側ディスパッチ表。順序は元の if/else 連鎖と同一に保つこと。
            _getRoutes = new[]
            {
                new EndpointRoute("/exposed/objects", RouteMatch.Exact, HandleGetObjects),
                new EndpointRoute("/exposed/object/*/*", RouteMatch.Wildcard, HandleGetProperty),
                new EndpointRoute("/exposed/object/", RouteMatch.Prefix, HandleGetObject),
                new EndpointRoute("/exposed/types", RouteMatch.Exact, HandleGetTypes),
                new EndpointRoute("/exposed/enums", RouteMatch.Exact, HandleGetEnums),
            };
            _putRoutes = new[]
            {
                new EndpointRoute("/exposed/object/*/@parent", RouteMatch.Wildcard, HandleSetParent),
                new EndpointRoute("/exposed/object/*/*", RouteMatch.Wildcard, HandleSetProperty),
            };
            _postRoutes = new[]
            {
                new EndpointRoute("/exposed/object/*/*/reset", RouteMatch.Wildcard, HandleResetProperty),
                new EndpointRoute("/exposed/object/*/*", RouteMatch.Wildcard, HandleAddArrayElement),
                new EndpointRoute("/exposed/function/*", RouteMatch.Wildcard, HandleInvokeFunction),
            };
            _deleteRoutes = new[]
            {
                new EndpointRoute("/exposed/object/*/*", RouteMatch.Wildcard, HandleRemoveArrayElement),
            };
            _patchRoutes = new[]
            {
                new EndpointRoute("/exposed/object/*/*", RouteMatch.Wildcard, HandleReorderArrayElement),
            };
        }

        public override void Cleanup()
        {
        }

        private static readonly RouteRule[] _kRoutes =
        {
            new RouteRule("/exposed/object/", RouteMatch.Prefix),
            new RouteRule("/exposed/function/", RouteMatch.Prefix),
            new RouteRule("/exposed/objects", RouteMatch.Prefix),
            new RouteRule("/exposed/types", RouteMatch.Prefix),
            new RouteRule("/exposed/enums", RouteMatch.Prefix),
        };

        protected override IReadOnlyList<RouteRule> Routes => _kRoutes;

        protected override bool SupportsGet() => true;

        protected override bool SupportsPut() => true;

        protected override bool SupportsPost() => true;

        protected override bool SupportsDelete() => true;

        protected override bool SupportsPatch() => true;

        // 各 HTTP メソッドの内側ディスパッチはコンストラクタで構築した宣言表に委譲する。
        // GET の旧実装は AbsolutePath を .ToLower() してから CompareTo/StartsWith して
        // いたが、対象が ASCII リテラル (/exposed/...) と URL 由来パスのみのため
        // MatchPattern の OrdinalIgnoreCase 判定と完全に等価。
        protected override Task HandleGetRequest(HttpListenerContext context)
            => DispatchEndpoints(context, _getRoutes, "Invalid request format");

        protected override Task HandlePutRequest(HttpListenerContext context)
            => DispatchEndpoints(context, _putRoutes, "Invalid request format");

        protected override Task HandlePostRequest(HttpListenerContext context)
            => DispatchEndpoints(context, _postRoutes, "Invalid request format");

        protected override Task HandleDeleteRequest(HttpListenerContext context)
            => DispatchEndpoints(context, _deleteRoutes, "Invalid request format");

        protected override Task HandlePatchRequest(HttpListenerContext context)
            => DispatchEndpoints(context, _patchRoutes, "Invalid request format");

        private async Task HandleGetObjects(HttpListenerContext context)
        {
            var typeName = context.Request.QueryString["type"];
            var category = context.Request.QueryString["category"];

            var exposedObjects = await ExecuteOnMainThread(() => _CollectExposedObjects(typeName, category));
            var json = await ExecuteOnMainThread(() => ExposedPropertySerializer.ToJson(exposedObjects, GetResolver()));
            await WriteResponse(200, context.Response, json);
        }

        /// <summary>
        /// /exposed/objects の対象 ExposedObject 集合を収集する。
        /// category 指定 > typeName 指定 > 全件 の優先順。Unity API を含むため
        /// メインスレッド上で呼ぶこと。
        /// </summary>
        private IEnumerable<ExposedObject> _CollectExposedObjects(string typeName, string category)
        {
            var container = GetObjectContainer();
            if (container == null)
            {
                return Enumerable.Empty<ExposedObject>();
            }

            // カテゴリ指定
            if (!string.IsNullOrEmpty(category))
            {
                return FindExposedObjectsByCategory(category);
            }

            var instanceObjects = Enumerable.Empty<ExposedObject>();

            // TypeName指定なし
            if (string.IsNullOrEmpty(typeName))
            {
                // Containerに登録されているオブジェクト
                instanceObjects = container.objects
                    .Where(obj => obj?.exposedObject != null)
                    .Select(obj => obj.exposedObject);

                // Staticクラス
                var staticClasses = ExposedClass.all.Values
                    .Where(t => t.isStatic)
                    .Select(t => ExposedObjectRegistry.GetOrCreate(t.typeName, t, null));
                instanceObjects = instanceObjects.Concat(staticClasses);
            }
            // TypeName指定あり
            else
            {
                // TypeNameでフィルタリング
                instanceObjects = container.objects
                    .Where(obj => obj?.exposedObject != null)
                    .Select(obj => obj.exposedObject)
                    .Where(obj => obj != null && (typeName == null || obj.targetTypeName == typeName));

                // Staticクラス名指定
                var staticClasses = ExposedClass.all.Values
                    .Where(t => t.isStatic && (typeName == null || t.typeName == typeName))
                    .Select(t => ExposedObjectRegistry.GetOrCreate(t.typeName, t, null));
                instanceObjects = instanceObjects.Concat(staticClasses);

                var exposedClass = ExposedClass.Find(typeName);

                // コンポーネント型名指定
                if (exposedClass != null && exposedClass.type.IsSubclassOf(typeof(Component)))
                {
                    var list = GameObject.FindObjectsByType(exposedClass.type, FindObjectsInactive.Include, FindObjectsSortMode.None);
                    if (list != null && list.Length > 0)
                    {
                        var foundObjects = list.Select(v =>
                        {
                            return ExposedObjectRegistry.FindByTarget(v) ?? ExposedObject.CreateUnregistered(exposedClass, v);
                        });
                        instanceObjects = instanceObjects.Concat(foundObjects);
                    }
                }
            }

            return instanceObjects;
        }


        private async Task HandleGetObject(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            var id = PathParser.GetPathSegment(path, 2);

            var exposedObject = await ExecuteOnMainThread(() =>
            {
                var exposedObject = FindExposedObjectById(id);

                return exposedObject;
            });

            if (exposedObject != null)
            {
                var json = await ExecuteOnMainThread(() => ExposedPropertySerializer.ToJson(exposedObject, GetResolver()));

                await WriteResponse(200, context.Response, json);
                return;
            }

            await WriteError(context, 404, "Object not found");
        }

        /// <summary>
        /// プロパティ系エンドポイント (/exposed/object/{id}/{slashPath}) の共通定型をまとめた
        /// パイプラインに渡すコンテキスト。メインスレッド上で <see cref="onProperty"/> に渡される。
        /// </summary>
        private readonly struct PropertyPipelineContext
        {
            public readonly ExposedObject exposedObject;
            public readonly string id;
            public readonly string slashPath;
            public readonly string propertyPath; // DotBracket 形式 (PropertyPath.Value)
            public readonly string body;         // readBody=false の場合は null

            public PropertyPipelineContext(ExposedObject exposedObject, string id,
                string slashPath, string propertyPath, string body)
            {
                this.exposedObject = exposedObject;
                this.id = id;
                this.slashPath = slashPath;
                this.propertyPath = propertyPath;
                this.body = body;
            }
        }

        /// <summary>
        /// onProperty の結果。成功時は 200 + <see cref="body"/>、
        /// 失敗時は <see cref="errorStatus"/> + {"error": <see cref="errorMessage"/>}。
        /// </summary>
        private readonly struct PropertyResult
        {
            public readonly bool ok;
            public readonly string body;
            public readonly int errorStatus;
            public readonly string errorMessage;

            private PropertyResult(bool ok, string body, int errorStatus, string errorMessage)
            {
                this.ok = ok;
                this.body = body;
                this.errorStatus = errorStatus;
                this.errorMessage = errorMessage;
            }

            public static PropertyResult Success(string body)
                => new PropertyResult(true, body, 0, null);

            public static PropertyResult Error(int status, string message)
                => new PropertyResult(false, null, status, message);
        }

        /// <summary>
        /// /exposed/object/{id}/{slashPath} 系の共通定型:
        /// id/slashPath 解析 → ExposedObject 解決 → (任意で body 読込) →
        /// メインスレッドで <paramref name="onProperty"/> 実行 → 応答書き込み。
        /// 一貫した REST エラースキーム: パス不正=400 "Invalid request format"、
        /// オブジェクト未解決=404 "Object not found"、プロパティ未解決=404
        /// "Property not found"、操作失敗=400。成功時の 200 本文は onProperty が
        /// 生成した文字列をそのまま返す(成功パスは従来挙動を維持)。
        /// </summary>
        private async Task RunPropertyPipeline(
            HttpListenerContext context,
            bool readBody,
            bool stripResetSuffix,
            Func<PropertyPipelineContext, PropertyResult> onProperty)
        {
            var path = context.Request.Url.AbsolutePath;
            if (stripResetSuffix && path.EndsWith("/reset"))
            {
                path = path.Substring(0, path.Length - "/reset".Length);
            }

            var id = PathParser.GetPathSegment(path, 2);
            var slashPath = PathParser.GetPathSegmentFrom(path, 3);

            if (id == null || slashPath == null)
            {
                await WriteError(context, 400, "Invalid request format");
                return;
            }

            // Slash形式からDotBracket形式に変換
            var propertyPath = PropertyPath.FromSlash(slashPath);

            var exposedObject = await ExecuteOnMainThread(() => FindExposedObjectById(id));

            if (exposedObject == null)
            {
                await WriteError(context, 404, "Object not found");
                return;
            }

            var body = readBody ? await ReadRequestBody(context.Request) : null;

            var pipelineContext = new PropertyPipelineContext(
                exposedObject, id, slashPath, propertyPath.Value, body);

            var result = await ExecuteOnMainThread(() => onProperty(pipelineContext));

            if (!result.ok)
            {
                await WriteError(context, result.errorStatus, result.errorMessage);
                return;
            }

            await WriteResponse(200, context.Response, result.body);
        }

        private Task HandleGetProperty(HttpListenerContext context)
        {
            return RunPropertyPipeline(context, readBody: false, stripResetSuffix: false,
                onProperty: ctx =>
                {
                    var property = ctx.exposedObject.FindProperty(ctx.propertyPath);
                    if (!property.HasValue)
                    {
                        return PropertyResult.Error(404, "Property not found");
                    }

                    var json = ExposedPropertySerializer.ToJson(property.Value, GetResolver());
                    return PropertyResult.Success(json);
                });
        }

        private Task HandleSetProperty(HttpListenerContext context)
        {
            return RunPropertyPipeline(context, readBody: true, stripResetSuffix: false,
                onProperty: ctx =>
                {
                    var property = ctx.exposedObject.FindProperty(ctx.propertyPath);
                    if (property == null)
                    {
                        return PropertyResult.Error(404, "Property not found");
                    }

                    var prop = property.Value;
                    var result = ExposedPropertySerializer.FromJson(ctx.body, in prop);
                    if (!result)
                    {
                        return PropertyResult.Error(400, "Failed to set property");
                    }

                    var json = ExposedPropertySerializer.ToJson(property.Value, GetResolver());

                    // onPropertyChanged で親要素の他フィールドが書き換わる場合に備え、
                    // 親が配列要素ならその要素全体を SSE でブロードキャストする。
                    // 親インスタンスは property.obj で既に手元にあるので、登録済み ExposedObject を
                    // 検索するのではなく、その場で CreateUnregistered で ExposedObject を作って使う。
                    _BroadcastParentElement(ctx.id, ctx.slashPath, property.Value);

                    return PropertyResult.Success(json);
                });
        }

        private Task HandleAddArrayElement(HttpListenerContext context)
        {
            return RunPropertyPipeline(context, readBody: true, stripResetSuffix: false,
                onProperty: ctx =>
                {
                    var property = ctx.exposedObject.FindProperty(ctx.propertyPath);
                    if (property == null)
                    {
                        return PropertyResult.Error(404, "Property not found");
                    }

                    var prop = property.Value;
                    return ExposedPropertySerializer.AddArrayElement(ctx.body, in prop)
                        ? PropertyResult.Success("{}")
                        : PropertyResult.Error(400, "Failed to add array element");
                });
        }

        private Task HandleRemoveArrayElement(HttpListenerContext context)
        {
            return RunPropertyPipeline(context, readBody: true, stripResetSuffix: false,
                onProperty: ctx =>
                {
                    var property = ctx.exposedObject.FindProperty(ctx.propertyPath);
                    if (property == null)
                    {
                        return PropertyResult.Error(404, "Property not found");
                    }

                    var prop = property.Value;
                    return ExposedPropertySerializer.RemoveArrayElement(ctx.body, in prop)
                        ? PropertyResult.Success("{}")
                        : PropertyResult.Error(400, "Failed to remove array element");
                });
        }

        private Task HandleReorderArrayElement(HttpListenerContext context)
        {
            return RunPropertyPipeline(context, readBody: true, stripResetSuffix: false,
                onProperty: ctx =>
                {
                    var property = ctx.exposedObject.FindProperty(ctx.propertyPath);
                    if (property == null)
                    {
                        return PropertyResult.Error(404, "Property not found");
                    }

                    var prop = property.Value;
                    return ExposedPropertySerializer.ReorderArrayElement(ctx.body, in prop)
                        ? PropertyResult.Success("{}")
                        : PropertyResult.Error(400, "Failed to reorder array element");
                });
        }

        private Task HandleResetProperty(HttpListenerContext context)
        {
            // Reset は body を消費するが未使用(InputStream 消費タイミング維持のため readBody:true)。
            return RunPropertyPipeline(context, readBody: true, stripResetSuffix: true,
                onProperty: ctx =>
                {
                    var property = ctx.exposedObject.FindProperty(ctx.propertyPath);
                    if (property == null)
                    {
                        return PropertyResult.Error(404, "Property not found");
                    }

                    var prop = property.Value;
                    ExposedPropertyUtility.ResetValue(ctx.exposedObject, in prop);

                    var newProperty = ctx.exposedObject.FindProperty(ctx.propertyPath);
                    var json = ExposedPropertySerializer.ToJson(newProperty.Value, GetResolver());
                    return PropertyResult.Success(json);
                });
        }


        private async Task HandleGetTypes(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            var typeName = context.Request.QueryString["type"];

            // ObjectSelector のシーン列挙など、メインスレッド専用の Unity API を ToJson 内で呼ぶ可能性があるため、
            // types のシリアライズはメインスレッドで実行する。
            var json = await ExecuteOnMainThread(() =>
            {
                var exposedTypes = ExposedClass.all.Values
                    .Where(t => t.typeName == typeName || typeName == null);
                return ExposedTypeInfoSerializer.ToJson(exposedTypes);
            });

            await WriteResponse(200, context.Response, json);
            return;
        }

        private async Task HandleGetEnums(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            var typeName = context.Request.QueryString["type"];

            var exposedEnums = ExposedEnum.all.Values
                .Where(e => e.typeName == typeName || typeName == null);

            var json = ExposedTypeInfoSerializer.ToJson(exposedEnums);

            await WriteResponse(200, context.Response, json);
            return;
        }

        private async Task HandleInvokeFunction(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            var id = PathParser.GetPathSegment(path, 2);
            // 第3セグメント以降をすべて取得（例: "controller/resetyawangle"）
            var functionPath = PathParser.GetPathSegmentFrom(path, 3);

            if (id == null || functionPath == null)
            {
                await WriteError(context, 400, "Invalid request format");
                return;
            }

            // 最後の/で分割してプロパティパスと関数名に分離
            string propertyPath = null;
            string functionName = functionPath;
            var lastSlashIndex = functionPath.LastIndexOf('/');
            if (lastSlashIndex >= 0)
            {
                propertyPath = functionPath.Substring(0, lastSlashIndex);
                functionName = functionPath.Substring(lastSlashIndex + 1);
            }

            var exposedObject = await ExecuteOnMainThread(() =>
            {
                return FindExposedObjectById(id);
            });

            if (exposedObject == null)
            {
                await WriteError(context, 404, "Object not found");
                return;
            }

            var body = await ReadRequestBody(context.Request);

            // 関数の検証とパラメータの準備、実行をすべてメインスレッドで行う
            var result = await ExecuteOnMainThread(() =>
            {
                var function = _ResolveInvokeFunction(
                    exposedObject, propertyPath, functionName, id, out var functionTarget);
                if (function == null)
                {
                    return (success: false, invokeResult: (object)null);
                }

                var args = _BuildInvokeArguments(function, body);
                var invokeResult = function.Invoke(functionTarget, args);
                return (success: true, invokeResult);
            });

            if (!result.success)
            {
                await WriteError(context, 400, "Function not found or failed to parse arguments");
                return;
            }

            // 結果をJSON形式で返す
            var resultJson = new JObject();
            if (result.invokeResult != null)
            {
                resultJson["result"] = ExposedPropertySerializer.SerializeUnityType(GetResolver(), result.invokeResult);
            }
            else
            {
                resultJson["result"] = JValue.CreateNull();
            }

            await WriteResponse(200, context.Response, JsonConvert.SerializeObject(resultJson));
        }

        /// <summary>
        /// 呼び出す関数とその実行対象を解決する。propertyPath があればその
        /// プロパティ値の型から、無ければ exposedObject 直接から関数を検索する。
        /// 解決できなければ null(理由は Debug.LogError で出力)。メインスレッド前提。
        /// </summary>
        private ExposedFunctionType _ResolveInvokeFunction(
            ExposedObject exposedObject, string propertyPath, string functionName,
            string id, out object functionTarget)
        {
            functionTarget = null;

            if (!string.IsNullOrEmpty(propertyPath))
            {
                // Slash形式からDotBracket形式に変換してプロパティパスをたどる
                var convertedPath = PropertyPath.FromSlash(propertyPath);
                var property = exposedObject.FindProperty(convertedPath.Value);
                if (property == null)
                {
                    Debug.LogError($"[RemoteControl] Property '{propertyPath}' not found on object '{id}'");
                    return null;
                }

                var propertyValue = property.Value.GetValue();
                if (propertyValue == null)
                {
                    Debug.LogError($"[RemoteControl] Property value is null for path '{propertyPath}'");
                    return null;
                }

                // プロパティの値の型からExposedClassを取得
                var propertyType = propertyValue.GetType();
                var exposedClass = ExposedClass.Get(propertyType);
                if (exposedClass == null)
                {
                    Debug.LogError($"[RemoteControl] ExposedClass not found for type '{propertyType.Name}'");
                    return null;
                }

                // ExposedClassから関数を検索
                functionTarget = propertyValue;
                return exposedClass.FindFunction(functionName);
            }

            // 従来の動作：直接オブジェクトから関数を検索
            functionTarget = exposedObject.target;
            return exposedObject.GetFunction(functionName);
        }

        /// <summary>
        /// リクエストボディ JSON の "args" 配列から関数引数を構築する。
        /// パラメータ個数ベースで確保し、未指定/null は HasDefaultValue があれば
        /// 既定値、無ければ型の default を使う。body 空 or args 無しなら null。
        /// </summary>
        private object[] _BuildInvokeArguments(ExposedFunctionType function, string body)
        {
            object[] args = null;
            var parameters = function.parameters;

            // リクエストボディからパラメータを取得
            if (!string.IsNullOrEmpty(body))
            {
                var jObject = JsonConvert.DeserializeObject<JObject>(body);
                var argsToken = jObject["args"];

                if (argsToken != null && argsToken is JArray jArray)
                {
                    // パラメータ個数ベースで配列を確保。送られて来た要素を埋め、
                    // 未指定/null の位置は HasDefaultValue があれば既定値、無ければ型の default を使う。
                    args = new object[parameters.Length];
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
                            //TODO: DeseirializeExposedObjectを使うべきか？
                            args[i] = ExposedPropertySerializer.DeserializeUnityType(DefaultExposedObjectResolver.Instance, jToken, param.ParameterType);
                        }
                    }
                }
            }

            return args;
        }

        private ExposedObject FindExposedObjectById(string id)
        {
            var container = GetObjectContainer();

            // Container に登録されているオブジェクトで検索（propertyName + グローバル _byId フォールバック）
            if (container != null)
            {
                var exposedObject = container.FindById(id);
                if (exposedObject != null)
                {
                    return exposedObject;
                }
            }

            // staticクラス名で検索 (target=null で生成できるのは static class のみ。
            // MonoBehaviour 等の非 static 型を null target で生成すると ExposedObject.cs:47 の警告が出て、
            // 以降の SetValue などが target=null に対して走り壊れる)
            var exposedType = ExposedClass.Find(id);
            if (exposedType != null && exposedType.isStatic)
            {
                var exposedObject = ExposedObjectRegistry.GetOrCreate(exposedType.typeName, exposedType, null);
                return exposedObject;
            }

            // instance id で検索 (typeName を id として割り当てた MonoBehaviour 等もここでヒットする)
            if (ExposedObjectRegistry.TryFindById(id, out var instanceObject))
            {
                return instanceObject;
            }

            // 非 static の ExposedClass で、シーン上のインスタンスを target として登録を試みる。
            // ExposedPropertyRef.Resolve と同じ救済ロジック。
            if (exposedType != null
                && exposedType.type != null
                && typeof(Component).IsAssignableFrom(exposedType.type))
            {
                var sceneTarget = UnityEngine.Object.FindFirstObjectByType(exposedType.type, FindObjectsInactive.Include);
                if (sceneTarget != null)
                {
                    return ExposedObjectRegistry.GetOrCreate(exposedType.typeName, exposedType, sceneTarget);
                }
            }

            // 最終フォールバック: 数値 instanceId として Unity の内部 API から逆引きして
            // 未登録の UnityEngine.Object を一時的な ExposedObject にラップする（レジストリ登録しない）
            if (int.TryParse(id, out var unityInstanceId))
            {
                var unityObj = ExposedObjectUtility.InstanceIDToObject(unityInstanceId);
                if (unityObj != null)
                {
                    var exposedClass = ExposedClass.Find(unityObj.GetType());
                    if (exposedClass != null)
                    {
                        return ExposedObject.CreateUnregistered(exposedClass, unityObj);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// カテゴリに一致するExposedObjectを収集する。
        /// </summary>
        private List<ExposedObject> FindExposedObjectsByCategory(string category)
        {
            return ExposedObjectRegistry.FindByCategory(category, GetObjectContainer());
        }

        /// <summary>
        /// PUT /exposed/object/{id}/@parent のリクエストボディ。
        /// </summary>
        [System.Serializable]
        struct SetParentRequest
        {
            public string parentId;
        }

        /// <summary>
        /// PUT /exposed/object/{id}/@parent: ExposedObject 同士の親子関係を変更する。
        /// body: { "parentId": "...id..." | null }
        /// 成功時は child 全体のフルシリアライズを返しつつ SSE で exposed_object_updated を broadcast。
        /// </summary>
        private async Task HandleSetParent(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            var id = PathParser.GetPathSegment(path, 2);
            if (string.IsNullOrEmpty(id))
            {
                await WriteError(context, 400, "Invalid request format");
                return;
            }

            var body = await ReadRequestBody(context.Request);

            string parentId = null;
            try
            {
                var jObj = string.IsNullOrWhiteSpace(body) ? null : JObject.Parse(body);
                if (jObj != null && jObj.TryGetValue("parentId", out var token))
                {
                    parentId = token.Type == JTokenType.Null ? null : token.Value<string>();
                }
            }
            catch (JsonException)
            {
                await WriteError(context, 400, "Invalid JSON body");
                return;
            }

            var result = await ExecuteOnMainThread(() =>
            {
                var ok = ExposedObjectRegistry.SetParent(id, parentId, out var err);
                if (!ok) return (ok: false, error: err, value: (JObject)null);

                var child = ExposedObjectRegistry.FindById(id);
                JObject value = null;
                if (child != null)
                {
                    value = ExposedPropertySerializer.SerializeFullToJObject(
                        child, GetResolver());
                    _BroadcastParentChanged(id, value);
                }
                return (ok: true, error: (string)null, value: value);
            });

            if (!result.ok)
            {
                await WriteError(context, 400, result.error ?? "Unknown error");
                return;
            }

            var responseJson = result.value != null
                ? JsonConvert.SerializeObject(result.value, Formatting.None)
                : "{}";
            await WriteResponse(200, context.Response, responseJson);
        }

        /// <summary>
        /// exposed_object_updated メッセージを生成する。キー挿入順
        /// [type,id,path,value,changed] は Newtonsoft 直列化バイトに影響するため厳守。
        /// </summary>
        private static JObject _CreateExposedObjectUpdatedMessage(string id, string path, JObject value, bool changed)
        {
            return new JObject
            {
                ["type"] = "exposed_object_updated",
                ["id"] = id,
                ["path"] = path,
                ["value"] = value,
                ["changed"] = changed
            };
        }

        /// <summary>
        /// @parent 変更を SSE で broadcast する。child 全体のフルシリアライズを送り、
        /// 受信側 (RemoteApp) はツリー表示を再構築する。
        /// </summary>
        private static void _BroadcastParentChanged(string childId, JObject valueJObject)
        {
            var message = _CreateExposedObjectUpdatedMessage(childId, "", valueJObject, true);

            foreach (var instance in RemoteControlServerManager.servers.Values)
            {
                _ = instance.server?.BroadcastMessage(message, "exposed_object_updated");
            }
        }

        /// <summary>
        /// SetProperty 完了後、変更された葉プロパティが配列要素内のフィールドだった場合、
        /// その要素全体を exposed_object_updated SSE でブロードキャストする。
        /// 目的は、onPropertyChanged を介して書き換わった依存フィールド（ShowIf 参照先など）を
        /// 他クライアントに伝播させること。親インスタンスは property.obj から直接取得し、
        /// その場で CreateUnregistered の ExposedObject を生成してシリアライズする。
        /// </summary>
        private static void _BroadcastParentElement(string requestId, string slashPath, ExposedProperty property)
        {
            if (string.IsNullOrEmpty(slashPath)) return;

            var parts = slashPath.Split('/');
            if (parts.Length < 2) return;

            // 葉の一つ上のセグメントが数値なら、親は配列要素。
            if (!int.TryParse(parts[parts.Length - 2], out _)) return;

            var parentInstance = property.obj;
            if (parentInstance == null) return;

            var parentClass = ExposedClass.Find(parentInstance.GetType());
            if (parentClass == null) return;

            var parentSlashPath = string.Join("/", parts, 0, parts.Length - 1);
            var parentExposed = ExposedObject.CreateUnregistered(parentClass, parentInstance);
            var valueJObject = ExposedPropertySerializer.SerializeFullToJObject(
                parentExposed, DefaultExposedObjectResolver.Instance);

            var message = _CreateExposedObjectUpdatedMessage(requestId, parentSlashPath, valueJObject, true);

            foreach (var instance in RemoteControlServerManager.servers.Values)
            {
                _ = instance.server?.BroadcastMessage(message, "exposed_object_updated");
            }
        }

    }
}



