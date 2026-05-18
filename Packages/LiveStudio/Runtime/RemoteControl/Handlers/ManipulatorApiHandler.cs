// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Lilium.RemoteControl;
using Lilium.RemoteControl.RestApi;
using Lilium.RemoteControl.Server;

namespace Lilium.LiveStudio
{
    [System.Serializable]
    public class ManipulatorOpenRequest
    {
        public string objectId;
        public string propertyPath;
    }

    [System.Serializable]
    public class ManipulatorOpenResponse
    {
        public bool success;
        public string sessionId;
        public int width;
        public int height;
        public float aspect;
        public string message;
    }

    [System.Serializable]
    public class ManipulatorCameraPoseRequest
    {
        public float? yaw;
        public float? pitch;
        public float? distance;
        /// <summary>
        /// true のとき、現在の編集対象位置を注視点 (pivot) に再アンカーする。
        /// yaw/pitch/distance と併用可能。
        /// </summary>
        public bool? focus;
        /// <summary>
        /// 画面ピクセル単位での pivot 平行移動量 (Unity Scene View の中ボタンドラッグ相当)。
        /// 画面右方向に dx 正、画面下方向に dy 正。現在の距離と FOV から world 単位に換算する。
        /// </summary>
        public float? panDx;
        public float? panDy;
    }

    /// <summary>
    /// Transform マニピュレーター編集用カメラの REST API ハンドラ。
    /// - POST /api/manipulator/open           : セッション作成
    /// - GET  /api/manipulator/image?session= : 画像取得（PNG + ヘッダーに座標系メタ）
    /// - DELETE /api/manipulator/open?session=: セッション破棄
    /// - PUT  /api/manipulator/camera?session=: カメラ姿勢更新
    /// </summary>
    public class ManipulatorApiHandler : BaseRemoteControlApiHandler
    {
        public ManipulatorApiHandler(RemoteControlServerCore server) : base(server) { }

        public override void Cleanup()
        {
            // セッションの破棄は Close エンドポイント経由で行う。
            // サーバー停止時に残存セッションを解放する処理はここで実装してもよいが、
            // 現状はアプリ終了時に自動で破棄される想定。
        }

        private static readonly RouteRule[] _kRoutes =
        {
            new RouteRule("/api/manipulator/", RouteMatch.Prefix)
        };

        protected override IReadOnlyList<RouteRule> Routes => _kRoutes;

        protected override bool SupportsGet() => true;
        protected override bool SupportsPost() => true;
        protected override bool SupportsPut() => true;
        protected override bool SupportsDelete() => true;

        protected override Task HandleGetRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            if (path.Equals("/api/manipulator/image", StringComparison.OrdinalIgnoreCase))
            {
                return HandleGetImage(context);
            }
            return WriteError(context, 404, "Unknown manipulator GET path.");
        }

