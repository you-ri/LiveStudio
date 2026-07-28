// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;

using Lilium.RemoteControl;
using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;

namespace Lilium.RemoteControl.LiveScene
{
    /// <summary>
    /// シーンの import/export REST ハンドラ (/live/export, /live/import)。
    /// 旧 ExposedObjectHandler (現 LiveObjectHandler) から分離。<see cref="LiveSceneSerializer"/> に依存するため
    /// シーン読み書きモジュール側に置く。<see cref="RemoteControlBehaviour"/> が登録/解除する。
    /// </summary>
    public class LiveSceneIoHandler : BaseRemoteControlApiHandler
    {
        [System.Serializable]
        struct ExportRequest
        {
            public string path;
        }

        [System.Serializable]
        struct ImportRequest
        {
            public string path;
        }

        [System.Serializable]
        struct RemoveOrphanRequest
        {
            public string sourceKey;
        }

        private readonly EndpointRoute[] _postRoutes;
        private readonly EndpointRoute[] _getRoutes;

        public LiveSceneIoHandler(RemoteControlServerCore server)
            : base(server,
                new RouteRule("/live/export", RouteMatch.Exact),
                new RouteRule("/live/import", RouteMatch.Exact),
                new RouteRule("/live/orphans", RouteMatch.Exact),
                new RouteRule("/live/orphans/remove", RouteMatch.Exact))
        {
            _postRoutes = new[]
            {
                new EndpointRoute("/live/export", RouteMatch.Exact, HandleExport),
                new EndpointRoute("/live/import", RouteMatch.Exact, HandleImport),
                new EndpointRoute("/live/orphans/remove", RouteMatch.Exact, HandleRemoveOrphan),
            };
            _getRoutes = new[]
            {
                new EndpointRoute("/live/orphans", RouteMatch.Exact, HandleGetOrphans),
            };
        }

        protected override bool SupportsPost() => true;

        protected override Task HandlePostRequest(HttpListenerContext context)
            => DispatchEndpoints(context, _postRoutes, "Invalid request format");

        protected override bool SupportsGet() => true;

        protected override Task HandleGetRequest(HttpListenerContext context)
            => DispatchEndpoints(context, _getRoutes, "Unknown endpoint");

        private async Task HandleExport(HttpListenerContext context)
        {
            var body = await ReadRequestBody(context.Request);
            if (string.IsNullOrEmpty(body))
            {
                await WriteError(context, 400, "Empty request body");
                return;
            }

            var request = JsonConvert.DeserializeObject<ExportRequest>(body);
            if (string.IsNullOrEmpty(request.path))
            {
                await WriteError(context, 400, "Path is required");
                return;
            }

            var result = await ExecuteOnMainThread(() =>
            {
                var resolver = GetResolver();

                // 相対パスの場合は Saved フォルダ相対
                var filePath = request.path;
                if (!System.IO.Path.IsPathRooted(filePath))
                {
                    var projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
                    var savedFolder = System.IO.Path.Combine(projectRoot, "Saved");
                    if (!System.IO.Directory.Exists(savedFolder))
                    {
                        System.IO.Directory.CreateDirectory(savedFolder);
                    }
                    filePath = System.IO.Path.Combine(projectRoot, filePath);
                }

                // ディレクトリがなければ作成
                var directory = System.IO.Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), resolver);
                System.IO.File.WriteAllText(filePath, json);
                Debug.Log($"[RemoteControl] Exported to {filePath}");
                return true;
            });

            if (!result)
            {
                await WriteError(context, 400, "Export failed");
                return;
            }
            await WriteResponse(200, context.Response, "{\"success\":true}");
        }

        private async Task HandleImport(HttpListenerContext context)
        {
            var body = await ReadRequestBody(context.Request);
            if (string.IsNullOrEmpty(body))
            {
                await WriteError(context, 400, "Empty request body");
                return;
            }

            var request = JsonConvert.DeserializeObject<ImportRequest>(body);
            if (string.IsNullOrEmpty(request.path))
            {
                await WriteError(context, 400, "Path is required");
                return;
            }

            var result = await ExecuteOnMainThread(() =>
            {
                var resolver = GetResolver();

                // 相対パスの場合は Saved フォルダ相対
                var filePath = request.path;
                if (!System.IO.Path.IsPathRooted(filePath))
                {
                    var projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
                    var savedFolder = System.IO.Path.Combine(projectRoot, "Saved");
                    filePath = System.IO.Path.Combine(savedFolder, filePath);
                }

                if (!System.IO.File.Exists(filePath))
                {
                    return (success: false, error: $"File not found: {filePath}");
                }

                var json = System.IO.File.ReadAllText(filePath);
                LiveSceneSerializer.LiveSceneFromJson(json, resolver);
                Debug.Log($"[RemoteControl] Loaded from {filePath}");
                return (success: true, error: null as string);
            });

            if (!result.success)
            {
                // result.error は Windows パス(バックスラッシュ)を含むため、
                // 手組み補間ではなく WriteError で正しく JSON エスケープする。
                await WriteError(context, 400, result.error);
                return;
            }
            await WriteResponse(200, context.Response, "{\"success\":true}");
        }

        // GET /live/orphans — 現在のライブシーンが参照するが実体が存在しない (未解決の) ルート
        // オブジェクトの一覧を返す。RemoteApp のシーンページが "Missing" として提示する。
        private async Task HandleGetOrphans(HttpListenerContext context)
        {
            var orphans = await ExecuteOnMainThread(() =>
            {
                var list = LiveScenePendingStore.GetOrphans();
                var dto = new List<object>(list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    dto.Add(new { sourceKey = list[i].sourceKey, type = list[i].type });
                }
                return dto;
            });
            await WriteJson(context, new { orphans });
        }

        // POST /live/orphans/remove — 指定 sourceKey の孤児エントリを pending store から除去する。
        // 以降シーンを保存すればファイルから消える (明示削除で解決)。
        private async Task HandleRemoveOrphan(HttpListenerContext context)
        {
            var body = await ReadRequestBody(context.Request);
            if (string.IsNullOrEmpty(body))
            {
                await WriteError(context, 400, "Empty request body");
                return;
            }

            var request = JsonConvert.DeserializeObject<RemoveOrphanRequest>(body);
            if (string.IsNullOrEmpty(request.sourceKey))
            {
                await WriteError(context, 400, "sourceKey is required");
                return;
            }

            var removed = await ExecuteOnMainThread(() => LiveScenePendingStore.RemoveOrphan(request.sourceKey));
            await WriteResponse(200, context.Response, $"{{\"success\":true,\"removed\":{(removed ? "true" : "false")}}}");
        }
    }
}
