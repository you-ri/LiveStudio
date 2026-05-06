using System;
using System.Net;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using UnityEngine;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.RestApi;
using Lilium.LiveStudio;
using Newtonsoft.Json;

namespace Lilium.LiveStudio
{
    [System.Serializable]
    public class CameraApiSettings
    {
        public System.Guid id;
        public string action; // "switch", "set_fov", "set_lookat", "start_stream", "stop_stream"
        public float fov;
        public string lookAt;
    }

    [System.Serializable]
    public class CameraApiInfo
    {
        public System.Guid id;
        public string name;
        public bool isLive;
        public float fov;
        public float aspect;
        public string lookAt;
    }

    [System.Serializable]
    public class CameraListResponse
    {
        public bool success;
        public CameraApiInfo[] cameras;
        public string timestamp;
    }

    [System.Serializable]
    public class CameraSwitchRequest
    {
        public string cameraId;
    }

    [System.Serializable]
    public class CameraControlRequest
    {
        public string type;
        public CameraApiSettings data;
    }

    [System.Serializable]
    public class CameraControlResponse
    {
        public bool success;
        public string message;
        public string timestamp;
    }

    public class CameraApiHandler : BaseRemoteControlApiHandler
    {
        private double _lastUpdateTime = 0f;
        private double _lastImageSendTime = 0f;
        private const float UPDATE_INTERVAL = 0.1f; // 100ms間隔でカメラ状態更新
        private const float IMAGE_SEND_INTERVAL = 0.066f; // 66ms間隔で画像送信（15fps）
        private string _lastActiveCameraName = string.Empty;
        private bool _isImageStreamingActive = false;
        
        public CameraApiHandler(RemoteControlServerCore server) : base(server)
        {
        }

        public override void Cleanup()
        {
        }

        // AnyThreadで呼び出される
        public void Update()
        {
            var timeNow = TimeUtility.GetTime();
            // 定期的にカメラ状態を更新
            if (timeNow - _lastUpdateTime >= UPDATE_INTERVAL)
            {
                _lastUpdateTime = timeNow;
                _ = CheckCameraSwitched();
            }

            // 定期的にカメラの画像を送信（ストリーミング有効時のみ）
            if (_isImageStreamingActive && timeNow - _lastImageSendTime >= IMAGE_SEND_INTERVAL)
            {
                _lastImageSendTime = timeNow;
                //_ = BroadcastCameraImages();
            }
        }
        
        private async Task CheckCameraSwitched()
        {
            var switchDetected = await ExecuteOnMainThread(() => {
                var cameras = CameraService.cameras;
                if (cameras == null)
                {
                    return false;
                }

                var activeCamera = cameras.FirstOrDefault(c => c.isLive);
                if (activeCamera != null && _lastActiveCameraName != activeCamera.displayName)
                {
                    _lastActiveCameraName = activeCamera.displayName;
                    return true;
                }
                
                return false;
            });
            
            if (switchDetected)
            {
                await BroadcastCameraUpdate();
            }
        }

        private async Task BroadcastCameraImages()
        {
            await ExecuteOnMainThread(() => {
                var cameras = CameraService.cameras;
                if (cameras == null)
                {
                    return;
                }
                
                foreach (var camera in cameras)
                {
                    if (camera.isLive && camera.image != null)
                    {
                        SendCameraImageSSE(camera);
                    }
                }

                foreach (var camera in cameras)
                {
                    camera.RequestCameraImage();
                }
            });
        }

        public override bool CanHandle(HttpListenerRequest request)
        {
            var path = request.Url.AbsolutePath;
            return path.Equals("/api/camera", StringComparison.OrdinalIgnoreCase) ||
                   path.Equals("/api/camera/image", StringComparison.OrdinalIgnoreCase) ||
                   path.Equals("/api/camera/switch", StringComparison.OrdinalIgnoreCase);
        }

        protected override bool SupportsGet() => true;
        protected override bool SupportsPost() => true;

        protected override async Task HandleGetRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;

            // Handle /api/camera/image endpoint (バイナリ直接送信)
            if (path.Equals("/api/camera/image", StringComparison.OrdinalIgnoreCase))
            {
                await HandleGetCameraImageRequest(context);
                return;
            }

