// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;

using Lilium.RemoteControl;
using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;

namespace Lilium.RemoteControl.Scene
{
    /// <summary>
    /// シーンの import/export REST ハンドラ (/exposed/export, /exposed/import)。
    /// 旧 ExposedObjectHandler から分離。<see cref="ExposedSceneSerializer"/> に依存するため
    /// シーン読み書きモジュール側に置く。<see cref="RemoteControlBehaviour"/> が登録/解除する。
    /// </summary>
    public class SceneIoHandler : BaseRemoteControlApiHandler
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

        public SceneIoHandler(RemoteControlServerCore server) : base(server)
        {
        }

        private IExposedObjectResolver _GetResolver()
        {
            return GetObjectContainer() ?? (IExposedObjectResolver)DefaultExposedObjectResolver.Instance;
        }

        public override void Cleanup()
        {
        }

        public override bool CanHandle(HttpListenerRequest request)
        {
            var path = request.Url.AbsolutePath.ToLower();
            return path.Equals("/exposed/export") ||
                   path.Equals("/exposed/import");
        }

        protected override bool SupportsPost() => true;

        protected override async Task HandlePostRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;

            if (path.Equals("/exposed/export", System.StringComparison.OrdinalIgnoreCase))
            {
                await HandleExport(context);
                return;
            }
            if (path.Equals("/exposed/import", System.StringComparison.OrdinalIgnoreCase))
            {
                await HandleImport(context);
                return;
            }

            await WriteResponse(400, context.Response, "{\"error\":\"Invalid request format\"}");
        }

        private async Task HandleExport(HttpListenerContext context)
        {
            var body = await ReadRequestBody(context.Request);
            if (string.IsNullOrEmpty(body))
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Empty request body\"}");
                return;
            }

            var request = JsonConvert.DeserializeObject<ExportRequest>(body);
            if (string.IsNullOrEmpty(request.path))
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Path is required\"}");
                return;
            }

            var result = await ExecuteOnMainThread(() =>
            {
                var resolver = _GetResolver();

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

                var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), resolver);
                System.IO.File.WriteAllText(filePath, json);
                Debug.Log($"[RemoteControl] Exported to {filePath}");
                return true;
            });

            if (!result)
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Export failed\"}");
                return;
            }
            await WriteResponse(200, context.Response, "{\"success\":true}");
        }

        private async Task HandleImport(HttpListenerContext context)
        {
            var body = await ReadRequestBody(context.Request);
            if (string.IsNullOrEmpty(body))
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Empty request body\"}");
                return;
            }

            var request = JsonConvert.DeserializeObject<ImportRequest>(body);
            if (string.IsNullOrEmpty(request.path))
            {
                await WriteResponse(400, context.Response, "{\"error\":\"Path is required\"}");
                return;
            }

            var result = await ExecuteOnMainThread(() =>
            {
                var resolver = _GetResolver();

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
                ExposedSceneSerializer.SceneFromJson(json, resolver);
                Debug.Log($"[RemoteControl] Loaded from {filePath}");
                return (success: true, error: null as string);
            });

            if (!result.success)
            {
                await WriteResponse(400, context.Response, $"{{\"error\":\"{result.error}\"}}");
                return;
            }
            await WriteResponse(200, context.Response, "{\"success\":true}");
        }
    }
}
