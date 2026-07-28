// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Transform マニピュレーター編集時に使用する専用カメラのセッション情報。
    /// </summary>
    public class ManipulatorSession
    {
        public Guid id;
        public string objectId;
        public string propertyPath;
        public Camera camera;
        public RenderTexture renderTexture;
        public Texture2D readback;

        // orbit 視点状態（RemoteApp 側からの操作で更新）
        public float yaw;
        public float pitch;
        public float distance;
        public Vector3 pivot;

        public int width;
        public int height;

        /// <summary>
        /// Resolves the manipulator state for this session: the parent world TRS, the current edited local TRS,
        /// and a world pivot. Objects implementing <see cref="IManipulatorTarget"/> describe their own
        /// (possibly socket-relative) state; otherwise the object's own GameObject transform is edited.
        /// </summary>
        public bool TryGetManipulatorState(out TransformValue parentWorld, out TransformValue targetLocal, out Vector3 pivotWorld)
        {
            parentWorld = TransformValue.identity;
            targetLocal = TransformValue.identity;
            pivotWorld = Vector3.zero;
            if (!LiveObjectRegistry.TryFindById(objectId, out var exposed)) return false;

            // 1) Property-relative target: the edited property at propertyPath IS the manipulated TransformValue
            //    (e.g. PropAttachment.offset, reached as a nested path like "attachment/offset" on the owning
            //    component). A by-value nested object has no id of its own, so it is never the resolved
            //    `exposed.target`; resolve it through the property instead. The property's value is the actual
            //    edited value (so target == offset), and its owner (`obj`) supplies the socket-relative parent
            //    frame via IManipulatorTarget.
            if (!string.IsNullOrEmpty(propertyPath))
            {
                // propertyPath はクライアント由来のスラッシュ形式 ("components/0/attachment/offset")。
                // LiveObjectHandle.FindProperty は DotBracket 形式を期待するため、REST ハンドラと同じ
                // PropertyPath.FromSlash 変換をかける (これを忘れると配列添字を解決できず NULL → GameObject
                // フォールバックに落ちる)。
                var dotBracketPath = Lilium.RemoteControl.Reflection.PropertyPath.FromSlash(propertyPath).Value;
                var property = exposed.FindProperty(dotBracketPath);
                if (property.HasValue
                    && property.Value.obj is IManipulatorTarget mt
                    && property.Value.GetValue() is TransformValue offset)
                {
                    targetLocal = offset;
                    if (mt.TryGetManipulatorState(out var parentTrs, out _, out var pivot))
                    {
                        parentWorld = parentTrs;
                        pivotWorld = pivot;
                    }
                    else
                    {
                        pivotWorld = offset.position;
                    }
                    return true;
                }
            }

            // 2) Default: edit the object's own GameObject transform (local TRS relative to its hierarchy parent).
            //    Used by LiveLight / LiveUnityObject whose `transform` property is the GameObject's transform.
            var go = _ResolveGameObject(exposed);
            if (go == null) return false;
            var t = go.transform;
            targetLocal = TransformValue.FromTransform(t);
            parentWorld = t.parent != null
                ? new TransformValue(t.parent.position, t.parent.rotation, t.parent.lossyScale)
                : TransformValue.identity;
            pivotWorld = t.position;
            return true;
        }

        private static GameObject _ResolveGameObject(LiveObjectHandle exposed)
        {
            var t = exposed.target;
            if (t == null) return null;
            if (t is GameObject go) return go;
            if (t is Component c) return c.gameObject;
            if (t is LiveUnityObjectBase b)
            {
                if (b.reference is GameObject rgo) return rgo;
                if (b.reference is Component rc) return rc.gameObject;
            }
            return null;
        }
    }

    /// <summary>
    /// マニピュレーター編集用カメラのライフサイクル管理。
    /// Open でセッション作成・カメラ生成、Close で破棄。
    /// LiveCamera とは独立しており、画面には映らない専用 RenderTexture に描画する。
    /// </summary>
    public static class ManipulatorCameraService
    {
        private static readonly Dictionary<Guid, ManipulatorSession> _sessions
            = new Dictionary<Guid, ManipulatorSession>();

        private const int kWidth = 480;
        private const int kHeight = 270;
        private const float kInitialYaw = 45f;
        private const float kInitialPitch = 25f;
        private const float kDefaultDistance = 3f;
        private const float kFieldOfView = 35f;

        public static IReadOnlyDictionary<Guid, ManipulatorSession> sessions => _sessions;

        public static ManipulatorSession Open(string objectId, string propertyPath)
        {
            if (string.IsNullOrEmpty(objectId))
            {
                Debug.LogError("[Studio] ManipulatorCameraService.Open: objectId is null/empty.");
                return null;
            }

            var go = new GameObject($"__ManipulatorCamera_{objectId}")
            {
                hideFlags = HideFlags.DontSave
            };

            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.15f, 0.15f, 0.18f, 1f);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 100f;
            cam.fieldOfView = kFieldOfView;
            cam.enabled = false;

            var rt = new RenderTexture(kWidth, kHeight, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            var tex = new Texture2D(kWidth, kHeight, TextureFormat.RGB24, false);

            var session = new ManipulatorSession
            {
                id = Guid.NewGuid(),
                objectId = objectId,
                propertyPath = propertyPath,
                camera = cam,
                renderTexture = rt,
                readback = tex,
                yaw = kInitialYaw,
                pitch = kInitialPitch,
                distance = kDefaultDistance,
                width = kWidth,
                height = kHeight,
            };

            if (session.TryGetManipulatorState(out var parentWorld, out var targetLocal, out var pivotWorld))
            {
                session.pivot = pivotWorld;
                // World scale of the edited target = parent scale ∘ local scale (equals lossyScale in
                // GameObject mode), used to frame the camera distance to the target size.
                var lossy = Vector3.Scale(parentWorld.scale, targetLocal.scale);
                var maxScale = Mathf.Max(lossy.x, lossy.y, lossy.z);
                if (maxScale > 0f)
                {
                    session.distance = Mathf.Max(kDefaultDistance, maxScale * 4f);
                }
            }
            else
            {
                session.pivot = Vector3.zero;
            }

            _sessions[session.id] = session;
            _ApplyCameraPose(session);
            return session;
        }

        public static void Close(Guid id)
        {
            if (!_sessions.TryGetValue(id, out var s)) return;
            _sessions.Remove(id);

            if (s.camera != null)
            {
                s.camera.targetTexture = null;
                UnityEngine.Object.Destroy(s.camera.gameObject);
            }
            if (s.renderTexture != null)
            {
                s.renderTexture.Release();
                UnityEngine.Object.Destroy(s.renderTexture);
            }
            if (s.readback != null)
            {
                UnityEngine.Object.Destroy(s.readback);
            }
        }

        public static bool TryGet(Guid id, out ManipulatorSession session)
            => _sessions.TryGetValue(id, out session);

        public static void UpdatePose(Guid id, float? yaw, float? pitch, float? distance)
        {
            if (!_sessions.TryGetValue(id, out var s)) return;
            if (yaw.HasValue) s.yaw = yaw.Value;
            if (pitch.HasValue) s.pitch = Mathf.Clamp(pitch.Value, -89f, 89f);
            if (distance.HasValue) s.distance = Mathf.Max(0.1f, distance.Value);
            _ApplyCameraPose(s);
        }

        /// <summary>
        /// 画面ピクセル単位での pivot 平行移動 (Unity Scene View の中ボタンドラッグ相当)。
        /// 距離と FOV に比例させ、カーソル下のシーン点が指に追従する感覚にする。
        /// </summary>
        public static void Pan(Guid id, float panDx, float panDy)
        {
            if (!_sessions.TryGetValue(id, out var s)) return;
            if (s.camera == null || s.height <= 0) return;

            var fovRad = s.camera.fieldOfView * Mathf.Deg2Rad;
            var worldPerPixel = 2f * s.distance * Mathf.Tan(fovRad * 0.5f) / s.height;

            var camT = s.camera.transform;
            // dx が正 (カーソル右方向) のとき、scene が右に追従するよう pivot を左へ。
            // dy は画面下正としてクライアントから送られる。画面下 = world の -up 方向。
            var delta = -camT.right * (panDx * worldPerPixel) + camT.up * (panDy * worldPerPixel);
            s.pivot += delta;
            _ApplyCameraPose(s);
        }

        /// <summary>
        /// 現在の編集対象 Transform の位置をカメラの注視点 (pivot) に据え直す。
        /// yaw / pitch / distance は維持したまま、カメラ位置のみを再計算する。
        /// </summary>
        public static void Focus(Guid id)
        {
            if (!_sessions.TryGetValue(id, out var s)) return;
            if (s.TryGetManipulatorState(out _, out _, out var pivotWorld))
            {
                s.pivot = pivotWorld;
                _ApplyCameraPose(s);
            }
        }

        /// <summary>
        /// セッションのカメラを Render し、PNG バイトを返す。
        /// メインスレッドから呼び出すこと。
        /// pivot は Open / Focus 時に固定され、対象が動いてもカメラは追従しない。
        /// </summary>
        public static byte[] CaptureToPng(ManipulatorSession s)
        {
            if (s == null || s.camera == null) return null;

            _ApplyCameraPose(s);

            var prevActive = RenderTexture.active;
            s.camera.Render();
            RenderTexture.active = s.renderTexture;
            s.readback.ReadPixels(new Rect(0, 0, s.width, s.height), 0, 0);
            s.readback.Apply();
            RenderTexture.active = prevActive;
            return s.readback.EncodeToPNG();
        }

        /// <summary>
        /// ワールド空間のビュー行列（worldToCameraMatrix）。
        /// </summary>
        public static Matrix4x4 GetViewMatrix(ManipulatorSession s)
            => s?.camera != null ? s.camera.worldToCameraMatrix : Matrix4x4.identity;

        /// <summary>
        /// プロジェクション行列。RenderTexture への描画に使用されるものと同一。
        /// </summary>
        public static Matrix4x4 GetProjectionMatrix(ManipulatorSession s)
            => s?.camera != null ? s.camera.projectionMatrix : Matrix4x4.identity;

        /// <summary>
        /// 編集対象 Transform の親のワールド TRS。親がない場合は identity を返す。
        /// </summary>
        public static TransformValue GetParentWorldTransform(ManipulatorSession s)
        {
            if (s == null) return TransformValue.identity;
            if (!s.TryGetManipulatorState(out var parentWorld, out _, out _)) return TransformValue.identity;
            return parentWorld;
        }

        /// <summary>
        /// 編集対象の現在のローカル TRS。
        /// </summary>
        public static TransformValue GetTargetLocalTransform(ManipulatorSession s)
        {
            if (s == null) return TransformValue.identity;
            if (!s.TryGetManipulatorState(out _, out var targetLocal, out _)) return TransformValue.identity;
            return targetLocal;
        }

        private static void _ApplyCameraPose(ManipulatorSession s)
        {
            var rad = Mathf.Deg2Rad;
            var cy = Mathf.Cos(s.yaw * rad);
            var sy = Mathf.Sin(s.yaw * rad);
            var cp = Mathf.Cos(s.pitch * rad);
            var sp = Mathf.Sin(s.pitch * rad);
            var offset = new Vector3(cp * sy, sp, cp * cy) * s.distance;
            var camT = s.camera.transform;
            camT.position = s.pivot + offset;
            camT.LookAt(s.pivot, Vector3.up);
        }
    }
}
