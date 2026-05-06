using System.Collections;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// UI要素（RectTransform）を時間経過で移動させるコンポーネント
    /// テロップ表示等に使用可能な汎用的な移動制御
    /// </summary>
    public class TelopController : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField]
        private RectTransform targetRectTransform;

        [Header("Movement Settings")]
        [SerializeField]
        private Vector3 startPosition = new Vector3(-1000, 0, 0);

        [SerializeField]
        private Vector3 endPosition = new Vector3(1000, 0, 0);

        [SerializeField]
        private float moveDuration = 3.0f;

        [SerializeField]
        private float waitDuration = 1.0f;

        [Header("Timing Control")]
        [SerializeField]
        private float initialDelay = 0f;

        [Header("Loop Settings")]
        [SerializeField]
        private bool enableLoop = true;

        [SerializeField]
        private bool pingPong = false; // 往復移動

        [SerializeField]
        private bool autoStart = true;

        [Header("Animation")]
        [SerializeField]
        private AnimationCurve easingCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Editor Helpers")]
        [SerializeField]
        private bool showPositionGizmos = true;

        // State
        private bool isMoving = false;
        private Coroutine movementCoroutine;
        private Vector3 originalPosition;
        private bool isReversed = false; // 往復移動用

        void Awake()
        {
            // targetRectTransformが設定されていない場合は自身のRectTransformを使用
            if (targetRectTransform == null)
            {
                targetRectTransform = GetComponent<RectTransform>();
            }

            if (targetRectTransform != null)
            {
                originalPosition = targetRectTransform.anchoredPosition3D;
            }
        }

        void OnEnable()
        {
            if (autoStart)
            {
                StartMovement();
            }
        }

        void Start()
        {
            if (autoStart)
            {
                StartMovement();
            }
        }

        /// <summary>
        /// 移動を開始
        /// </summary>
        public void StartMovement()
        {
            if (targetRectTransform == null)
            {
                Debug.LogError("[LiveStudio] TelopController: targetRectTransform is not assigned");
                return;
            }

            if (movementCoroutine != null)
            {
                StopCoroutine(movementCoroutine);
            }

            movementCoroutine = StartCoroutine(MovementCoroutine());
        }

        /// <summary>
        /// 移動を停止
        /// </summary>
        public void StopMovement()
        {
            if (movementCoroutine != null)
            {
                StopCoroutine(movementCoroutine);
                movementCoroutine = null;
            }
            isMoving = false;
        }

        /// <summary>
        /// 位置をリセット
        /// </summary>
        public void ResetPosition()
        {
            if (targetRectTransform != null)
            {
                targetRectTransform.anchoredPosition3D = originalPosition;
            }
            isReversed = false;
        }

        /// <summary>
        /// 開始位置に移動
        /// </summary>
        public void SetToStartPosition()
        {
            if (targetRectTransform != null)
            {
                Vector3 pos = pingPong && isReversed ? endPosition : startPosition;
                targetRectTransform.anchoredPosition3D = pos;
            }
        }

        /// <summary>
        /// 終了位置に移動
        /// </summary>
        public void SetToEndPosition()
        {
            if (targetRectTransform != null)
            {
                Vector3 pos = pingPong && isReversed ? startPosition : endPosition;
                targetRectTransform.anchoredPosition3D = pos;
            }
        }

        private IEnumerator MovementCoroutine()
        {
            // 初期遅延
            if (initialDelay > 0f)
            {
                yield return new WaitForSeconds(initialDelay);
            }

            while (true)
            {
                yield return StartCoroutine(SingleMovementCoroutine());

                if (!enableLoop)
                    break;

                if (pingPong)
                {
                    isReversed = !isReversed;
                }

                // 待機時間
                if (waitDuration > 0)
                {
                    yield return new WaitForSeconds(waitDuration);
                }
            }

            movementCoroutine = null;
        }

        private IEnumerator SingleMovementCoroutine()
        {
            if (targetRectTransform == null) yield break;

            isMoving = true;

            Vector3 fromPos = pingPong && isReversed ? endPosition : startPosition;
            Vector3 toPos = pingPong && isReversed ? startPosition : endPosition;

            // 開始位置に設定
            targetRectTransform.anchoredPosition3D = fromPos;

            float elapsed = 0f;

            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / moveDuration);

                // イージングカーブを適用
                float easedT = easingCurve.Evaluate(t);

                // 位置を補間
                Vector3 currentPos = Vector3.Lerp(fromPos, toPos, easedT);
                targetRectTransform.anchoredPosition3D = currentPos;

                yield return null;
            }

            // 最終位置に確実に設定
            targetRectTransform.anchoredPosition3D = toPos;
            isMoving = false;
        }

        #region Editor Helpers

#if UNITY_EDITOR
        /// <summary>
        /// エディタ用：現在の位置を開始位置として設定
        /// </summary>
        [ContextMenu("Set Current Position as Start")]
        private void SetCurrentAsStartPosition()
        {
            if (targetRectTransform != null)
            {
                startPosition = targetRectTransform.anchoredPosition3D;
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }

        /// <summary>
        /// エディタ用：現在の位置を終了位置として設定
        /// </summary>
        [ContextMenu("Set Current Position as End")]
        private void SetCurrentAsEndPosition()
        {
            if (targetRectTransform != null)
            {
                endPosition = targetRectTransform.anchoredPosition3D;
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }

        /// <summary>
        /// エディタ用：移動をプレビュー
        /// </summary>
        [ContextMenu("Preview Movement")]
        private void PreviewMovement()
        {
            if (Application.isPlaying)
            {
                StartMovement();
            }
            else
            {
                Debug.Log("[LiveStudio] TelopController: Preview is only available in Play Mode");
            }
        }

        void OnDrawGizmosSelected()
        {
            if (!showPositionGizmos || targetRectTransform == null) return;

            // Canvas取得
            Canvas canvas = targetRectTransform.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            // World座標に変換
            Vector3 worldStart = canvas.transform.TransformPoint(startPosition);
            Vector3 worldEnd = canvas.transform.TransformPoint(endPosition);

            // 開始位置（緑）
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(worldStart, 20f);
            Gizmos.DrawIcon(worldStart, "sv_icon_dot0_pix16_gizmo", false);

            // 終了位置（赤）
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(worldEnd, 20f);
            Gizmos.DrawIcon(worldEnd, "sv_icon_dot1_pix16_gizmo", false);

            // 移動パス
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(worldStart, worldEnd);

            // 現在位置（青）
            if (Application.isPlaying && targetRectTransform != null)
            {
                Vector3 worldCurrent = canvas.transform.TransformPoint(targetRectTransform.anchoredPosition3D);
                Gizmos.color = Color.blue;
                Gizmos.DrawWireSphere(worldCurrent, 15f);
            }
        }
#endif

        #endregion

        #region Properties

        public bool IsMoving => isMoving;
        public RectTransform TargetRectTransform => targetRectTransform;
        public Vector3 StartPosition => startPosition;
        public Vector3 EndPosition => endPosition;
        public float MoveDuration => moveDuration;
        public float InitialDelay => initialDelay;
        public bool EnableLoop => enableLoop;
        public bool PingPong => pingPong;

        #endregion

        void OnDestroy()
        {
            StopMovement();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // moveDurationの最小値チェック
            if (moveDuration < 0.1f)
                moveDuration = 0.1f;

            // waitDurationの最小値チェック
            if (waitDuration < 0f)
                waitDuration = 0f;

            // initialDelayの最小値チェック
            if (initialDelay < 0f)
                initialDelay = 0f;
        }
#endif
    }
}