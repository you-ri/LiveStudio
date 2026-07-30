using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
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


    public class LiveObjectHandler : BaseRemoteControlApiHandler
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

        public LiveObjectHandler(RemoteControlServerCore server) : base(server)
        {
            // 内側ディスパッチ表。順序は元の if/else 連鎖と同一に保つこと。
            _getRoutes = new[]
            {
                new EndpointRoute("/live/objects", RouteMatch.Exact, HandleGetObjects),
                new EndpointRoute("/live/object/*/*", RouteMatch.Wildcard, HandleGetProperty),
                new EndpointRoute("/live/object/", RouteMatch.Prefix, HandleGetObject),
                new EndpointRoute("/live/types", RouteMatch.Exact, HandleGetTypes),
                new EndpointRoute("/live/enums", RouteMatch.Exact, HandleGetEnums),
                new EndpointRoute("/live/changes", RouteMatch.Exact, HandleGetChanges),
            };
            _putRoutes = new[]
            {
                new EndpointRoute("/live/object/*/@parent", RouteMatch.Wildcard, HandleSetParent),
                new EndpointRoute("/live/object/*/*", RouteMatch.Wildcard, HandleSetProperty),
            };
            _postRoutes = new[]
            {
                new EndpointRoute("/live/batch", RouteMatch.Exact, HandleBatch),
                new EndpointRoute("/live/object/*/*/reset", RouteMatch.Wildcard, HandleResetProperty),
                new EndpointRoute("/live/object/*/*", RouteMatch.Wildcard, HandleAddArrayElement),
                new EndpointRoute("/live/function/*", RouteMatch.Wildcard, HandleInvokeFunction),
            };
            _deleteRoutes = new[]
            {
                new EndpointRoute("/live/object/*/*", RouteMatch.Wildcard, HandleRemoveArrayElement),
            };
            _patchRoutes = new[]
            {
                new EndpointRoute("/live/object/*/*", RouteMatch.Wildcard, HandleReorderArrayElement),
            };
        }

        public override void Cleanup()
        {
        }

        private static readonly RouteRule[] _kRoutes =
        {
            new RouteRule("/live/object/", RouteMatch.Prefix),
            new RouteRule("/live/function/", RouteMatch.Prefix),
            new RouteRule("/live/objects", RouteMatch.Prefix),
            new RouteRule("/live/types", RouteMatch.Prefix),
            new RouteRule("/live/enums", RouteMatch.Prefix),
            new RouteRule("/live/batch", RouteMatch.Exact),
            new RouteRule("/live/changes", RouteMatch.Exact),
        };

        protected override IReadOnlyList<RouteRule> Routes => _kRoutes;

        protected override bool SupportsGet() => true;

        protected override bool SupportsPut() => true;

        protected override bool SupportsPost() => true;

        protected override bool SupportsDelete() => true;

        protected override bool SupportsPatch() => true;

        // 各 HTTP メソッドの内側ディスパッチはコンストラクタで構築した宣言表に委譲する。
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
            var maxDepth = ResolveNestedMaxDepth(context.Request.QueryString);

            var liveObjects = await ExecuteOnMainThread(() => _CollectLiveObjects(typeName, category));
            var json = await ExecuteOnMainThread(() => LivePropertySerializer.ToJson(liveObjects, GetResolver(), maxDepth));
            await WriteResponse(200, context.Response, json);
        }

        /// <summary>The change-feed path, without query. Shared by the single and batch routes.</summary>
        private const string kChangesPath = "/live/changes";

        // Per-thread scratch for the changes response. The endpoint is polled continuously by every
        // connected remote app, so the steady state (nothing changed) must not allocate beyond the
        // response string itself. The single route runs on a worker thread and the batch route on the
        // main thread, so these are per-thread rather than shared.
        [ThreadStatic] private static List<string> _changeBuffer;
        [ThreadStatic] private static StringBuilder _changeJson;

        /// <summary>
        /// Reports which exposed objects changed since the client's last poll:
        /// <c>{"revision":57,"changes":["id", ...]}</c>.
        ///
        /// Without <c>since</c> the response carries an empty change list and the current revision —
        /// how a client syncs up on connect without being handed every id recorded so far.
        /// With <c>?since=N</c> it carries the ids recorded after revision N. Only ids travel; the
        /// client refetches the objects it actually holds, which is what keeps this cheap.
        /// </summary>
        private async Task HandleGetChanges(HttpListenerContext context)
        {
            var sinceRaw = context.Request.QueryString["since"];
            var hasSince = long.TryParse(sinceRaw, out var since);
            await WriteResponse(200, context.Response, _BuildChangesJson(hasSince, since));
        }

        /// <summary>
        /// Builds the change-feed response body. Unity-independent, so both the worker-thread single
        /// route and the main-thread batch route can call it.
        /// </summary>
        private static string _BuildChangesJson(bool hasSince, long since)
        {
            var buffer = _changeBuffer ?? (_changeBuffer = new List<string>(64));
            long revision;
            if (hasSince)
            {
                revision = LiveChangeLog.GetChangesSince(since, buffer);
            }
            else
            {
                buffer.Clear();
                revision = LiveChangeLog.revision;
            }

            var sb = _changeJson ?? (_changeJson = new StringBuilder(256));
            sb.Clear();
            sb.Append("{\"revision\":").Append(revision).Append(",\"changes\":[");
            for (int i = 0; i < buffer.Count; i++)
            {
                if (i > 0) sb.Append(',');
                // Ids come from the registry (type names, GUIDs, instance IDs) and never contain
                // characters needing escapes, but go through the encoder so a hand-registered id
                // cannot produce malformed JSON.
                sb.Append(JsonConvert.ToString(buffer[i]));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// True when a batch sub-request path targets the change feed (with or without a query).
        /// </summary>
        private static bool _IsChangesPath(string absolutePath)
        {
            if (absolutePath == null) return false;
            if (!absolutePath.StartsWith(kChangesPath, StringComparison.Ordinal)) return false;
            return absolutePath.Length == kChangesPath.Length
                || absolutePath[kChangesPath.Length] == '?';
        }

        /// <summary>
        /// Reads <c>?since=N</c> off a batch sub-request path. Shares the alloc-free scanner with
        /// the event inbox, which reads the same cursor parameter off its own sub-request.
        /// </summary>
        private static bool _TryParseChangesSince(string absolutePath, out long since)
            => EventInbox.TryParseSince(absolutePath, out since);

        /// <summary>
        /// Resolves the nested-expansion depth for a GET request from the <c>nested</c> query flag.
        /// Absent (default) yields a shallow depth-1 response where nested inline composites are emitted
        /// as truncation stubs so the payload stays small and scalable; the client fetches deeper levels
        /// on demand. <c>?nested</c>, <c>?nested=true</c> and <c>?nested=1</c> request the legacy
        /// unbounded expansion. A bare <c>?nested</c> (no '=') is stored by .NET under a null key, so
        /// both forms are checked. Only <c>/live/objects</c> and <c>/live/object/{id}</c> use this;
        /// property GET (<c>/live/object/{id}/{path}</c>) is always fully expanded by design.
        /// Takes the parsed query collection (not the request) so it is unit-testable without a live server.
        /// </summary>
        internal static int ResolveNestedMaxDepth(System.Collections.Specialized.NameValueCollection query)
        {
            return _IsNestedRequested(query) ? int.MaxValue : 1;
        }

        private static bool _IsNestedRequested(System.Collections.Specialized.NameValueCollection query)
        {
            if (query == null) return false;
            var value = query["nested"];
            if (value != null)
            {
                return value.Length == 0
                    || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || value == "1";
            }
            // Bare `?nested` — value-less keys land under the null key as a comma-joined list.
            var bareKeys = query[null];
            if (!string.IsNullOrEmpty(bareKeys))
            {
                foreach (var part in bareKeys.Split(','))
                {
                    if (part.Equals("nested", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// /live/objects の対象 LiveObjectHandle 集合を収集する。
        /// category 指定 > typeName 指定 > 全件 の優先順。Unity API を含むため
        /// メインスレッド上で呼ぶこと。
        /// </summary>
        private IEnumerable<LiveObjectHandle> _CollectLiveObjects(string typeName, string category)
            => CollectLiveObjects(GetObjectContainer(), typeName, category);

        /// <summary>
        /// Container-scoped collection used by <see cref="_CollectLiveObjects"/>. Live as
        /// <c>internal static</c> so tests can drive the type/category resolution with a hand-built
        /// container, without standing up a full server. Behavior is identical to the instance path.
        /// </summary>
        internal static IEnumerable<LiveObjectHandle> CollectLiveObjects(
            LiveObjectContainer container, string typeName, string category)
        {
            if (container == null)
            {
                return Enumerable.Empty<LiveObjectHandle>();
            }

            // カテゴリ指定
            if (!string.IsNullOrEmpty(category))
            {
                return LiveObjectRegistry.FindByCategory(category, container);
            }

            var instanceObjects = Enumerable.Empty<LiveObjectHandle>();

            // TypeName指定なし
            if (string.IsNullOrEmpty(typeName))
            {
                // Containerに登録されているオブジェクト（メイン + 他シーンのソースを合成）
                instanceObjects = container.EnumerateAllObjects()
                    .Where(obj => obj?.liveObject != null)
                    .Select(obj => obj.liveObject.Value);

                // Staticクラス
                var staticClasses = LiveClass.all.Values
                    .Where(t => t.isStatic)
                    .Select(t => LiveObjectRegistry.GetOrCreate(t.typeName, t, null));
                instanceObjects = instanceObjects.Concat(staticClasses);
            }
            // TypeName指定あり
            else
            {
                // TypeNameでフィルタリング（メイン + 他シーンのソースを合成）
                instanceObjects = container.EnumerateAllObjects()
                    .Where(obj => obj?.liveObject != null)
                    .Select(obj => obj.liveObject.Value)
                    .Where(obj => typeName == null || obj.targetTypeName == typeName);

                // Staticクラス名指定
                var staticClasses = LiveClass.all.Values
                    .Where(t => t.isStatic && (typeName == null || t.typeName == typeName))
                    .Select(t => LiveObjectRegistry.GetOrCreate(t.typeName, t, null));
                instanceObjects = instanceObjects.Concat(staticClasses);

                var liveClass = LiveClass.Find(typeName);

                // コンポーネント型名指定
                if (liveClass != null && liveClass.type.IsSubclassOf(typeof(Component)))
                {
                    var list = GameObject.FindObjectsByType(liveClass.type, FindObjectsInactive.Include, FindObjectsSortMode.None);
                    if (list != null && list.Length > 0)
                    {
                        // An explicit ?type=X query wants every X component surfaced as its own
                        // addressable handle, even when its GameObject is *also* exposed through a
                        // generic wrapper (e.g. an LiveGameObjectWithTransform transform handle).
                        // A component class such as AvatarController ("Avatar") is a distinct exposed
                        // identity from the GameObject wrapper that happens to share its GameObject,
                        // so it must not be filtered out by GameObject identity. Reusing an existing
                        // registered handle via FindByTarget still collapses genuine same-target dupes.
                        var foundObjects = list
                            .Select(v => LiveObjectRegistry.FindByTarget(v)
                                ?? LiveObjectHandle.CreateUnregistered(liveClass, v));
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
            var maxDepth = ResolveNestedMaxDepth(context.Request.QueryString);

            var liveObject = await ExecuteOnMainThread(() =>
            {
                var liveObject = FindLiveObjectById(id);

                return liveObject;
            });

            if (liveObject != null)
            {
                var json = await ExecuteOnMainThread(() => LivePropertySerializer.ToJson(liveObject.Value, GetResolver(), maxDepth: maxDepth));

                await WriteResponse(200, context.Response, json);
                return;
            }

            await WriteError(context, 404, "Object not found");
        }

        /// <summary>
        /// プロパティ系エンドポイント (/live/object/{id}/{slashPath}) の共通定型をまとめた
        /// パイプラインに渡すコンテキスト。メインスレッド上で <see cref="onProperty"/> に渡される。
        /// </summary>
        private readonly struct PropertyPipelineContext
        {
            public readonly LiveObjectHandle liveObject;
            public readonly string id;
            public readonly string slashPath;
            public readonly string propertyPath; // DotBracket 形式 (PropertyPath.Value)
            public readonly string body;         // readBody=false の場合は null

            public PropertyPipelineContext(LiveObjectHandle liveObject, string id,
                string slashPath, string propertyPath, string body)
            {
                this.liveObject = liveObject;
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
        /// /live/object/{id}/{slashPath} 系の共通定型:
        /// id/slashPath 解析 → LiveObjectHandle 解決 → (任意で body 読込) →
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
            Func<PropertyPipelineContext, ILiveObjectResolver, PropertyResult> onProperty)
        {
            var path = context.Request.Url.AbsolutePath;

            // body はワーカースレッドで先読みし、コンテキスト構築〜オペレーション適用を
            // 1 回のメインスレッドホップにまとめる。応答バイトは従来と一致する
            // (object 未解決時に InputStream を読むかどうかは応答内容に影響しない)。
            var body = readBody ? await ReadRequestBody(context.Request) : null;

            var result = await ExecuteOnMainThread(() =>
            {
                if (!TryBuildPropertyContext(GetObjectContainer(), path, stripResetSuffix, body,
                        out var ctx, out var errStatus, out var errMessage))
                {
                    return PropertyResult.Error(errStatus, errMessage);
                }
                return onProperty(ctx, GetResolver());
            });

            if (!result.ok)
            {
                await WriteError(context, result.errorStatus, result.errorMessage);
                return;
            }

            await WriteResponse(200, context.Response, result.body);
        }

        /// <summary>
        /// /live/object/{id}/{slashPath} 系の URL から <see cref="PropertyPipelineContext"/> を
        /// 構築する HTTP 非依存コア。RunPropertyPipeline (単発) と batch 内側ディスパッチで共用する。
        /// FindLiveObjectById が Unity API を含むためメインスレッド前提。
        /// エラー時は (400 "Invalid request format" / 404 "Object not found") を out で返す。
        /// </summary>
        private static bool TryBuildPropertyContext(
            LiveObjectContainer container, string absolutePath, bool stripResetSuffix, string body,
            out PropertyPipelineContext ctx, out int errStatus, out string errMessage)
        {
            ctx = default;
            errStatus = 0;
            errMessage = null;

            var path = absolutePath;
            if (stripResetSuffix && path.EndsWith("/reset"))
            {
                path = path.Substring(0, path.Length - "/reset".Length);
            }

            var id = PathParser.GetPathSegment(path, 2);
            var slashPath = PathParser.GetPathSegmentFrom(path, 3);

            if (id == null || slashPath == null)
            {
                errStatus = 400;
                errMessage = "Invalid request format";
                return false;
            }

            // Slash形式からDotBracket形式に変換
            var propertyPath = PropertyPath.FromSlash(slashPath);

            var liveObject = FindLiveObjectById(container, id);
            if (liveObject == null)
            {
                errStatus = 404;
                errMessage = "Object not found";
                return false;
            }

            ctx = new PropertyPipelineContext(
                liveObject.Value, id, slashPath, propertyPath.Value, body);
            return true;
        }

        private Task HandleGetProperty(HttpListenerContext context)
            => RunPropertyPipeline(context, readBody: false, stripResetSuffix: false, ApplyGetProperty);

        private Task HandleSetProperty(HttpListenerContext context)
            => RunPropertyPipeline(context, readBody: true, stripResetSuffix: false, ApplySetProperty);

        private Task HandleAddArrayElement(HttpListenerContext context)
            => RunPropertyPipeline(context, readBody: true, stripResetSuffix: false, ApplyAddArrayElement);

        private Task HandleRemoveArrayElement(HttpListenerContext context)
            => RunPropertyPipeline(context, readBody: true, stripResetSuffix: false, ApplyRemoveArrayElement);

        private Task HandleReorderArrayElement(HttpListenerContext context)
            => RunPropertyPipeline(context, readBody: true, stripResetSuffix: false, ApplyReorderArrayElement);

        private Task HandleResetProperty(HttpListenerContext context)
            // Reset は body を消費するが未使用(InputStream 消費タイミング維持のため readBody:true)。
            => RunPropertyPipeline(context, readBody: true, stripResetSuffix: true, ApplyResetProperty);

        // ---- オペレーション計算部 (HTTP 非依存)。単発エンドポイントと batch で共用する。 ----
        // 出力は従来の onProperty ラムダと厳密に一致させること (REST invariance)。

        private static PropertyResult ApplyGetProperty(PropertyPipelineContext ctx, ILiveObjectResolver resolver)
        {
            var property = ctx.liveObject.FindProperty(ctx.propertyPath);
            if (!property.HasValue)
            {
                return PropertyResult.Error(404, "Property not found");
            }

            var json = LivePropertySerializer.ToJson(property.Value, resolver);
            return PropertyResult.Success(json);
        }

        private static PropertyResult ApplySetProperty(PropertyPipelineContext ctx, ILiveObjectResolver resolver)
        {
            var property = ctx.liveObject.FindProperty(ctx.propertyPath);
            if (property == null)
            {
                return PropertyResult.Error(404, "Property not found");
            }

            var prop = property.Value;
            var result = LivePropertySerializer.FromJson(ctx.body, in prop);
            if (!result)
            {
                return PropertyResult.Error(400, "Failed to set property");
            }

            var json = LivePropertySerializer.ToJson(property.Value, resolver);

            // onPropertyChanged で親要素の他フィールドが書き換わる場合に備え、
            // 親が配列要素ならそのオブジェクトを変更ログに記録して他クライアントに再取得させる。
            _RecordArrayElementChange(ctx.id, ctx.slashPath);

            return PropertyResult.Success(json);
        }

        private static PropertyResult ApplyAddArrayElement(PropertyPipelineContext ctx, ILiveObjectResolver resolver)
        {
            var property = ctx.liveObject.FindProperty(ctx.propertyPath);
            if (property == null)
            {
                return PropertyResult.Error(404, "Property not found");
            }

            var prop = property.Value;
            return LivePropertySerializer.AddArrayElement(ctx.body, in prop)
                ? PropertyResult.Success("{}")
                : PropertyResult.Error(400, "Failed to add array element");
        }

        private static PropertyResult ApplyRemoveArrayElement(PropertyPipelineContext ctx, ILiveObjectResolver resolver)
        {
            var property = ctx.liveObject.FindProperty(ctx.propertyPath);
            if (property == null)
            {
                return PropertyResult.Error(404, "Property not found");
            }

            var prop = property.Value;
            return LivePropertySerializer.RemoveArrayElement(ctx.body, in prop)
                ? PropertyResult.Success("{}")
                : PropertyResult.Error(400, "Failed to remove array element");
        }

        private static PropertyResult ApplyReorderArrayElement(PropertyPipelineContext ctx, ILiveObjectResolver resolver)
        {
            var property = ctx.liveObject.FindProperty(ctx.propertyPath);
            if (property == null)
            {
                return PropertyResult.Error(404, "Property not found");
            }

            var prop = property.Value;
            return LivePropertySerializer.ReorderArrayElement(ctx.body, in prop)
                ? PropertyResult.Success("{}")
                : PropertyResult.Error(400, "Failed to reorder array element");
        }

        private static PropertyResult ApplyResetProperty(PropertyPipelineContext ctx, ILiveObjectResolver resolver)
        {
            var property = ctx.liveObject.FindProperty(ctx.propertyPath);
            if (property == null)
            {
                return PropertyResult.Error(404, "Property not found");
            }

            var prop = property.Value;
            LivePropertyUtility.ResetValue(ctx.liveObject, in prop);

            var newProperty = ctx.liveObject.FindProperty(ctx.propertyPath);
            var json = LivePropertySerializer.ToJson(newProperty.Value, resolver);
            return PropertyResult.Success(json);
        }


        // ---- Batch (POST /live/batch) ----
        // Epic Remote Control の /remote/batch 相当。複数サブリクエストを 1 リクエストで
        // 順次適用し、各結果を集約して返す。全件を 1 回のメインスレッドホップ内で適用するため
        // フレーム内で一貫する。各 item は独立 (continue-on-error)。

        /// <summary>
        /// POST /live/batch: { "requests": [ { id, method, path, body }, ... ] }
        /// → 200 { "responses": [ { id, status, body }, ... ] }
        /// </summary>
        private async Task HandleBatch(HttpListenerContext context)
        {
            var body = await ReadRequestBody(context.Request);

            // Pass 1: パース/抽出は Unity API 非依存なのでメインスレッドホップの外 (ワーカー) で行い、
            // 毎フレームのメインスレッド占有を減らす。不正 JSON は空リスト → 実行ゼロ (原子性)。
            var items = ExtractBatchItems(body);

            // 受信箱は「誰が聞いているか」を知る必要があり、LiveObjectContainer だけでは解けない。
            // Unity API にも触れないので、メインスレッドホップに持ち込まずここで先に解決する。
            var preResolved = _ResolveInboxItems(context.Request, items);

            // Pass 2: 実行 + 応答構築はメインスレッド (各オペレーションが Unity API を含む)。
            // 全件を 1 ホップで適用しフレーム内で一貫させる。
            var responseJson = await ExecuteOnMainThread(
                () => ExecuteBatchItems(GetObjectContainer(), GetResolver(), items, preResolved));
            await WriteResponse(200, context.Response, responseJson);
        }

        /// <summary>
        /// Resolves any inbox sub-requests (<c>GET /api/events</c>) up front, returning a body per
        /// item position and null for everything the batch executor handles itself. Returns null
        /// when the batch carries no inbox request, which is the case for every batch except the
        /// remote app's poll loop.
        /// </summary>
        private string[] _ResolveInboxItems(HttpListenerRequest request, List<BatchItem> items)
        {
            if (items == null) return null;

            string[] resolved = null;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!EventInbox.IsInboxPath(item.path)) continue;

                if (resolved == null) resolved = new string[items.Count];
                if (!string.Equals(item.method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    // 405 は executor 側に任せず、ここで空応答にはしない (未解決のまま通す)。
                    continue;
                }

                EventInbox.TryParseSince(item.path, out var since);
                resolved[i] = EventInbox.BuildJson(_context?.eventQueue, GetClientId(request), since);
            }
            return resolved;
        }

        // バッチ本文のプロパティ名 intern テーブル。既知キーを生成時に一度だけ登録し、以降は
        // 読み取り専用 (DefaultJsonNameTable.Get はロックフリー) なので並行バッチのワーカースレッドから
        // 安全に共有できる。⚠ 生成後に Add してはならない (Add は非スレッド安全)。
        // これにより、リクエスト件数 N に比例して確保していた繰り返しプロパティ名文字列 (~4N 本) を排除する。
        private static readonly DefaultJsonNameTable _kBatchNameTable = _CreateBatchNameTable();

        private static DefaultJsonNameTable _CreateBatchNameTable()
        {
            var table = new DefaultJsonNameTable();
            table.Add("requests");
            table.Add("id");
            table.Add("method");
            table.Add("path");
            table.Add("body");
            return table;
        }

        // batch サブリクエスト 1 件分の抽出結果。JObject ツリー (JObject/JProperty/内部辞書/ラッパ JValue)
        // を作らず、ストリーム読みで軽量に取り出すためのタプル。id は echo 用の JToken (欠落/null 可)。
        internal readonly struct BatchItem
        {
            public readonly JToken id;
            public readonly string method;
            public readonly string path;
            public readonly string body;

            public BatchItem(JToken id, string method, string path, string body)
            {
                this.id = id;
                this.method = method;
                this.path = path;
                this.body = body;
            }
        }

        /// <summary>
        /// batch 本文 (POST /live/batch) をストリーム解析し、各サブリクエストの id/method/path/body を
        /// 軽量タプルに取り出す (Pass 1)。JObject ツリーを作らないことでパース側の GC を削減する。
        /// Unity API を含まないためワーカースレッドで実行できる (メインスレッドホップの外で呼ぶ)。
        ///
        /// 挙動は従来の JObject.Parse + フィールド読みと厳密に一致させる:
        /// - 不正 JSON / 途中切断 / 末尾余剰は全体を棄却し空リストを返す (部分実行しない = 原子性)。
        /// - コメントトークンは JObject.Load 同様に読み飛ばす。
        /// - トップレベル requests の重複キーは last-wins (JObject 既定の Replace 準拠)。
        /// - フィールド順不同、未知フィールド無視、id の型は問わずそのまま echo。
        /// </summary>
        internal static List<BatchItem> ExtractBatchItems(string requestsBodyJson)
        {
            var items = new List<BatchItem>();
            if (string.IsNullOrWhiteSpace(requestsBodyJson)) return items;

            try
            {
                using (var reader = new JsonTextReader(new StringReader(requestsBodyJson))
                {
                    PropertyNameTable = _kBatchNameTable,
                })
                {
                    _ExtractBatchItems(reader, items);
                }
            }
            catch (JsonException)
            {
                // 不正 JSON: 途中まで抽出した分も含め全体を棄却する
                // (JObject.Parse 失敗時と同じく 1 件も実行しない)。
                items.Clear();
            }

            return items;
        }

        private static void _ExtractBatchItems(JsonTextReader reader, List<BatchItem> items)
        {
            // ルートオブジェクトへ。非オブジェクト root は JObject.Parse なら例外だが結果は空 responses で
            // 同じなので、ここでは空のまま抜ける (バイト列一致)。
            if (!_ReadSkipComments(reader)) return;
            if (reader.TokenType != JsonToken.StartObject) return;

            while (true)
            {
                _ReadOrThrowSkipComments(reader);
                if (reader.TokenType == JsonToken.EndObject) break;
                if (reader.TokenType != JsonToken.PropertyName)
                    throw new JsonReaderException("Unexpected token while reading batch object.");

                var name = (string)reader.Value; // name table により intern 済み
                _ReadOrThrowSkipComments(reader); // 値へ

                if (string.Equals(name, "requests", StringComparison.Ordinal)
                    && reader.TokenType == JsonToken.StartArray)
                {
                    items.Clear(); // 重複 requests キーは last-wins (JObject 既定 Replace と一致)
                    _ExtractRequestArray(reader, items);
                }
                else
                {
                    reader.Skip(); // requests 以外 / requests 非配列は無視 (JObject.Parse でも空 responses)
                }
            }

            _DrainToEnd(reader); // 末尾に余剰があれば reader が例外 → 呼び出し側で全体棄却
        }

        private static void _ExtractRequestArray(JsonTextReader reader, List<BatchItem> items)
        {
            // reader は StartArray 上。
            while (true)
            {
                _ReadOrThrowSkipComments(reader);
                if (reader.TokenType == JsonToken.EndArray) break;

                if (reader.TokenType == JsonToken.StartObject)
                {
                    JToken id = null;
                    string method = null, path = null, body = null;

                    while (true)
                    {
                        _ReadOrThrowSkipComments(reader);
                        if (reader.TokenType == JsonToken.EndObject) break;
                        if (reader.TokenType != JsonToken.PropertyName)
                            throw new JsonReaderException("Unexpected token while reading batch request.");

                        var field = (string)reader.Value;
                        _ReadOrThrowSkipComments(reader); // 値へ

                        if (string.Equals(field, "id", StringComparison.Ordinal))
                        {
                            id = JToken.ReadFrom(reader); // 型を問わず echo (現行 req["id"] と等価)
                        }
                        else if (string.Equals(field, "method", StringComparison.Ordinal))
                        {
                            method = _ReadFieldAsString(reader);
                        }
                        else if (string.Equals(field, "path", StringComparison.Ordinal))
                        {
                            path = _ReadFieldAsString(reader);
                        }
                        else if (string.Equals(field, "body", StringComparison.Ordinal))
                        {
                            body = reader.TokenType == JsonToken.Null
                                ? null
                                : JToken.ReadFrom(reader).ToString(Formatting.None);
                        }
                        else
                        {
                            reader.Skip(); // 未知フィールドは無視
                        }
                    }

                    items.Add(new BatchItem(id, method, path, body));
                }
                else
                {
                    // 非オブジェクト要素 (スカラー/配列/null): 従来の (reqToken as JObject)==null 相当。
                    // method/path が null になり Pass 2 で 400 "Invalid request format"、id は null echo。
                    reader.Skip();
                    items.Add(default);
                }
            }
        }

        /// <summary>
        /// method/path フィールドの値を、従来の <c>req["field"]?.ToString()</c> と同じ文字列に変換する。
        /// 文字列はそのまま (追加確保なし)、コンテナ (オブジェクト/配列) は JToken.ToString (= Indented) を
        /// 再現、その他 (数値/bool/null) は JValue.ToString() と等価な内部値の ToString。
        /// </summary>
        private static string _ReadFieldAsString(JsonTextReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonToken.String:
                    return (string)reader.Value;
                case JsonToken.StartObject:
                case JsonToken.StartArray:
                    // 従来 req["field"].ToString() は Formatting.Indented。稀な病的入力なので確保は許容し忠実再現。
                    return JToken.ReadFrom(reader).ToString();
                default:
                    // 数値/bool/null 等: JValue.ToString() は内部値の ToString() (CurrentCulture) と同一。
                    // reader.Value?.ToString() はこれと一致 (null は null → IsNullOrEmpty で 400、現行と同結果)。
                    return reader.Value?.ToString();
            }
        }

        // name table 付き reader で、コメントトークンを透過して次の意味あるトークンへ進む。
        // EOF (Read()==false) では false を返す。JObject.Load 既定 (CommentHandling.Ignore) と挙動を揃える。
        private static bool _ReadSkipComments(JsonTextReader reader)
        {
            while (reader.Read())
            {
                if (reader.TokenType != JsonToken.Comment) return true;
            }
            return false;
        }

        // 構造 (オブジェクト/配列/値) の途中で次のトークンが必須の箇所で使う。EOF は不完全 JSON として例外化する。
        // JObject.Load は途中切断を "Unexpected end of content" として例外化するため、生ループでもこれを再現する
        // (放置すると切断入力で無限ループや部分実行になる)。
        private static void _ReadOrThrowSkipComments(JsonTextReader reader)
        {
            if (!_ReadSkipComments(reader))
                throw new JsonReaderException("Unexpected end of content while reading batch.");
        }

        // ルートオブジェクト読了後に末尾を読み切る。余剰コンテンツがあれば reader が JsonException を投げ、
        // 呼び出し側が全体を棄却する (JObject.Parse の末尾チェックと一致)。EOF は正常。
        private static void _DrainToEnd(JsonReader reader)
        {
            while (reader.Read()) { }
        }

        /// <summary>
        /// 抽出済みの <see cref="BatchItem"/> 群を順に適用し { "responses": [...] } JSON を返す (Pass 2)。
        /// 各オペレーションが Unity API を含むためメインスレッド前提。各 item は独立 (continue-on-error)。
        /// <paramref name="preResolvedBodies"/> は「コンテナだけでは解けないためハンドラ側で先に
        /// 解決済みの応答本文」(受信箱など) を item 位置ごとに渡す。null 要素は通常どおり実行する。
        /// </summary>
        internal static string ExecuteBatchItems(
            LiveObjectContainer container, ILiveObjectResolver resolver, List<BatchItem> items,
            string[] preResolvedBodies = null)
        {
            var responses = new JArray();

            if (items != null)
            {
                for (int index = 0; index < items.Count; index++)
                {
                    var item = items[index];
                    var preResolved = preResolvedBodies != null && index < preResolvedBodies.Length
                        ? preResolvedBodies[index]
                        : null;

                    PropertyResult opResult;
                    if (preResolved != null)
                    {
                        opResult = PropertyResult.Success(preResolved);
                    }
                    else if (string.IsNullOrEmpty(item.method) || string.IsNullOrEmpty(item.path))
                    {
                        opResult = PropertyResult.Error(400, "Invalid request format");
                    }
                    else
                    {
                        try
                        {
                            opResult = ExecuteOperation(container, resolver, item.method, item.path, item.body);
                        }
                        catch (Exception ex)
                        {
                            // 1 件の例外でバッチ全体を巻き込まない (continue-on-error)。
                            Debug.LogError($"[RemoteControl] Batch operation failed ({item.method} {item.path}): {ex.Message}");
                            opResult = PropertyResult.Error(500, "Internal error");
                        }
                    }

                    responses.Add(_BuildBatchResponseItem(item.id, opResult));
                }
            }

            var resultRoot = new JObject { ["responses"] = responses };
            return LivePropertySerializer.SerializeToJson(resultRoot);
        }

        /// <summary>
        /// batch 本文を解釈し { "responses": [...] } を返す HTTP 非依存コア (パース+実行を同期実行)。
        /// サーバーを立てずにテスト可能。本番経路 (<see cref="HandleBatch"/>) は Pass 1
        /// (<see cref="ExtractBatchItems"/>) をワーカーで、Pass 2 (<see cref="ExecuteBatchItems"/>) を
        /// メインスレッドで分けて呼ぶ。
        /// </summary>
        internal static string ExecuteBatch(
            LiveObjectContainer container, ILiveObjectResolver resolver, string requestsBodyJson)
        {
            return ExecuteBatchItems(container, resolver, ExtractBatchItems(requestsBodyJson));
        }

        /// <summary>
        /// (method, path) を単発エンドポイントと同じパターンで内側ディスパッチし、対応する
        /// Apply* / InvokeFunctionCore を呼ぶ。対応するのは /live/object/{id}/{path} 系
        /// (GET/PUT/POST/DELETE/PATCH) と POST /live/function/{id}/{path}。
        /// それ以外のパスは 404、未対応メソッドは 405。
        /// </summary>
        private static PropertyResult ExecuteOperation(
            LiveObjectContainer container, ILiveObjectResolver resolver,
            string method, string absolutePath, string body)
        {
            // ToUpperInvariant で正規化文字列を確保する代わりに、比較時に大小無視で照合する
            // (バッチ経路で 1 オペレーションあたり 1 本の文字列確保を排除する)。
            var m = method ?? string.Empty;

            bool isPost = string.Equals(m, "POST", StringComparison.OrdinalIgnoreCase);

            // 受信箱。誰宛かはコンテナからは解けないため <see cref="HandleBatch"/> が先に解決する。
            // ここに来るのは GET 以外か、サーバーを介さない直接実行 (テスト) のときだけ。
            if (EventInbox.IsInboxPath(absolutePath))
            {
                return string.Equals(m, "GET", StringComparison.OrdinalIgnoreCase)
                    ? PropertyResult.Error(400, "Event inbox requires a client")
                    : PropertyResult.Error(405, "Method not allowed");
            }

            // 変更フィード。表示中プロパティの GET と同じ 1 往復に相乗りさせるため、単発ルートだけでなく
            // バッチからも引けるようにする（クライアントは毎サイクル必ずこれを 1 件載せる）。
            if (_IsChangesPath(absolutePath))
            {
                if (!string.Equals(m, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    return PropertyResult.Error(405, "Method not allowed");
                }
                var hasSince = _TryParseChangesSince(absolutePath, out var since);
                return PropertyResult.Success(_BuildChangesJson(hasSince, since));
            }

            // 関数呼び出し
            if (isPost && MatchPattern(absolutePath, "/live/function/*", RouteMatch.Wildcard))
            {
                var id = PathParser.GetPathSegment(absolutePath, 2);
                var functionPath = PathParser.GetPathSegmentFrom(absolutePath, 3);
                if (id == null || functionPath == null)
                {
                    return PropertyResult.Error(400, "Invalid request format");
                }
                return InvokeFunctionCore(container, resolver, id, functionPath, body);
            }

            // プロパティ系パイプライン。reset サフィックスは単発ルートと同じ優先順で先に判定する。
            var isReset = isPost
                && MatchPattern(absolutePath, "/live/object/*/*/reset", RouteMatch.Wildcard);
            if (isReset || MatchPattern(absolutePath, "/live/object/*/*", RouteMatch.Wildcard))
            {
                if (!TryBuildPropertyContext(container, absolutePath, isReset, body,
                        out var ctx, out var errStatus, out var errMessage))
                {
                    return PropertyResult.Error(errStatus, errMessage);
                }

                // 元の switch(uppercased) と同じ評価順・同じ判定を大小無視比較で再現する。
                if (string.Equals(m, "GET", StringComparison.OrdinalIgnoreCase)) return ApplyGetProperty(ctx, resolver);
                if (string.Equals(m, "PUT", StringComparison.OrdinalIgnoreCase)) return ApplySetProperty(ctx, resolver);
                if (isPost) return isReset ? ApplyResetProperty(ctx, resolver) : ApplyAddArrayElement(ctx, resolver);
                if (string.Equals(m, "DELETE", StringComparison.OrdinalIgnoreCase)) return ApplyRemoveArrayElement(ctx, resolver);
                if (string.Equals(m, "PATCH", StringComparison.OrdinalIgnoreCase)) return ApplyReorderArrayElement(ctx, resolver);
                return PropertyResult.Error(405, "Method not allowed");
            }

            return PropertyResult.Error(404, "Not found");
        }

        /// <summary>
        /// 1 サブリクエストの結果を { id, status, body } JObject に整形する。
        /// 成功時の body はオペレーションの応答 JSON をパースしたトークン、失敗時は {"error": ...}。
        /// </summary>
        private static JObject _BuildBatchResponseItem(JToken idToken, PropertyResult result)
        {
            int status;
            JToken bodyToken;
            if (result.ok)
            {
                status = 200;
                // 成功オペレーションの body は必ず Formatting.None のコンパクトな有効 JSON
                // (ToJson / "{}" / SerializeObject が生成)。JToken へ再パースせず JRaw で逐語挿入すると、
                // レスポンス全体をシリアライズした際のバイト列は従来 (parse→再シリアライズ) と同一のまま、
                // 1 オペレーションあたりの JToken ツリー複製を丸ごと省ける。
                bodyToken = string.IsNullOrEmpty(result.body)
                    ? JValue.CreateNull()
                    : (JToken)new JRaw(result.body);
            }
            else
            {
                status = result.errorStatus;
                bodyToken = new JObject { ["error"] = result.errorMessage };
            }

            return new JObject
            {
                ["id"] = idToken != null ? idToken.DeepClone() : JValue.CreateNull(),
                ["status"] = status,
                ["body"] = bodyToken,
            };
        }

        private async Task HandleGetTypes(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            var typeName = context.Request.QueryString["type"];

            // ObjectSelector のシーン列挙など、メインスレッド専用の Unity API を ToJson 内で呼ぶ可能性があるため、
            // types のシリアライズはメインスレッドで実行する。
            var json = await ExecuteOnMainThread(() =>
            {
                // ToJObject 中の派生型解決 (LiveClass.Find) が未登録の [LiveClass] 型を遅延登録して
                // LiveClass.all を変更しうるため、生の Dictionary ビューのまま列挙せず
                // スナップショットを取ってから直列化する (Collection was modified 対策)。
                var liveTypes = LiveClass.all.Values
                    .Where(t => t.typeName == typeName || typeName == null)
                    .ToList();
                return LiveTypeInfoSerializer.ToJson(liveTypes);
            });

            await WriteResponse(200, context.Response, json);
            return;
        }

        private async Task HandleGetEnums(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            var typeName = context.Request.QueryString["type"];

            var liveEnums = LiveEnum.all.Values
                .Where(e => e.typeName == typeName || typeName == null);

            var json = LiveTypeInfoSerializer.ToJson(liveEnums);

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

            var body = await ReadRequestBody(context.Request);

            // 関数の解決・パラメータ準備・実行・結果 JSON 化をすべてメインスレッドで行う
            var result = await ExecuteOnMainThread(
                () => InvokeFunctionCore(GetObjectContainer(), GetResolver(), id, functionPath, body));

            if (!result.ok)
            {
                await WriteError(context, result.errorStatus, result.errorMessage);
                return;
            }

            await WriteResponse(200, context.Response, result.body);
        }

        /// <summary>
        /// /live/function/{id}/{functionPath} の HTTP 非依存コア。単発エンドポイントと
        /// batch 内側ディスパッチで共用する。メインスレッド前提。成功時は {"result": ...} を
        /// <see cref="PropertyResult.Success"/> で返す。失敗は従来の文言で Error を返す
        /// (object 未解決=404 "Object not found"、関数未解決/引数失敗=400)。
        /// </summary>
        private static PropertyResult InvokeFunctionCore(
            LiveObjectContainer container, ILiveObjectResolver resolver,
            string id, string functionPath, string body)
        {
            // 最後の/で分割してプロパティパスと関数名に分離
            string propertyPath = null;
            string functionName = functionPath;
            var lastSlashIndex = functionPath.LastIndexOf('/');
            if (lastSlashIndex >= 0)
            {
                propertyPath = functionPath.Substring(0, lastSlashIndex);
                functionName = functionPath.Substring(lastSlashIndex + 1);
            }

            var liveObject = FindLiveObjectById(container, id);
            if (liveObject == null)
            {
                return PropertyResult.Error(404, "Object not found");
            }

            var function = _ResolveInvokeFunction(
                liveObject.Value, propertyPath, functionName, id, out var functionTarget);
            if (function == null)
            {
                return PropertyResult.Error(400, "Function not found or failed to parse arguments");
            }

            var args = _BuildInvokeArguments(function, body);
            var invokeResult = function.Invoke(functionTarget, args);

            // 結果をJSON形式で返す
            var resultJson = new JObject();
            resultJson["result"] = invokeResult != null
                ? LivePropertySerializer.SerializeUnityType(resolver, invokeResult)
                : JValue.CreateNull();

            return PropertyResult.Success(JsonConvert.SerializeObject(resultJson));
        }

        /// <summary>
        /// 呼び出す関数とその実行対象を解決する。propertyPath があればその
        /// プロパティ値の型から、無ければ liveObject 直接から関数を検索する。
        /// 解決できなければ null(理由は Debug.LogError で出力)。メインスレッド前提。
        /// </summary>
        private static LiveFunctionType _ResolveInvokeFunction(
            LiveObjectHandle liveObject, string propertyPath, string functionName,
            string id, out object functionTarget)
        {
            functionTarget = null;

            if (!string.IsNullOrEmpty(propertyPath))
            {
                // Slash形式からDotBracket形式に変換してプロパティパスをたどる
                var convertedPath = PropertyPath.FromSlash(propertyPath);
                var property = liveObject.FindProperty(convertedPath.Value);
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

                // プロパティの値の型からLiveClassを取得
                var propertyType = propertyValue.GetType();
                var liveClass = LiveClass.Get(propertyType);
                if (liveClass == null)
                {
                    Debug.LogError($"[RemoteControl] LiveClass not found for type '{propertyType.Name}'");
                    return null;
                }

                // LiveClassから関数を検索
                functionTarget = propertyValue;
                return liveClass.FindFunction(functionName);
            }

            // 従来の動作：直接オブジェクトから関数を検索
            functionTarget = liveObject.target;
            return liveObject.GetFunction(functionName);
        }

        /// <summary>
        /// リクエストボディ JSON の "args" 配列から関数引数を構築する。
        /// パラメータ個数ベースで確保し、未指定/null は HasDefaultValue があれば
        /// 既定値、無ければ型の default を使う。body 空 or args 無しなら null。
        /// 引数構築の実体は <see cref="LivePropertySerializer.BuildInvokeArguments"/> に集約し、
        /// 保存済みオペレーション引数 (InvokeFunctionOperation) と共有する。
        /// </summary>
        private static object[] _BuildInvokeArguments(LiveFunctionType function, string body)
        {
            if (string.IsNullOrEmpty(body)) return null;

            var jObject = JsonConvert.DeserializeObject<JObject>(body);
            var argsToken = jObject["args"] as JArray;
            if (argsToken == null) return null;

            return LivePropertySerializer.BuildInvokeArguments(
                function, argsToken, DefaultLiveObjectResolver.Instance);
        }

        private LiveObjectHandle? FindLiveObjectById(string id)
            => FindLiveObjectById(GetObjectContainer(), id);

        /// <summary>
        /// id から <see cref="LiveObjectHandle"/> を解決する HTTP 非依存コア。
        /// container を明示的に受け取るため、サーバーを立てずに batch 等から再利用・テストできる。
        /// Unity API (FindObjectsByType 等) を含むためメインスレッド前提。
        /// </summary>
        private static LiveObjectHandle? FindLiveObjectById(LiveObjectContainer container, string id)
        {
            // Container に登録されているオブジェクトで検索（propertyName + グローバル _byId フォールバック）
            if (container != null)
            {
                var liveObject = container.FindById(id);
                if (liveObject != null)
                {
                    return liveObject;
                }
            }

            // staticクラス名で検索 (target=null で生成できるのは static class のみ。
            // MonoBehaviour 等の非 static 型を null target で生成すると LiveObjectHandle.cs:47 の警告が出て、
            // 以降の SetValue などが target=null に対して走り壊れる)
            var liveType = LiveClass.Find(id);
            if (liveType != null && liveType.isStatic)
            {
                var liveObject = LiveObjectRegistry.GetOrCreate(liveType.typeName, liveType, null);
                return liveObject;
            }

            // instance id で検索 (typeName を id として割り当てた MonoBehaviour 等もここでヒットする)
            if (LiveObjectRegistry.TryFindById(id, out var instanceObject))
            {
                return instanceObject;
            }

            // 非 static の LiveClass で、シーン上のインスタンスを target として登録を試みる。
            // LivePropertyRef.Resolve と同じ救済ロジック。
            if (liveType != null
                && liveType.type != null
                && typeof(Component).IsAssignableFrom(liveType.type))
            {
                var sceneTarget = UnityEngine.Object.FindFirstObjectByType(liveType.type, FindObjectsInactive.Include);
                if (sceneTarget != null)
                {
                    return LiveObjectRegistry.GetOrCreate(liveType.typeName, liveType, sceneTarget);
                }
            }

            // 最終フォールバック: 数値 instanceId として Unity の内部 API から逆引きして
            // 未登録の UnityEngine.Object を一時的な LiveObjectHandle にラップする（レジストリ登録しない）
            if (long.TryParse(id, out var unityInstanceId))
            {
                var unityObj = LiveObjectUtility.InstanceIDToObject(unityInstanceId);
                if (unityObj != null)
                {
                    var liveClass = LiveClass.Find(unityObj.GetType());
                    if (liveClass != null)
                    {
                        return LiveObjectHandle.CreateUnregistered(liveClass, unityObj);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// PUT /live/object/{id}/@parent のリクエストボディ。
        /// </summary>
        [System.Serializable]
        struct SetParentRequest
        {
            public string parentId;
        }

        /// <summary>
        /// PUT /live/object/{id}/@parent: LiveObjectHandle 同士の親子関係を変更する。
        /// body: { "parentId": "...id..." | null }
        /// 成功時は child 全体のフルシリアライズを返しつつ、変更ログに child を記録して
        /// 他クライアントに再取得させる。
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
                var ok = LiveObjectRegistry.SetParent(id, parentId, out var err);
                if (!ok) return (ok: false, error: err, value: (JObject)null);

                var child = LiveObjectRegistry.FindById(id);
                JObject value = null;
                if (child != null)
                {
                    value = LivePropertySerializer.SerializeFullToJObject(
                        child.Value, GetResolver());
                    _RecordParentChanged(id);
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
        /// Publishes a @parent change so other clients rebuild their tree. Only the child's id is
        /// recorded; whoever holds that object refetches it and reads the new parent from it.
        /// </summary>
        private static void _RecordParentChanged(string childId)
        {
            LiveChangeLog.Record(childId);
        }

        /// <summary>
        /// Publishes that a leaf inside an array element changed, so dependent fields rewritten through
        /// onPropertyChanged (ShowIf targets and the like) reach other clients. Writes to a leaf outside
        /// an array need no record: the client that issued the PUT already knows, and any other client
        /// displaying that property reads it back through the live property poll.
        /// </summary>
        private static void _RecordArrayElementChange(string requestId, string slashPath)
        {
            if (string.IsNullOrEmpty(slashPath)) return;

            var parts = slashPath.Split('/');
            if (parts.Length < 2) return;

            // 葉の一つ上のセグメントが数値なら、親は配列要素。
            if (!int.TryParse(parts[parts.Length - 2], out _)) return;

            LiveChangeLog.Record(requestId);
        }

    }
}



