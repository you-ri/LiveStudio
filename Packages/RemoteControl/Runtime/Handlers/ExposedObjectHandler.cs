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

        public ExposedObjectHandler(RemoteControlServerCore server) : base(server)
        {
        }

        private IExposedObjectResolver _GetResolver()
        {
            return GetObjectContainer() ?? (IExposedObjectResolver)DefaultExposedObjectResolver.Instance;
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

        protected override async Task HandleGetRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath.ToLower();

            if (path.CompareTo("/exposed/objects") == 0)
            {
                await HandleGetObjects(context);
                return;
            }
            else if (PathParser.IsMatchIgnoreCase(path, "/exposed/object/*/*"))
            {
                await HandleGetProperty(context);
                return;
            }
            else if (path.StartsWith("/exposed/object/"))
            {
                await HandleGetObject(context);
                return;
            }
            else if (path.CompareTo("/exposed/types") == 0)
            {
                await HandleGetTypes(context);
                return;
            }
            else if (path.CompareTo("/exposed/enums") == 0)
            {
                await HandleGetEnums(context);
                return;
            }

            await WriteResponse(400, context.Response, "{\"error\":\"Invalid request format\"}");
        }

        protected override async Task HandlePutRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;

            // 親子関係変更 (メタデータ PUT): /exposed/object/{id}/@parent
            if (PathParser.IsMatchIgnoreCase(path, "/exposed/object/*/@parent"))
            {
                await HandleSetParent(context);
                return;
            }

            if (PathParser.IsMatchIgnoreCase(path, "/exposed/object/*/*"))
            {
                await HandleSetProperty(context);
                return;
            }

            await WriteResponse(400, context.Response, "{\"error\":\"Empty request body\"}");
        }

        protected override async Task HandlePostRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;

            if (PathParser.IsMatchIgnoreCase(path, "/exposed/object/*/*/reset"))
            {
                await HandleResetProperty(context);
                return;
            }
            // 配列要素追加はパスが5セグメント以上の場合（プロパティパスを含む）
            else if (PathParser.IsMatchIgnoreCase(path, "/exposed/object/*/*"))
            {
                await HandleAddArrayElement(context);
                return;
            }
            // 関数呼び出し
            else if (PathParser.IsMatchIgnoreCase(path, "/exposed/function/*"))
            {
                await HandleInvokeFunction(context);
                return;
            }

            await WriteResponse(400, context.Response, "{\"error\":\"Invalid request format\"}");
        }

        protected override async Task HandleDeleteRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;

            if (PathParser.IsMatchIgnoreCase(path, "/exposed/object/*/*"))
            {
                await HandleRemoveArrayElement(context);
                return;
            }

            await WriteResponse(400, context.Response, "{\"error\":\"Invalid request format\"}");
        }

        protected override async Task HandlePatchRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;

            if (PathParser.IsMatchIgnoreCase(path, "/exposed/object/*/*"))
            {
                await HandleReorderArrayElement(context);
                return;
            }

            await WriteResponse(400, context.Response, "{\"error\":\"Invalid request format\"}");
        }

        private async Task HandleGetObjects(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            var typeName = context.Request.QueryString["type"];
            var category = context.Request.QueryString["category"];

            var exposedObjects = await ExecuteOnMainThread(() =>
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
            });

            var json = await ExecuteOnMainThread(() => ExposedPropertySerializer.ToJson(exposedObjects, GetObjectContainer() ?? (IExposedObjectResolver)DefaultExposedObjectResolver.Instance));
            await WriteResponse(200, context.Response, json);
            return;
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
                var json = await ExecuteOnMainThread(() => ExposedPropertySerializer.ToJson(exposedObject, GetObjectContainer() ?? (IExposedObjectResolver)DefaultExposedObjectResolver.Instance));

                await WriteResponse(200, context.Response, json);
                return;
            }

            await WriteResponse(404, context.Response, "{\"error\":\"Object not found\"}");
        }

        private async Task HandleGetProperty(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            var id = PathParser.GetPathSegment(path, 2);
            var slashPath = PathParser.GetPathSegmentFrom(path, 3);

            if (id == null || slashPath == null)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Invalid request format\"}");
                return;
            }

            // Slash形式からDotBracket形式に変換
            var propertyPath = PropertyPath.FromSlash(slashPath);

            var exposedObject = await ExecuteOnMainThread(() =>
            {
                var exposedObject = FindExposedObjectById(id);

                return exposedObject;
            });

            if (exposedObject == null)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Invalid request format\"}");
                return;
            }

            var response = await ExecuteOnMainThread(() =>
            {
                var property = exposedObject.FindProperty(propertyPath.Value);
                if (!property.HasValue)
                {
                    return null;
                }

                var json = ExposedPropertySerializer.ToJson(property.Value, _GetResolver());
                return json;
            });

            if (response == null)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Property not found\"}");
                return;
            }

            await WriteResponse(200, context.Response, response);
            return;
        }


        private async Task HandleSetProperty(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            var id = PathParser.GetPathSegment(path, 2);
            var slashPath = PathParser.GetPathSegmentFrom(path, 3);

            if (id == null || slashPath == null)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Invalid request format\"}");
                return;
            }

            // Slash形式からDotBracket形式に変換
            var propertyPath = PropertyPath.FromSlash(slashPath);

            var exposedObject = await ExecuteOnMainThread(() =>
            {
                var exposedObject = FindExposedObjectById(id);

                return exposedObject;
            });

            if (exposedObject == null)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Object not found\"}");
                return;
            }

            var body = await ReadRequestBody(context.Request);

            var response = await ExecuteOnMainThread(() =>
            {
                var property = exposedObject.FindProperty(propertyPath.Value);
                if (property == null)
                {
                    return null;
                }

                var prop = property.Value;
                var result = ExposedPropertySerializer.FromJson(body, in prop);
                if (!result)
                {
                    return null;
                }

                var json = ExposedPropertySerializer.ToJson(property.Value, _GetResolver());

                // onPropertyChanged で親要素の他フィールドが書き換わる場合に備え、
                // 親が配列要素ならその要素全体を SSE でブロードキャストする。
                // 親インスタンスは property.obj で既に手元にあるので、登録済み ExposedObject を
                // 検索するのではなく、その場で CreateUnregistered で ExposedObject を作って使う。
                _BroadcastParentElement(id, slashPath, property.Value);

                return json;
            });

            if (response == null)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Property not found\"}");
                return;
            }

            await WriteResponse(200, context.Response, response);
            return;
        }


        private async Task HandleAddArrayElement(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            var id = PathParser.GetPathSegment(path, 2);
            var slashPath = PathParser.GetPathSegmentFrom(path, 3);

            if (id == null || slashPath == null)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Invalid request format\"}");
                return;
            }

            // Slash形式からDotBracket形式に変換
            var propertyPath = PropertyPath.FromSlash(slashPath);

            var exposedObject = await ExecuteOnMainThread(() =>
            {
                return FindExposedObjectById(id);
            });

            if (exposedObject == null)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Object not found\"}");
                return;
            }

            var body = await ReadRequestBody(context.Request);

            bool result = await ExecuteOnMainThread(() =>
            {
                var property = exposedObject.FindProperty(propertyPath.Value);
                if (property == null)
                {
                    return false;
                }

                var prop = property.Value;
                return ExposedPropertySerializer.AddArrayElement(body, in prop);
            });

            if (!result)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Failed to add array element\"}");
                return;
            }

            await WriteResponse(200, context.Response, "{}");
        }

        private async Task HandleRemoveArrayElement(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            var id = PathParser.GetPathSegment(path, 2);
            var slashPath = PathParser.GetPathSegmentFrom(path, 3);

            if (id == null || slashPath == null)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Invalid request format\"}");
                return;
            }

            // Slash形式からDotBracket形式に変換
            var propertyPath = PropertyPath.FromSlash(slashPath);

            var exposedObject = await ExecuteOnMainThread(() =>
            {
                return FindExposedObjectById(id);
            });

            if (exposedObject == null)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Object not found\"}");
                return;
            }

            var body = await ReadRequestBody(context.Request);

            bool result = await ExecuteOnMainThread(() =>
            {
                var property = exposedObject.FindProperty(propertyPath.Value);
                if (property == null)
                {
                    return false;
                }

                var prop = property.Value;
                return ExposedPropertySerializer.RemoveArrayElement(body, in prop);
            });

            if (!result)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Failed to remove array element\"}");
                return;
            }

            await WriteResponse(200, context.Response, "{}");
        }

        private async Task HandleReorderArrayElement(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            var id = PathParser.GetPathSegment(path, 2);
            var slashPath = PathParser.GetPathSegmentFrom(path, 3);

            if (id == null || slashPath == null)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Invalid request format\"}");
                return;
            }

            // Slash形式からDotBracket形式に変換
            var propertyPath = PropertyPath.FromSlash(slashPath);

            var exposedObject = await ExecuteOnMainThread(() =>
            {
                return FindExposedObjectById(id);
            });

            if (exposedObject == null)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Object not found\"}");
                return;
            }

            var body = await ReadRequestBody(context.Request);

            bool result = await ExecuteOnMainThread(() =>
            {
                var property = exposedObject.FindProperty(propertyPath.Value);
                if (property == null)
                {
                    return false;
                }

                var prop = property.Value;
                return ExposedPropertySerializer.ReorderArrayElement(body, in prop);
            });

            if (!result)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Failed to reorder array element\"}");
                return;
            }

            await WriteResponse(200, context.Response, "{}");
        }

        private async Task HandleResetProperty(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            if (path.EndsWith("/reset"))
            {
                path = path.Substring(0, path.Length - "/reset".Length);
            }
            var id = PathParser.GetPathSegment(path, 2);
            var slashPath = PathParser.GetPathSegmentFrom(path, 3);

            if (id == null || slashPath == null)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Invalid request format\"}");
                return;
            }

            // Slash形式からDotBracket形式に変換
            var propertyPath = PropertyPath.FromSlash(slashPath);

            var exposedObject = await ExecuteOnMainThread(() =>
            {
                var exposedObject = FindExposedObjectById(id);
                return exposedObject;
            });

            if (exposedObject == null)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Object not found\"}");
                return;
            }

            var body = await ReadRequestBody(context.Request);

            var response = await ExecuteOnMainThread(() =>
            {
                //Debug.Log($"[RemoteControl] Resetting property '{propertyPath.Value}' on object '{id}'");
                var property = exposedObject.FindProperty(propertyPath.Value);
                if (property == null)
                {
                    return null;
                }

                var prop = property.Value;
                var result = ExposedPropertyUtility.ResetValue(exposedObject, in prop);

                var newProperty = exposedObject.FindProperty(propertyPath.Value);
                var json = ExposedPropertySerializer.ToJson(newProperty.Value, _GetResolver());
                return json;
            });

            if (response == null)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Property not found\"}");
                return;
            }

            await WriteResponse(200, context.Response, response);
            return;
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
                return ExposedPropertySerializer.ToJson(exposedTypes);
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

            var json = ExposedPropertySerializer.ToJson(exposedEnums);

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
                await WriteResponse(400, context.Response, "{\"error\":\"Invalid request format\"}");
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
                await WriteResponse(400, context.Response, "{\"error\":\"Object not found\"}");
                return;
            }

            var body = await ReadRequestBody(context.Request);

            // 関数の検証とパラメータの準備、実行をすべてメインスレッドで行う
            var result = await ExecuteOnMainThread(() =>
            {
                // プロパティパスがある場合は、そのプロパティのオブジェクト上で関数を検索
                ExposedFunctionType function = null;
                object functionTarget = null;

                if (!string.IsNullOrEmpty(propertyPath))
                {
                    // Slash形式からDotBracket形式に変換してプロパティパスをたどる
                    var convertedPath = PropertyPath.FromSlash(propertyPath);
                    var property = exposedObject.FindProperty(convertedPath.Value);
                    if (property == null)
                    {
                        Debug.LogError($"[RemoteControl] Property '{propertyPath}' not found on object '{id}'");
                        return (success: false, invokeResult: (object)null);
                    }

                    var propertyValue = property.Value.GetValue();
                    if (propertyValue == null)
                    {
                        Debug.LogError($"[RemoteControl] Property value is null for path '{propertyPath}'");
                        return (success: false, invokeResult: (object)null);
                    }

                    // プロパティの値の型からExposedClassを取得
                    var propertyType = propertyValue.GetType();
                    var exposedClass = ExposedClass.Get(propertyType);
                    if (exposedClass == null)
                    {
                        Debug.LogError($"[RemoteControl] ExposedClass not found for type '{propertyType.Name}'");
                        return (success: false, invokeResult: (object)null);
                    }

                    // ExposedClassから関数を検索
                    function = exposedClass.FindFunction(functionName);
                    functionTarget = propertyValue;
                }
                else
                {
                    // 従来の動作：直接オブジェクトから関数を検索
                    function = exposedObject.GetFunction(functionName);
                    functionTarget = exposedObject.target;
                }

                if (function == null)
                {
                    return (success: false, invokeResult: (object)null);
                }

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

                var invokeResult = function.Invoke(functionTarget, args);
                return (success: true, invokeResult);
            });

            if (!result.success)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Function not found or failed to parse arguments\"}");
                return;
            }

            // 結果をJSON形式で返す
            var resultJson = new JObject();
            if (result.invokeResult != null)
            {
                resultJson["result"] = ExposedPropertySerializer.SerializeUnityType(_GetResolver(), result.invokeResult);
            }
            else
            {
                resultJson["result"] = JValue.CreateNull();
            }

            await WriteResponse(200, context.Response, JsonConvert.SerializeObject(resultJson));
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
                await WriteResponse(400, context.Response, "{\"error\":\"Invalid request format\"}");
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
                await WriteResponse(400, context.Response, "{\"error\":\"Invalid JSON body\"}");
                return;
            }

            var result = await ExecuteOnMainThread(() =>
            {
                var ok = ExposedObjectRegistry.SetParent(id, parentId, out var err);
                if (!ok) return (false, err, (JObject)null);

                var child = ExposedObjectRegistry.FindById(id);
                JObject value = null;
                if (child != null)
                {
                    value = ExposedPropertySerializer.SerializeFullToJObject(
                        child, _GetResolver());
                    _BroadcastParentChanged(id, value);
                }
                return (true, (string)null, value);
            });

            if (!result.Item1)
            {
                var errorJson = JsonConvert.SerializeObject(new { error = result.Item2 ?? "Unknown error" });
                await WriteResponse(400, context.Response, errorJson);
                return;
            }

            var responseJson = result.Item3 != null
                ? JsonConvert.SerializeObject(result.Item3, Formatting.None)
                : "{}";
            await WriteResponse(200, context.Response, responseJson);
        }

        /// <summary>
        /// @parent 変更を SSE で broadcast する。child 全体のフルシリアライズを送り、
        /// 受信側 (RemoteApp) はツリー表示を再構築する。
        /// </summary>
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