            // Handle standard /api/camera endpoint
            var response = await ExecuteOnMainThread(() => new CameraListResponse
            {
                success = true,
                cameras = GetCurrentCameras(),
                timestamp = GetISOTimestamp()
            });

            var json = JsonConvert.SerializeObject(response);
            context.Response.StatusCode = 200;
            await WriteResponse(context.Response, json);
        }
        
        /// <summary>
        /// カメラ画像をバイナリ（PNG）で直接送信するエンドポイント
        /// </summary>
        private async Task HandleGetCameraImageRequest(HttpListenerContext context)
        {
            var imageBytes = await ExecuteOnMainThread<byte[]>(() => {
                var cameras = CameraService.cameras;
                if (cameras == null)
                {
                    return null;
                }

                // クエリパラメータからカメラIDを取得
                var cameraIdParam = context.Request.QueryString["camera"];
                IExposedCamera targetCamera = null;

                if (!string.IsNullOrEmpty(cameraIdParam) && Guid.TryParse(cameraIdParam, out Guid cameraId))
                {
                    targetCamera = cameras.FirstOrDefault(c => c.guid == cameraId);
                }
                else
                {
                    // カメラIDが指定されていない場合はアクティブカメラを使用
                    targetCamera = cameras.FirstOrDefault(c => c.isLive);
                }

                if (targetCamera == null)
                {
                    return null;
                }

                // カメラ画像をリクエスト
                targetCamera.RequestCameraImage();

                if (targetCamera.image == null)
                {
                    return null;
                }

                // カメラのアスペクト比を考慮してサムネイルサイズを計算
                int thumbnailWidth, thumbnailHeight;
                CameraUtility.CalculateThumbnailSize(targetCamera.aspect, out thumbnailWidth, out thumbnailHeight);

                // Texture2Dをリサイズ
                var thumbnailTexture = CameraUtility.ResizeTexture(targetCamera.image, thumbnailWidth, thumbnailHeight);

                byte[] bytes = thumbnailTexture.EncodeToPNG();

                // サムネイル用テクスチャを破棄
                if (thumbnailTexture != targetCamera.image)
                {
                    UnityEngine.Object.DestroyImmediate(thumbnailTexture);
                }

                return bytes;
            });

            if (imageBytes != null && imageBytes.Length > 0)
            {
                context.Response.ContentType = "image/png";
                context.Response.StatusCode = 200;
                context.Response.ContentLength64 = imageBytes.Length;
                await context.Response.OutputStream.WriteAsync(imageBytes, 0, imageBytes.Length);
                context.Response.Close();
            }
            else
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
            }
        }

        protected override async Task HandlePostRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;

            // /api/camera/switch エンドポイント
            if (path.Equals("/api/camera/switch", StringComparison.OrdinalIgnoreCase))
            {
                await HandleSwitchRequest(context);
                return;
            }

            // /api/camera エンドポイント
            var body = await ReadRequestBody(context.Request);

            if (string.IsNullOrEmpty(body))
            {
                context.Response.StatusCode = 400;
                await WriteResponse(context.Response, "{\"error\":\"Empty request body\"}");
                return;
            }

            var request = JsonConvert.DeserializeObject<CameraControlRequest>(body);

            if (request?.data != null)
            {
                var response = await ExecuteOnMainThread(() => {
                    var result = ExecuteCameraAction(request.data);

                    // SSEで他のクライアントに通知
                    _ = BroadcastCameraUpdate();

                    return result;
                });

                var json = JsonConvert.SerializeObject(response);
                context.Response.StatusCode = 200;
                await WriteResponse(context.Response, json);
            }
            else
            {
                context.Response.StatusCode = 400;
                await WriteResponse(context.Response, "{\"error\":\"Invalid request format\"}");
            }
        }

        private async Task HandleSwitchRequest(HttpListenerContext context)
        {
            var body = await ReadRequestBody(context.Request);

            if (string.IsNullOrEmpty(body))
            {
                context.Response.StatusCode = 400;
                await WriteResponse(context.Response, "{\"error\":\"Empty request body\"}");
                return;
            }

            var request = JsonConvert.DeserializeObject<CameraSwitchRequest>(body);

            if (request == null || string.IsNullOrEmpty(request.cameraId) || !Guid.TryParse(request.cameraId, out Guid cameraId))
            {
                context.Response.StatusCode = 400;
                await WriteResponse(context.Response, "{\"error\":\"Invalid or missing cameraId\"}");
                return;
            }

            var response = await ExecuteOnMainThread(() => {
                CameraService.SwitchCamera(cameraId);
                _ = BroadcastCameraUpdate();
                return new CameraControlResponse
                {
                    success = true,
                    message = "Camera switched successfully",
                    timestamp = GetISOTimestamp()
                };
            });

            var json = JsonConvert.SerializeObject(response);
            context.Response.StatusCode = 200;
            await WriteResponse(context.Response, json);
        }

        private CameraControlResponse ExecuteCameraAction(CameraApiSettings settings)
        {
            if (string.IsNullOrEmpty(settings.action))
            {
                return new CameraControlResponse
                {
                    success = false,
                    message = "Action is required",
                    timestamp = GetISOTimestamp()
                };
            }

            switch (settings.action.ToLower())
            {
                case "switch":
                    if (settings.id == System.Guid.Empty)
                    {
                        return new CameraControlResponse
                        {
                            success = false,
                            message = "Camera ID is required for switch action",
                            timestamp = GetISOTimestamp()
                        };
                    }
                    CameraService.SwitchCamera(settings.id);
                    break;


                case "start_stream":
                    _isImageStreamingActive = true;
                    break;

                case "stop_stream":
                    _isImageStreamingActive = false;
                    break;

                default:
                    Debug.LogWarning($"[Remote] Unknown camera action: {settings.action}");
                    return new CameraControlResponse
                    {
                        success = false,
                        message = $"Unknown action: {settings.action}",
                        timestamp = GetISOTimestamp()
                    };
            }

            return new CameraControlResponse
            {
                success = true,
                message = $"Camera action '{settings.action}' executed successfully",
                timestamp = GetISOTimestamp()
            };
        }

        private CameraApiInfo[] GetCurrentCameras()
        {
            var cameras = CameraService.cameras;
            if (cameras == null)
            {
                return new CameraApiInfo[0];
            }

            return cameras.Select(camera => new CameraApiInfo
            {
                id = camera.guid,
                name = camera.displayName,
                isLive = camera.isLive,
                aspect = camera.aspect,
            }).ToArray();
        }

        private async Task BroadcastCameraUpdate()
        {
            var updateData = await ExecuteOnMainThread(() => new
            {
                type = "camera_update",
                cameras = GetCurrentCameras(),
                timestamp = GetISOTimestamp()
            });
            
            await _server?.BroadcastMessage(updateData, "camera_update");
        }

        private async void SendCameraImageSSE(IExposedCamera activeCamera)
        {
            if (activeCamera?.image != null)
            {
                // カメラのアスペクト比を考慮してサムネイルサイズを計算
                int thumbnailWidth, thumbnailHeight;
                CameraUtility.CalculateThumbnailSize(activeCamera.aspect, out thumbnailWidth, out thumbnailHeight);

                // Texture2Dを小さなサムネイルサイズにリサイズしてからPNGエンコード
                var thumbnailTexture = CameraUtility.ResizeTexture(activeCamera.image, thumbnailWidth, thumbnailHeight);

                byte[] imageBytes = thumbnailTexture.EncodeToPNG();
                
                // SSE用データ構造
                var imageData = new
                {
                    type = "camera_image",
                    cameraName = activeCamera.displayName,
                    imageData = System.Convert.ToBase64String(imageBytes),
                    timestamp = GetISOTimestamp()
                };

                await _server?.BroadcastMessage(imageData, "camera_image");
                
                // サムネイル用テクスチャを破棄
                if (thumbnailTexture != activeCamera.image)
                {
                    UnityEngine.Object.DestroyImmediate(thumbnailTexture);
                }
            }
        }

        public void StartImageStreaming()
        {
            _isImageStreamingActive = true;
            Debug.Log("[Fusion] Camera image streaming started via API");
        }

        public void StopImageStreaming()
        {
            _isImageStreamingActive = false;
            Debug.Log("[Fusion] Camera image streaming stopped via API");
        }

        public bool IsImageStreamingActive()
        {
            return _isImageStreamingActive;
        }
    }
}