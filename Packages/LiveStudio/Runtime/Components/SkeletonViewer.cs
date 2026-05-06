using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// スケルトン階層構造をGizmoで可視化するコンポーネント
    /// GameObjectの子階層を辿ってラインを描画
    /// </summary>
    [DefaultExecutionOrder(30300)]
    public class SkeletonViewer : MonoBehaviour
    {
        [Header("Gizmo Settings")]
        [Tooltip("Gizmoのライン色")]
        [SerializeField]
        private Color _lineColor = Color.green;

        [Tooltip("Gizmoのライン幅")]
        [SerializeField]
        private float _lineWidth = 2f;

        [Tooltip("ボーン位置の球サイズ")]
        [SerializeField]
        private float _sphereSize = 0.01f;

        [Tooltip("常にGizmoを描画するか（falseの場合は選択時のみ）")]
        [SerializeField]
        private bool _alwaysDraw = true;

        public Color lineColor
        {
            get => _lineColor;
            set => _lineColor = value;
        }

        public float lineWidth
        {
            get => _lineWidth;
            set => _lineWidth = value;
        }

        public float sphereSize
        {
            get => _sphereSize;
            set => _sphereSize = value;
        }

        public bool alwaysDraw
        {
            get => _alwaysDraw;
            set => _alwaysDraw = value;
        }

        /// <summary>
        /// Gizmo描画（選択時のみ）
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (!_alwaysDraw && isActiveAndEnabled)
            {
                _DrawSkeletonGizmos();
            }
        }

        /// <summary>
        /// Gizmo描画（常時）
        /// </summary>
        private void OnDrawGizmos()
        {
            if (_alwaysDraw && isActiveAndEnabled)
            {
                _DrawSkeletonGizmos();
            }
        }

        /// <summary>
        /// スケルトン構造を描画
        /// </summary>
        private void _DrawSkeletonGizmos()
        {
            Gizmos.color = _lineColor;

            // ルートから再帰的に描画
            _DrawBoneRecursive(transform);
        }

        /// <summary>
        /// ボーンを再帰的に描画
        /// </summary>
        /// <param name="bone">描画するボーンのTransform</param>
        private void _DrawBoneRecursive(Transform bone)
        {
            if (bone == null)
            {
                return;
            }

            // ボーン位置に球を描画
            Gizmos.DrawWireSphere(bone.position, _sphereSize);

            // 各子ボーンへのラインを描画
            for (int i = 0; i < bone.childCount; i++)
            {
                Transform child = bone.GetChild(i);

                // 親から子へのラインを描画
                Gizmos.DrawLine(bone.position, child.position);

                // 子を再帰的に描画
                _DrawBoneRecursive(child);
            }
        }
    }
}
