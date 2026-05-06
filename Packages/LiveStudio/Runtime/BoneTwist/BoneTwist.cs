using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// ボーンのねじり補正を適用するMonoBehaviourコンポーネント
    /// FinalIK TwistRelaxerアルゴリズムに基づく
    /// 親ボーンと子ボーンの回転からねじりを緩和する
    /// </summary>
    public class BoneTwist : MonoBehaviour
    {
        [Header("Bone Hierarchy")]
        [SerializeField]
        [Tooltip("子ボーン（未指定の場合は自動検出）")]
        private Transform _childBone = null;

        [Header("Twist Settings")]
        [SerializeField]
        [Range(0.0f, 1.0f)]
        [Tooltip("ねじり補正の重み（0=補正なし、1=完全に補正）")]
        private float _weight = 1.0f;

        [SerializeField]
        [Range(0.0f, 1.0f)]
        [Tooltip("親と子のクロスフェード（0=親に追従、0.5=中間、1=子に追従）")]
        private float _parentChildCrossfade = 0.5f;

        [SerializeField]
        [Range(-180.0f, 180.0f)]
        [Tooltip("ねじり角度のオフセット（度）")]
        private float _twistAngleOffset = 0.0f;

        [Header("Advanced")]
        [SerializeField]
        [Tooltip("ねじり補正を有効にするか")]
        private bool _isEnabled = true;

        // 内部データ
        private BoneTwistData _twistData;
        private bool _isInitialized = false;
        private Quaternion _childRotationCache;

        /// <summary>
        /// 子ボーン
        /// </summary>
        public Transform childBone
        {
            get => _childBone;
            set
            {
                _childBone = value;
                _isInitialized = false;
            }
        }

        /// <summary>
        /// ねじり補正の重み
        /// </summary>
        public float weight
        {
            get => _weight;
            set
            {
                _weight = Mathf.Clamp01(value);
                if (_isInitialized)
                {
                    _twistData.weight = _weight;
                }
            }
        }

        /// <summary>
        /// 親子クロスフェード
        /// </summary>
        public float parentChildCrossfade
        {
            get => _parentChildCrossfade;
            set
            {
                _parentChildCrossfade = Mathf.Clamp01(value);
                if (_isInitialized)
                {
                    _twistData.parentChildCrossfade = _parentChildCrossfade;
                }
            }
        }

        /// <summary>
        /// ねじり角度オフセット
        /// </summary>
        public float twistAngleOffset
        {
            get => _twistAngleOffset;
            set
            {
                _twistAngleOffset = Mathf.Clamp(value, -180.0f, 180.0f);
                if (_isInitialized)
                {
                    _twistData.twistAngleOffset = _twistAngleOffset;
                }
            }
        }


        private void Start()
        {
            _Initialize();
        }

        private void OnValidate()
        {
            // エディタでパラメータ変更時に再初期化
            _isInitialized = false;
        }

        private void LateUpdate()
        {
            if (!_isInitialized)
            {
                _Initialize();
            }

            if (!_isInitialized)
            {
                return;
            }

            _Relax();
        }

        /// <summary>
        /// 初期化処理（FinalIK TwistSolver.Initiate()に相当）
        /// </summary>
        private void _Initialize()
        {
            // 親ボーンを取得（常にtransform.parent）
            Transform parentBone = transform.parent;
            if (parentBone == null)
            {
                Debug.LogWarning("[Studio] BoneTwist: Parent bone is not found", this);
                return;
            }

            // 子ボーンの自動検出
            if (_childBone == null)
            {
                if (transform.childCount > 0)
                {
                    _childBone = transform.GetChild(0);
                }
                else
                {
                    // 子がいない場合は親の他の子を探す
                    for (int i = 0; i < parentBone.childCount; i++)
                    {
                        Transform child = parentBone.GetChild(i);
                        if (child != transform)
                        {
                            _childBone = child;
                            break;
                        }
                    }
                }
            }

            if (_childBone == null)
            {
                Debug.LogWarning("[Studio] BoneTwist: Child bone is not found", this);
                return;
            }

            // BoneTwistSystemで初期化
            _twistData = BoneTwistSystem.Initiate(
                transform,
                parentBone,
                _childBone,
                _weight,
                _parentChildCrossfade,
                _twistAngleOffset
            );

            _isInitialized = _twistData.isInitialized;
        }

        /// <summary>
        /// ねじり補正を適用（FinalIK TwistSolver.Relax()に相当）
        /// </summary>
        private void _Relax()
        {
            if (Mathf.Approximately(_twistData.weight, 0.0f))
            {
                return;
            }

            // 親ボーンを取得（常にtransform.parent）
            Transform parentBone = transform.parent;
            if (parentBone == null)
            {
                return;
            }

            // 子の回転を保存（ねじり補正後に復元するため）
            _childRotationCache = _childBone.rotation;

            // ねじり補正を適用
            transform.rotation = BoneTwistSystem.Relax(
                in _twistData,
                transform.rotation,
                parentBone.rotation,
                _childBone.rotation,
                transform.position,
                parentBone.position,
                _childBone.position
            );

            // 子の回転を復元
            _childBone.rotation = _childRotationCache;
        }

        /// <summary>
        /// デフォルト回転に戻す（FinalIK TwistSolver.FixTransforms()に相当）
        /// </summary>
        public void FixTransform()
        {
            if (!_isInitialized)
            {
                return;
            }

            transform.localRotation = _twistData.targetInitialRotation;
            if (_childBone != null)
            {
                _childBone.localRotation = _twistData.childInitialRotation;
            }
        }

        /// <summary>
        /// 初期状態をリセット
        /// </summary>
        public void ResetInitialState()
        {
            _isInitialized = false;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // 親ボーンを取得（常にtransform.parent）
            Transform parentBone = transform.parent;

            // 子ボーンの自動検出（Gizmos表示用）
            Transform childBone = _childBone;
            if (childBone == null && transform.childCount > 0)
            {
                childBone = transform.GetChild(0);
            }

            if (parentBone != null)
            {
                // 親ボーンへのライン
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, parentBone.position);
            }

            if (childBone != null)
            {
                // 子ボーンへのライン
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(transform.position, childBone.position);
            }

            if (_isInitialized)
            {
                // ねじり軸を描画
                Gizmos.color = Color.cyan;
                Vector3 twistAxisWorld = transform.TransformDirection(_twistData.twistAxis);
                Gizmos.DrawRay(transform.position, twistAxisWorld * 0.1f);

                // 補助軸を描画
                Gizmos.color = Color.magenta;
                Vector3 axisWorld = transform.TransformDirection(_twistData.axis);
                Gizmos.DrawRay(transform.position, axisWorld * 0.1f);
            }
        }
#endif
    }
}
