using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.LiveStudio
{

    [RequireComponent(typeof(Animator))]
    public class HumanPoseRetargeter : MonoBehaviour
    {
        public Animator sourceAnimator;

        private Animator _targetAnimator;


        private void Start()
        {
            _targetAnimator = GetComponent<Animator>();
            if (sourceAnimator == null || _targetAnimator == null)
            {
                Debug.LogError("HumanPoseRetargeter: Source or Target Animator is not assigned.");
                enabled = false;
                return;
            }
        }

        void Update()
        {
            Apply();
        }


        void Apply()
        {
            if (sourceAnimator == null) return;


            HumanPose sourcePose = new HumanPose();
            
            HumanPoseHandler sourcePoseHandler = new HumanPoseHandler(sourceAnimator.avatar, sourceAnimator.transform);
            HumanPoseHandler targetPoseHandler = new HumanPoseHandler(_targetAnimator.avatar, _targetAnimator.transform);

            // HumanPoseを取得
            sourcePoseHandler.GetHumanPose(ref sourcePose);
            
            // ターゲットのルート変換を事前に設定
            //_targetAnimator.transform.position = sourceAnimator.transform.position;
            //_targetAnimator.transform.rotation = sourceAnimator.transform.rotation;
            
            // HumanPoseを適用（bodyPosition/bodyRotationがルート相対で適用される）
            targetPoseHandler.SetHumanPose(ref sourcePose);

            sourcePoseHandler.Dispose();
            targetPoseHandler.Dispose();

        }
    }
}