        protected override Task HandlePostRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            if (path.Equals("/api/manipulator/open", StringComparison.OrdinalIgnoreCase))
            {
                return HandlePostOpen(context);
            }
            return WriteError(context, 404, "Unknown manipulator POST path.");
        }

        protected override Task HandlePutRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            if (path.Equals("/api/manipulator/camera", StringComparison.OrdinalIgnoreCase))
            {
                return HandlePutCamera(context);
            }
            return WriteError(context, 404, "Unknown manipulator PUT path.");
        }

        protected override Task HandleDeleteRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            if (path.Equals("/api/manipulator/open", StringComparison.OrdinalIgnoreCase))
            {
                return HandleDeleteOpen(context);
            }
            return WriteError(context, 404, "Unknown manipulator DELETE path.");
        }

        private async Task HandlePostOpen(HttpListenerContext context)
        {
            var body = await ReadRequestBody(context.Request);
            if (string.IsNullOrEmpty(body))
            {
                await WriteError(context, 400, "Empty request body.");
                return;
            }

            ManipulatorOpenRequest req;
            try
            {
                req = JsonConvert.DeserializeObject<ManipulatorOpenRequest>(body);
            }
            catch (Exception ex)
            {
                await WriteError(context, 400, "Invalid JSON: " + ex.Message);
                return;
            }

            if (req == null || string.IsNullOrEmpty(req.objectId))
            {
                await WriteError(context, 400, "objectId is required.");
                return;
            }

            var session = await ExecuteOnMainThread(() => ManipulatorCameraService.Open(req.objectId, req.propertyPath));
            if (session == null)
            {
                await WriteError(context, 404, "Failed to open manipulator session (object not found or invalid).");
                return;
            }

            var aspect = session.height == 0 ? 1f : (float)session.width / session.height;
            var response = new ManipulatorOpenResponse
            {
                success = true,
                sessionId = session.id.ToString(),
                width = session.width,
                height = session.height,
                aspect = aspect,
            };
            context.Response.StatusCode = 200;
            await WriteResponse(context.Response, JsonConvert.SerializeObject(response));
        }

        private async Task HandleGetImage(HttpListenerContext context)
        {
            if (!_TryGetSession(context, out var session))
            {
                await WriteError(context, 404, "Session not found.");
                return;
            }

            var payload = await ExecuteOnMainThread(() => _BuildImagePayload(session));
            if (payload.imageBytes == null || payload.imageBytes.Length == 0)
            {
                await WriteError(context, 500, "Failed to capture image.");
                return;
            }

            var response = context.Response;
            response.ContentType = "image/png";
            response.StatusCode = 200;
            response.ContentLength64 = payload.imageBytes.Length;
            if (string.IsNullOrEmpty(response.Headers["Access-Control-Allow-Origin"]))
            {
                response.Headers.Add("Access-Control-Allow-Origin", "*");
            }
            response.Headers.Add("Access-Control-Expose-Headers", "X-Manipulator-State");
            response.Headers.Add("X-Manipulator-State", payload.stateBase64);

            await response.OutputStream.WriteAsync(payload.imageBytes, 0, payload.imageBytes.Length);
            response.Close();
        }

        private async Task HandleDeleteOpen(HttpListenerContext context)
        {
            if (!_TryGetSession(context, out var session))
            {
                // 冪等: 既に削除済みでも 204 で返す
                context.Response.StatusCode = 204;
                await WriteResponse(context.Response, string.Empty, "application/json");
                return;
            }

            await ExecuteOnMainThread(() => ManipulatorCameraService.Close(session.id));
            context.Response.StatusCode = 204;
            await WriteResponse(context.Response, string.Empty, "application/json");
        }

        private async Task HandlePutCamera(HttpListenerContext context)
        {
            if (!_TryGetSession(context, out var session))
            {
                await WriteError(context, 404, "Session not found.");
                return;
            }

            var body = await ReadRequestBody(context.Request);
            ManipulatorCameraPoseRequest req = null;
            try
            {
                if (!string.IsNullOrEmpty(body))
                    req = JsonConvert.DeserializeObject<ManipulatorCameraPoseRequest>(body);
            }
            catch (Exception ex)
            {
                await WriteError(context, 400, "Invalid JSON: " + ex.Message);
                return;
            }

            if (req == null)
            {
                await WriteError(context, 400, "Empty request body.");
                return;
            }

            await ExecuteOnMainThread(() =>
            {
                ManipulatorCameraService.UpdatePose(session.id, req.yaw, req.pitch, req.distance);
                if (req.panDx.HasValue || req.panDy.HasValue)
                {
                    ManipulatorCameraService.Pan(session.id, req.panDx ?? 0f, req.panDy ?? 0f);
                }
                if (req.focus == true)
                {
                    ManipulatorCameraService.Focus(session.id);
                }
            });
            context.Response.StatusCode = 204;
            await WriteResponse(context.Response, string.Empty, "application/json");
        }

        private struct ImagePayload
        {
            public byte[] imageBytes;
            /// <summary>
            /// base64(UTF-8(JSON)) で送る座標系メタ。HTTP ヘッダーに安全に載せるため base64 化。
            /// </summary>
            public string stateBase64;
        }

        private static ImagePayload _BuildImagePayload(ManipulatorSession session)
        {
            var view = ManipulatorCameraService.GetViewMatrix(session);
            var proj = ManipulatorCameraService.GetProjectionMatrix(session);
            var parent = ManipulatorCameraService.GetParentWorldTransform(session);
            var target = ManipulatorCameraService.GetTargetLocalTransform(session);
            var bytes = ManipulatorCameraService.CaptureToPng(session);

            var payloadObj = new
            {
                view = _MatrixToColumnMajorArray(view),
                projection = _MatrixToColumnMajorArray(proj),
                parent = _TransformValueToObject(parent),
                target = _TransformValueToObject(target),
                viewport = new { width = session.width, height = session.height },
            };
            var json = JsonConvert.SerializeObject(payloadObj);
            var base64 = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

            return new ImagePayload
            {
                imageBytes = bytes,
                stateBase64 = base64,
            };
        }

        private static float[] _MatrixToColumnMajorArray(Matrix4x4 m)
        {
            var a = new float[16];
            for (int c = 0; c < 4; c++)
            {
                for (int r = 0; r < 4; r++)
                {
                    a[c * 4 + r] = m[r, c];
                }
            }
            return a;
        }

        private static object _TransformValueToObject(TransformValue v)
        {
            return new
            {
                position = new { x = v.position.x, y = v.position.y, z = v.position.z },
                rotation = new { x = v.rotation.x, y = v.rotation.y, z = v.rotation.z, w = v.rotation.w },
                scale = new { x = v.scale.x, y = v.scale.y, z = v.scale.z },
            };
        }

        private static bool _TryGetSession(HttpListenerContext context, out ManipulatorSession session)
        {
            session = null;
            var idParam = context.Request.QueryString["session"];
            if (string.IsNullOrEmpty(idParam)) return false;
            if (!Guid.TryParse(idParam, out var id)) return false;
            return ManipulatorCameraService.TryGet(id, out session);
        }

    }
}
