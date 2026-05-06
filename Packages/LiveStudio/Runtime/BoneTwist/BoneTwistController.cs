using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// ボーンのねじり補正設定（Serializable）
    /// </summary>
    [System.Serializable]
    public class BoneTwistSolver
    {
        [Tooltip("ねじり補正対象のボーン")]
        public Transform targetBone;

        [Tooltip("子ボーン（未指定の場合は自動検出）")]
        public Transform childBone;

        [Range(0.0f, 1.0f)]
        [Tooltip("ねじり補正の重み（0=補正なし、1=完全に補正）")]
        public float weight = 1.0f;

        [Range(0.0f, 1.0f)]
        [Tooltip("親と子のクロスフェード（0=親に追従、0.5=中間、1=子に追従）")]
        public float parentChildCrossfade = 0.5f;

        [Range(-180.0f, 180.0f)]
        [Tooltip("ねじり角度のオフセット（度）")]
        public float twistAngleOffset = 0.0f;

        // 内部データ
        [System.NonSerialized]
        public BoneTwistData twistData;

        [System.NonSerialized]
        public bool isInitialized = false;

        [System.NonSerialized]
        public Quaternion childRotationCache;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public BoneTwistSolver()
        {
            weight = 1.0f;
            parentChildCrossfade = 0.5f;
            twistAngleOffset = 0.0f;
        }

        /// <summary>
        /// コンストラクタ（ターゲットボーン指定）
        /// </summary>
        public BoneTwistSolver(Transform target)
        {
            targetBone = target;
            weight = 1.0f;
            parentChildCrossfade = 0.5f;
            twistAngleOffset = 0.0f;
        }
    }

    /// <summary>
    /// 複数のボーンに対してねじり補正を一括制御するコンポーネント
    /// FinalIK TwistRelaxerに相当
    /// </summary>
    public class BoneTwistController : MonoBehaviour
    {
        [Header("Bone Twist Solvers")]
        [Tooltip("ねじり補正するボーンのリスト（階層の逆順で追加推奨：手首→前腕→上腕）")]
        [SerializeField]
        private BoneTwistSolver[] _twistSolvers = new BoneTwistSolver[0];

        /// <summary>
        /// ソルバー配列
        /// </summary>
        public BoneTwistSolver[] twistSolvers
        {
            get => _twistSolvers;
            set
            {
                _twistSolvers = value;
                _InitializeAll();
            }
        }


        private void Start()
        {
            _InitializeAll();
        }

        private void LateUpdate()
        {

            // すべてのソルバーを処理
            foreach (var solver in _twistSolvers)
            {
                if (solver == null || solver.targetBone == null)
                {
                    continue;
                }

                if (!solver.isInitialized)
                {
                    _InitializeSolver(solver);
                }

                if (solver.isInitialized)
                {
                    _RelaxSolver(solver);
                }
            }
        }

        /// <summary>
        /// すべてのソルバーを初期化
        /// </summary>
        private void _InitializeAll()
        {
            if (_twistSolvers == null || _twistSolvers.Length == 0)
            {
                Debug.LogWarning("[Studio] BoneTwistController: No twist solvers assigned", this);
                return;
            }

            foreach (var solver in _twistSolvers)
            {
                if (solver != null && solver.targetBone != null)
                {
                    _InitializeSolver(solver);
                }
            }
        }

        /// <summary>
        /// 個別ソルバーを初期化
        /// </summary>
        private void _InitializeSolver(BoneTwistSolver solver)
        {
            if (solver.targetBone == null)
            {
                Debug.LogWarning("[Studio] BoneTwistController: Target bone is not assigned", this);
                return;
            }

            // 親ボーンを取得（常にtarget.parent）
            Transform parentBone = solver.targetBone.parent;
            if (parentBone == null)
            {
                Debug.LogWarning($"[Studio] BoneTwistController: Parent bone is not found for {solver.targetBone.name}", this);
                return;
            }

            // 子ボーンの自動検出
            if (solver.childBone == null)
            {
                if (solver.targetBone.childCount > 0)
                {
                    solver.childBone = solver.targetBone.GetChild(0);
                }
                else
                {
                    // 子がいない場合は親の他の子を探す
                    for (int i = 0; i < parentBone.childCount; i++)
                    {
                        Transform child = parentBone.GetChild(i);
                        if (child != solver.targetBone)
                        {
                            solver.childBone = child;
                            break;
                        }
                    }
                }
            }

            if (solver.childBone == null)
            {
                Debug.LogWarning($"[Studio] BoneTwistController: Child bone is not found for {solver.targetBone.name}", this);
                return;
            }

            // BoneTwistSystemで初期化
            solver.twistData = BoneTwistSystem.Initiate(
                solver.targetBone,
                parentBone,
                solver.childBone,
                solver.weight,
                solver.parentChildCrossfade,
                solver.twistAngleOffset
            );

            solver.isInitialized = solver.twistData.isInitialized;

            // デフォルト回転に戻す
            _FixTransformSolver(solver);
        }

  

        /// <summary>
        /// 個別ソルバーのねじり補正を適用
        /// </summary>
        private void _RelaxSolver(BoneTwistSolver solver)
        {
            // 重みなどのパラメータを更新
            solver.twistData.weight = solver.weight;
            solver.twistData.parentChildCrossfade = solver.parentChildCrossfade;
            solver.twistData.twistAngleOffset = solver.twistAngleOffset;

            if (Mathf.Approximately(solver.twistData.weight, 0.0f))
            {
                return;
            }

            // 親ボーンを取得（常にtarget.parent）
            Transform parentBone = solver.targetBone.parent;
            if (parentBone == null)
            {
                return;
            }

            //Debug.Log($"[Studio] BoneTwistController: Relaxing {solver.twistData.weight}", this);

            // 子の回転を保存（ねじり補正後に復元するため）
            solver.childRotationCache = solver.childBone.rotation;

            // ねじり補正を適用
            solver.targetBone.rotation = BoneTwistSystem.Relax(
                in solver.twistData,
                solver.targetBone.rotation,
                parentBone.rotation,
                solver.childBone.rotation,
                solver.targetBone.position,
                parentBone.position,
                solver.childBone.position
            );

            // 子の回転を復元
            solver.childBone.rotation = solver.childRotationCache;
        }

        /// <summary>
        /// 個別ソルバーをデフォルト回転に戻す
        /// </summary>
        private void _FixTransformSolver(BoneTwistSolver solver)
        {
            if (!solver.isInitialized)
            {
                return;
            }

            solver.targetBone.localRotation = solver.twistData.targetInitialRotation;
            if (solver.childBone != null)
            {
                solver.childBone.localRotation = solver.twistData.childInitialRotation;
            }
        }

        /// <summary>
        /// すべてのソルバーをデフォルト回転に戻す
        /// </summary>
        public void FixTransforms()
        {
            foreach (var solver in _twistSolvers)
            {
                if (solver != null && solver.isInitialized)
                {
                    _FixTransformSolver(solver);
                }
            }
        }

        /// <summary>
        /// すべてのソルバーを再初期化
        /// </summary>
        public void ResetAll()
        {
            foreach (var solver in _twistSolvers)
            {
                if (solver != null)
                {
                    solver.isInitialized = false;
                }
            }
            _InitializeAll();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_twistSolvers == null || _twistSolvers.Length == 0)
            {
                return;
            }

            foreach (var solver in _twistSolvers)
            {
                if (solver == null || solver.targetBone == null)
                {
                    continue;
                }

                // 親ボーンを取得（常にtarget.parent）
                Transform parent = solver.targetBone.parent;

                // 子ボーンの自動検出（Gizmos表示用）
                Transform child = solver.childBone;
                if (child == null && solver.targetBone.childCount > 0)
                {
                    child = solver.targetBone.GetChild(0);
                }

                if (parent != null)
                {
                    // 親ボーンへのライン
                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(solver.targetBone.position, parent.position);
                }

                if (child != null)
                {
                    // 子ボーンへのライン
                    Gizmos.color = Color.blue;
                    Gizmos.DrawLine(solver.targetBone.position, child.position);
                }

                if (solver.isInitialized)
                {
                    // ねじり軸を描画
                    Gizmos.color = Color.cyan;
                    Vector3 twistAxisWorld = solver.targetBone.TransformDirection(solver.twistData.twistAxis);
                    Gizmos.DrawRay(solver.targetBone.position, twistAxisWorld * 0.05f);

                    // 補助軸を描画
                    Gizmos.color = Color.magenta;
                    Vector3 axisWorld = solver.targetBone.TransformDirection(solver.twistData.axis);
                    Gizmos.DrawRay(solver.targetBone.position, axisWorld * 0.05f);
                }
            }
        }
#endif
    }
}
