// Copyright (c) You-Ri, 2026
// Face tracking blendshapes are animated by Adjerry91's Face Tracking Templates

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// VRC Face Tracking v2 対応アバターコンポーネント
    /// ARKit 52値を FT/v2/... Animatorパラメータに変換して書き込む
    /// 身体アニメーションは AvatarAnimationSystem.UpdateBodyAnimation で処理
    /// </summary>
    [DefaultExecutionOrder(10)]
    [RequireComponent(typeof(Animator))]
    public class VRCFTAvatar : MonoBehaviour, IAvatar
    {
        //----------------------------------------------------------------------
        // Individual パラメータインデックス
        //----------------------------------------------------------------------
        const int FT_EyeLidRight = 0;
        const int FT_EyeLidLeft = 1;
        const int FT_EyeSquintRight = 2;
        const int FT_EyeSquintLeft = 3;
        const int FT_EyeWideRight = 4;
        const int FT_EyeWideLeft = 5;
        const int FT_EyeRightX = 6;
        const int FT_EyeRightY = 7;
        const int FT_EyeLeftX = 8;
        const int FT_EyeLeftY = 9;
        const int FT_BrowPinchRight = 10;
        const int FT_BrowPinchLeft = 11;
        const int FT_BrowLowererRight = 12;
        const int FT_BrowLowererLeft = 13;
        const int FT_BrowInnerUpRight = 14;
        const int FT_BrowInnerUpLeft = 15;
        const int FT_BrowOuterUpRight = 16;
        const int FT_BrowOuterUpLeft = 17;
        const int FT_NoseSneerRight = 18;
        const int FT_NoseSneerLeft = 19;
        const int FT_CheekSquintRight = 20;
        const int FT_CheekSquintLeft = 21;
        const int FT_CheekPuffSuckRight = 22;
        const int FT_CheekPuffSuckLeft = 23;
        const int FT_JawOpen = 24;
        const int FT_JawX = 25;
        const int FT_JawZ = 26;
        const int FT_MouthClosed = 27;
        const int FT_MouthCornerPullRight = 28;
        const int FT_MouthCornerPullLeft = 29;
        const int FT_MouthCornerSlantRight = 30;
        const int FT_MouthCornerSlantLeft = 31;
        const int FT_MouthFrownRight = 32;
        const int FT_MouthFrownLeft = 33;
        const int FT_MouthStretchRight = 34;
        const int FT_MouthStretchLeft = 35;
        const int FT_MouthUpperUpRight = 36;
        const int FT_MouthUpperUpLeft = 37;
        const int FT_MouthLowerDownRight = 38;
        const int FT_MouthLowerDownLeft = 39;
        const int FT_MouthDimpleRight = 40;
        const int FT_MouthDimpleLeft = 41;
        const int FT_MouthPressRight = 42;
        const int FT_MouthPressLeft = 43;
        const int FT_MouthRaiserUpper = 44;
        const int FT_MouthRaiserLower = 45;
        const int FT_MouthUpperX = 46;
        const int FT_MouthLowerX = 47;
        const int FT_LipFunnelUpperRight = 48;
        const int FT_LipFunnelUpperLeft = 49;
        const int FT_LipFunnelLowerRight = 50;
        const int FT_LipFunnelLowerLeft = 51;
        const int FT_LipPuckerUpperRight = 52;
        const int FT_LipPuckerUpperLeft = 53;
        const int FT_LipPuckerLowerRight = 54;
        const int FT_LipPuckerLowerLeft = 55;
        const int FT_LipSuckUpperRight = 56;
        const int FT_LipSuckUpperLeft = 57;
        const int FT_LipSuckLowerRight = 58;
        const int FT_LipSuckLowerLeft = 59;
        const int FT_TongueOut = 60;

        //----------------------------------------------------------------------
        // Combined パラメータインデックス
        //----------------------------------------------------------------------
        // Eye
        const int FT_EyeLid = 61;
        const int FT_EyeSquint = 62;
        const int FT_EyeX = 63;
        const int FT_EyeY = 64;
        // Brow
        const int FT_BrowInnerUp = 65;
        const int FT_BrowOuterUp = 66;
        const int FT_BrowDownRight = 67;
        const int FT_BrowDownLeft = 68;
        const int FT_BrowUp = 69;
        const int FT_BrowExpressionRight = 70;
        const int FT_BrowExpressionLeft = 71;
        const int FT_BrowExpression = 72;
        // Nose
        const int FT_NoseSneer = 73;
        // Cheek
        const int FT_CheekSquint = 74;
        const int FT_CheekPuffSuck = 75;
        // Lip
        const int FT_LipSuckUpper = 76;
        const int FT_LipSuckLower = 77;
        const int FT_LipSuck = 78;
        const int FT_LipFunnelUpper = 79;
        const int FT_LipFunnelLower = 80;
        const int FT_LipFunnel = 81;
        const int FT_LipPuckerUpper = 82;
        const int FT_LipPuckerLower = 83;
        const int FT_LipPucker = 84;
        // Mouth
        const int FT_MouthX = 85;
        const int FT_MouthUpperUp = 86;
        const int FT_MouthLowerDown = 87;
        const int FT_MouthOpen = 88;
        const int FT_MouthSmileRight = 89;
        const int FT_MouthSmileLeft = 90;
        const int FT_MouthSadRight = 91;
        const int FT_MouthSadLeft = 92;
        const int FT_SmileFrownRight = 93;
        const int FT_SmileFrownLeft = 94;
        const int FT_SmileFrown = 95;
        const int FT_SmileSadRight = 96;
        const int FT_SmileSadLeft = 97;
        const int FT_SmileSad = 98;

        const int FT_ParamCount = 99;

        static readonly string[] s_ftParamNames = new string[]
        {
            // --- Individual ---
            // Eye
            "FT/v2/EyeLidRight",
            "FT/v2/EyeLidLeft",
            "FT/v2/EyeSquintRight",
            "FT/v2/EyeSquintLeft",
            "FT/v2/EyeWideRight",
            "FT/v2/EyeWideLeft",
            "FT/v2/EyeRightX",
            "FT/v2/EyeRightY",
            "FT/v2/EyeLeftX",
            "FT/v2/EyeLeftY",
            // Brow
            "FT/v2/BrowPinchRight",
            "FT/v2/BrowPinchLeft",
            "FT/v2/BrowLowererRight",
            "FT/v2/BrowLowererLeft",
            "FT/v2/BrowInnerUpRight",
            "FT/v2/BrowInnerUpLeft",
            "FT/v2/BrowOuterUpRight",
            "FT/v2/BrowOuterUpLeft",
            // Nose
            "FT/v2/NoseSneerRight",
            "FT/v2/NoseSneerLeft",
            // Cheek
            "FT/v2/CheekSquintRight",
            "FT/v2/CheekSquintLeft",
            "FT/v2/CheekPuffSuckRight",
            "FT/v2/CheekPuffSuckLeft",
            // Jaw
            "FT/v2/JawOpen",
            "FT/v2/JawX",
            "FT/v2/JawZ",
            // Mouth
            "FT/v2/MouthClosed",
            "FT/v2/MouthCornerPullRight",
            "FT/v2/MouthCornerPullLeft",
            "FT/v2/MouthCornerSlantRight",
            "FT/v2/MouthCornerSlantLeft",
            "FT/v2/MouthFrownRight",
            "FT/v2/MouthFrownLeft",
            "FT/v2/MouthStretchRight",
            "FT/v2/MouthStretchLeft",
            "FT/v2/MouthUpperUpRight",
            "FT/v2/MouthUpperUpLeft",
            "FT/v2/MouthLowerDownRight",
            "FT/v2/MouthLowerDownLeft",
            "FT/v2/MouthDimpleRight",
            "FT/v2/MouthDimpleLeft",
            "FT/v2/MouthPressRight",
            "FT/v2/MouthPressLeft",
            "FT/v2/MouthRaiserUpper",
            "FT/v2/MouthRaiserLower",
            "FT/v2/MouthUpperX",
            "FT/v2/MouthLowerX",
            // Lip
            "FT/v2/LipFunnelUpperRight",
            "FT/v2/LipFunnelUpperLeft",
            "FT/v2/LipFunnelLowerRight",
            "FT/v2/LipFunnelLowerLeft",
            "FT/v2/LipPuckerUpperRight",
            "FT/v2/LipPuckerUpperLeft",
            "FT/v2/LipPuckerLowerRight",
            "FT/v2/LipPuckerLowerLeft",
            "FT/v2/LipSuckUpperRight",
            "FT/v2/LipSuckUpperLeft",
            "FT/v2/LipSuckLowerRight",
            "FT/v2/LipSuckLowerLeft",
            // Tongue
            "FT/v2/TongueOut",

            // --- Combined ---
            // Eye
            "FT/v2/EyeLid",
            "FT/v2/EyeSquint",
            "FT/v2/EyeX",
            "FT/v2/EyeY",
            // Brow
            "FT/v2/BrowInnerUp",
            "FT/v2/BrowOuterUp",
            "FT/v2/BrowDownRight",
            "FT/v2/BrowDownLeft",
            "FT/v2/BrowUp",
            "FT/v2/BrowExpressionRight",
            "FT/v2/BrowExpressionLeft",
            "FT/v2/BrowExpression",
            // Nose
            "FT/v2/NoseSneer",
            // Cheek
            "FT/v2/CheekSquint",
            "FT/v2/CheekPuffSuck",
            // Lip
            "FT/v2/LipSuckUpper",
            "FT/v2/LipSuckLower",
            "FT/v2/LipSuck",
            "FT/v2/LipFunnelUpper",
            "FT/v2/LipFunnelLower",
            "FT/v2/LipFunnel",
            "FT/v2/LipPuckerUpper",
            "FT/v2/LipPuckerLower",
            "FT/v2/LipPucker",
            // Mouth
            "FT/v2/MouthX",
            "FT/v2/MouthUpperUp",
            "FT/v2/MouthLowerDown",
            "FT/v2/MouthOpen",
            "FT/v2/MouthSmileRight",
            "FT/v2/MouthSmileLeft",
            "FT/v2/MouthSadRight",
            "FT/v2/MouthSadLeft",
            "FT/v2/SmileFrownRight",
            "FT/v2/SmileFrownLeft",
            "FT/v2/SmileFrown",
            "FT/v2/SmileSadRight",
            "FT/v2/SmileSadLeft",
            "FT/v2/SmileSad",
        };

        [SerializeField]
        [Tooltip("目の回転最大角度 (x: yaw, y: pitch)")]
        Vector2 _eyeRotationMax = new Vector2(40f, 40f);

        Animator _animator;
        MotionSourceBase _motionSource;
        bool _isTracking;

        float[] _targetValues;
        int[] _paramHashes;
        int[] _validParamIndices;

        AvatarExpressionConfig _expressionConfig;
        FacialKey[] _expressionKeys;
        Dictionary<string, int> _ftNameToIndex;

        Transform _leftEyeBone;
        Transform _rightEyeBone;
        Quaternion _leftEyeNeutral;
        Quaternion _rightEyeNeutral;
        Quaternion _leftEyeOffset;
        Quaternion _rightEyeOffset;

        void Start()
        {
            _animator = GetComponent<Animator>();

            // Animatorに実際に存在するパラメータのみをフィルタ
            var existingParams = new HashSet<int>();
            foreach (var p in _animator.parameters)
                existingParams.Add(p.nameHash);

            // 存在するパラメータのみ初期値を設定
            if (existingParams.Contains(Animator.StringToHash("EyeTrackingActive")))
                _animator.SetFloat("EyeTrackingActive", 1f);
            if (existingParams.Contains(Animator.StringToHash("LipTrackingActive")))
                _animator.SetFloat("LipTrackingActive", 1f);
            if (existingParams.Contains(Animator.StringToHash("ExpressionTrackingActive")))
                _animator.SetFloat("ExpressionTrackingActive", 1f);
            if (existingParams.Contains(Animator.StringToHash("IsLocal")))
                _animator.SetFloat("IsLocal", 1f);

            _targetValues = new float[FT_ParamCount];
            _paramHashes = new int[FT_ParamCount];
            for (int i = 0; i < FT_ParamCount; i++)
            {
                _paramHashes[i] = Animator.StringToHash(s_ftParamNames[i]);
            }

            var validIndices = new List<int>(FT_ParamCount);
            for (int i = 0; i < FT_ParamCount; i++)
            {
                if (existingParams.Contains(_paramHashes[i]))
                    validIndices.Add(i);
            }
            _validParamIndices = validIndices.ToArray();

            _expressionKeys = new FacialKey[FT_ParamCount];
            _ftNameToIndex = new Dictionary<string, int>(FT_ParamCount);
            for (int i = 0; i < FT_ParamCount; i++)
            {
                _expressionKeys[i] = FacialKey.CreateCustom(s_ftParamNames[i]);
                _ftNameToIndex[s_ftParamNames[i]] = i;
            }

            _SetupEyeBones();
            _isTracking = false;

            ((IAvatar)this).BuildAvatar();
        }

        void _SetupEyeBones()
        {
            _leftEyeBone = _animator.GetBoneTransform(HumanBodyBones.LeftEye);
            _rightEyeBone = _animator.GetBoneTransform(HumanBodyBones.RightEye);
            var headBone = _animator.GetBoneTransform(HumanBodyBones.Head);

            if (_leftEyeBone != null && headBone != null)
            {
                _leftEyeNeutral = _leftEyeBone.localRotation;
                _leftEyeOffset = Quaternion.Inverse(headBone.rotation) * _leftEyeBone.rotation;
            }
            if (_rightEyeBone != null && headBone != null)
            {
                _rightEyeNeutral = _rightEyeBone.localRotation;
                _rightEyeOffset = Quaternion.Inverse(headBone.rotation) * _rightEyeBone.rotation;
            }
        }

        void Update()
        {
            if (_motionSource == null || !_motionSource.frameData.isValid)
            {
                if (_isTracking)
                {
                    _SetShowMeshes(false);
                }
                _isTracking = false;
                return;
            }

            if (!_isTracking)
            {
                _SetShowMeshes(true);
            }
            _isTracking = true;

            _ComputeTargetValues();
            _ComputeCombinedValues();
            _WriteToAnimator();

            AvatarAnimationSystem.UpdateBodyAnimation(_animator, in _motionSource.frameData);
        }

        void LateUpdate()
        {
            if (!_isTracking || _motionSource == null) return;

            AvatarAnimationSystem.UpdateBodyAnimation(_animator, in _motionSource.frameData);
            _ApplyEyeRotation();
        }

        unsafe void _ComputeTargetValues()
        {
            ref var frame = ref _motionSource.frameData;
            fixed (float* bs = frame.expression.weights)
            {
                // EyeLid (special)
                _targetValues[FT_EyeLidRight] = Mathf.Clamp01(1f - bs[(int)ARKitBlendShapeLocation.EyeBlinkRight]) * 0.75f
                                              + bs[(int)ARKitBlendShapeLocation.EyeWideRight] * 0.25f;
                _targetValues[FT_EyeLidLeft] = Mathf.Clamp01(1f - bs[(int)ARKitBlendShapeLocation.EyeBlinkLeft]) * 0.75f
                                             + bs[(int)ARKitBlendShapeLocation.EyeWideLeft] * 0.25f;

                // Eye squint / wide
                _targetValues[FT_EyeSquintRight] = bs[(int)ARKitBlendShapeLocation.EyeSquintRight];
                _targetValues[FT_EyeSquintLeft] = bs[(int)ARKitBlendShapeLocation.EyeSquintLeft];
                _targetValues[FT_EyeWideRight] = bs[(int)ARKitBlendShapeLocation.EyeWideRight];
                _targetValues[FT_EyeWideLeft] = bs[(int)ARKitBlendShapeLocation.EyeWideLeft];

                // Eye gaze (difference)
                _targetValues[FT_EyeRightX] = bs[(int)ARKitBlendShapeLocation.EyeLookOutRight] - bs[(int)ARKitBlendShapeLocation.EyeLookInRight];
                _targetValues[FT_EyeRightY] = bs[(int)ARKitBlendShapeLocation.EyeLookUpRight] - bs[(int)ARKitBlendShapeLocation.EyeLookDownRight];
                _targetValues[FT_EyeLeftX] = bs[(int)ARKitBlendShapeLocation.EyeLookOutLeft] - bs[(int)ARKitBlendShapeLocation.EyeLookInLeft];
                _targetValues[FT_EyeLeftY] = bs[(int)ARKitBlendShapeLocation.EyeLookUpLeft] - bs[(int)ARKitBlendShapeLocation.EyeLookDownLeft];

                // Brow (browDown is a blended shape: BrowLowerer + BrowPinch)
                float browDownRight = bs[(int)ARKitBlendShapeLocation.BrowDownRight];
                float browDownLeft = bs[(int)ARKitBlendShapeLocation.BrowDownLeft];
                _targetValues[FT_BrowPinchRight] = browDownRight;
                _targetValues[FT_BrowPinchLeft] = browDownLeft;
                _targetValues[FT_BrowLowererRight] = browDownRight;
                _targetValues[FT_BrowLowererLeft] = browDownLeft;
                float browInnerUp = bs[(int)ARKitBlendShapeLocation.BrowInnerUp];
                _targetValues[FT_BrowInnerUpRight] = browInnerUp;
                _targetValues[FT_BrowInnerUpLeft] = browInnerUp;
                _targetValues[FT_BrowOuterUpRight] = bs[(int)ARKitBlendShapeLocation.BrowOuterUpRight];
                _targetValues[FT_BrowOuterUpLeft] = bs[(int)ARKitBlendShapeLocation.BrowOuterUpLeft];

                // Nose
                _targetValues[FT_NoseSneerRight] = bs[(int)ARKitBlendShapeLocation.NoseSneerRight];
                _targetValues[FT_NoseSneerLeft] = bs[(int)ARKitBlendShapeLocation.NoseSneerLeft];

                // Cheek
                _targetValues[FT_CheekSquintRight] = bs[(int)ARKitBlendShapeLocation.CheekSquintRight];
                _targetValues[FT_CheekSquintLeft] = bs[(int)ARKitBlendShapeLocation.CheekSquintLeft];
                float cheekPuff = bs[(int)ARKitBlendShapeLocation.CheekPuff];
                _targetValues[FT_CheekPuffSuckRight] = cheekPuff;
                _targetValues[FT_CheekPuffSuckLeft] = cheekPuff;

                // Jaw
                _targetValues[FT_JawOpen] = bs[(int)ARKitBlendShapeLocation.JawOpen];
                _targetValues[FT_JawX] = Mathf.Clamp(bs[(int)ARKitBlendShapeLocation.JawRight] - bs[(int)ARKitBlendShapeLocation.JawLeft], -1f, 1f);
                _targetValues[FT_JawZ] = bs[(int)ARKitBlendShapeLocation.JawForward];

                // Mouth
                _targetValues[FT_MouthClosed] = bs[(int)ARKitBlendShapeLocation.MouthClose];
                _targetValues[FT_MouthCornerPullRight] = bs[(int)ARKitBlendShapeLocation.MouthSmileRight];
                _targetValues[FT_MouthCornerPullLeft] = bs[(int)ARKitBlendShapeLocation.MouthSmileLeft];
                _targetValues[FT_MouthCornerSlantRight] = bs[(int)ARKitBlendShapeLocation.MouthSmileRight];
                _targetValues[FT_MouthCornerSlantLeft] = bs[(int)ARKitBlendShapeLocation.MouthSmileLeft];
                _targetValues[FT_MouthFrownRight] = bs[(int)ARKitBlendShapeLocation.MouthFrownRight];
                _targetValues[FT_MouthFrownLeft] = bs[(int)ARKitBlendShapeLocation.MouthFrownLeft];
                _targetValues[FT_MouthStretchRight] = bs[(int)ARKitBlendShapeLocation.MouthStretchRight];
                _targetValues[FT_MouthStretchLeft] = bs[(int)ARKitBlendShapeLocation.MouthStretchLeft];
                _targetValues[FT_MouthUpperUpRight] = bs[(int)ARKitBlendShapeLocation.MouthUpperUpRight];
                _targetValues[FT_MouthUpperUpLeft] = bs[(int)ARKitBlendShapeLocation.MouthUpperUpLeft];
                _targetValues[FT_MouthLowerDownRight] = bs[(int)ARKitBlendShapeLocation.MouthLowerDownRight];
                _targetValues[FT_MouthLowerDownLeft] = bs[(int)ARKitBlendShapeLocation.MouthLowerDownLeft];
                _targetValues[FT_MouthDimpleRight] = bs[(int)ARKitBlendShapeLocation.MouthDimpleRight];
                _targetValues[FT_MouthDimpleLeft] = bs[(int)ARKitBlendShapeLocation.MouthDimpleLeft];
                _targetValues[FT_MouthPressRight] = bs[(int)ARKitBlendShapeLocation.MouthPressRight];
                _targetValues[FT_MouthPressLeft] = bs[(int)ARKitBlendShapeLocation.MouthPressLeft];
                _targetValues[FT_MouthRaiserUpper] = bs[(int)ARKitBlendShapeLocation.MouthShrugUpper];
                _targetValues[FT_MouthRaiserLower] = bs[(int)ARKitBlendShapeLocation.MouthShrugLower];

                // Mouth directional (difference)
                float mouthX = Mathf.Clamp(bs[(int)ARKitBlendShapeLocation.MouthRight] - bs[(int)ARKitBlendShapeLocation.MouthLeft], -1f, 1f);
                _targetValues[FT_MouthUpperX] = mouthX;
                _targetValues[FT_MouthLowerX] = mouthX;

                // Lip funnel (1:4)
                float mouthFunnel = bs[(int)ARKitBlendShapeLocation.MouthFunnel];
                _targetValues[FT_LipFunnelUpperRight] = mouthFunnel;
                _targetValues[FT_LipFunnelUpperLeft] = mouthFunnel;
                _targetValues[FT_LipFunnelLowerRight] = mouthFunnel;
                _targetValues[FT_LipFunnelLowerLeft] = mouthFunnel;

                // Lip pucker (1:4)
                float mouthPucker = bs[(int)ARKitBlendShapeLocation.MouthPucker];
                _targetValues[FT_LipPuckerUpperRight] = mouthPucker;
                _targetValues[FT_LipPuckerUpperLeft] = mouthPucker;
                _targetValues[FT_LipPuckerLowerRight] = mouthPucker;
                _targetValues[FT_LipPuckerLowerLeft] = mouthPucker;

                // Lip suck (1:2)
                float mouthRollUpper = bs[(int)ARKitBlendShapeLocation.MouthRollUpper];
                _targetValues[FT_LipSuckUpperRight] = mouthRollUpper;
                _targetValues[FT_LipSuckUpperLeft] = mouthRollUpper;
                float mouthRollLower = bs[(int)ARKitBlendShapeLocation.MouthRollLower];
                _targetValues[FT_LipSuckLowerRight] = mouthRollLower;
                _targetValues[FT_LipSuckLowerLeft] = mouthRollLower;

                // Tongue
                _targetValues[FT_TongueOut] = bs[(int)ARKitBlendShapeLocation.TongueOut];
            }
        }

        void _ComputeCombinedValues()
        {
            // Eye combined
            _targetValues[FT_EyeLid] = (_targetValues[FT_EyeLidRight] + _targetValues[FT_EyeLidLeft]) * 0.5f;
            _targetValues[FT_EyeSquint] = (_targetValues[FT_EyeSquintRight] + _targetValues[FT_EyeSquintLeft]) * 0.5f;
            _targetValues[FT_EyeX] = (_targetValues[FT_EyeRightX] + _targetValues[FT_EyeLeftX]) * 0.5f;
            _targetValues[FT_EyeY] = (_targetValues[FT_EyeRightY] + _targetValues[FT_EyeLeftY]) * 0.5f;

            // Brow combined
            _targetValues[FT_BrowInnerUp] = (_targetValues[FT_BrowInnerUpRight] + _targetValues[FT_BrowInnerUpLeft]) * 0.5f;
            _targetValues[FT_BrowOuterUp] = (_targetValues[FT_BrowOuterUpRight] + _targetValues[FT_BrowOuterUpLeft]) * 0.5f;
            _targetValues[FT_BrowDownRight] = _targetValues[FT_BrowLowererRight];
            _targetValues[FT_BrowDownLeft] = _targetValues[FT_BrowLowererLeft];
            float browUpRight = Mathf.Max(_targetValues[FT_BrowInnerUpRight], _targetValues[FT_BrowOuterUpRight]);
            float browUpLeft = Mathf.Max(_targetValues[FT_BrowInnerUpLeft], _targetValues[FT_BrowOuterUpLeft]);
            _targetValues[FT_BrowUp] = (browUpRight + browUpLeft) * 0.5f;
            _targetValues[FT_BrowExpressionRight] = Mathf.Clamp(browUpRight - _targetValues[FT_BrowDownRight], -1f, 1f);
            _targetValues[FT_BrowExpressionLeft] = Mathf.Clamp(browUpLeft - _targetValues[FT_BrowDownLeft], -1f, 1f);
            _targetValues[FT_BrowExpression] = (_targetValues[FT_BrowExpressionRight] + _targetValues[FT_BrowExpressionLeft]) * 0.5f;

            // Nose combined
            _targetValues[FT_NoseSneer] = (_targetValues[FT_NoseSneerRight] + _targetValues[FT_NoseSneerLeft]) * 0.5f;

            // Cheek combined
            _targetValues[FT_CheekSquint] = (_targetValues[FT_CheekSquintRight] + _targetValues[FT_CheekSquintLeft]) * 0.5f;
            _targetValues[FT_CheekPuffSuck] = (_targetValues[FT_CheekPuffSuckRight] + _targetValues[FT_CheekPuffSuckLeft]) * 0.5f;

            // Lip combined
            _targetValues[FT_LipSuckUpper] = (_targetValues[FT_LipSuckUpperRight] + _targetValues[FT_LipSuckUpperLeft]) * 0.5f;
            _targetValues[FT_LipSuckLower] = (_targetValues[FT_LipSuckLowerRight] + _targetValues[FT_LipSuckLowerLeft]) * 0.5f;
            _targetValues[FT_LipSuck] = (_targetValues[FT_LipSuckUpper] + _targetValues[FT_LipSuckLower]) * 0.5f;
            _targetValues[FT_LipFunnelUpper] = (_targetValues[FT_LipFunnelUpperRight] + _targetValues[FT_LipFunnelUpperLeft]) * 0.5f;
            _targetValues[FT_LipFunnelLower] = (_targetValues[FT_LipFunnelLowerRight] + _targetValues[FT_LipFunnelLowerLeft]) * 0.5f;
            _targetValues[FT_LipFunnel] = (_targetValues[FT_LipFunnelUpper] + _targetValues[FT_LipFunnelLower]) * 0.5f;
            _targetValues[FT_LipPuckerUpper] = (_targetValues[FT_LipPuckerUpperRight] + _targetValues[FT_LipPuckerUpperLeft]) * 0.5f;
            _targetValues[FT_LipPuckerLower] = (_targetValues[FT_LipPuckerLowerRight] + _targetValues[FT_LipPuckerLowerLeft]) * 0.5f;
            _targetValues[FT_LipPucker] = (_targetValues[FT_LipPuckerUpper] + _targetValues[FT_LipPuckerLower]) * 0.5f;

            // Mouth combined
            _targetValues[FT_MouthX] = (_targetValues[FT_MouthUpperX] + _targetValues[FT_MouthLowerX]) * 0.5f;
            _targetValues[FT_MouthUpperUp] = (_targetValues[FT_MouthUpperUpRight] + _targetValues[FT_MouthUpperUpLeft]) * 0.5f;
            _targetValues[FT_MouthLowerDown] = (_targetValues[FT_MouthLowerDownRight] + _targetValues[FT_MouthLowerDownLeft]) * 0.5f;
            _targetValues[FT_MouthOpen] = (_targetValues[FT_MouthUpperUp] + _targetValues[FT_MouthLowerDown]) * 0.5f;

            float smileRight = (_targetValues[FT_MouthCornerPullRight] + _targetValues[FT_MouthCornerSlantRight]) * 0.5f;
            float smileLeft = (_targetValues[FT_MouthCornerPullLeft] + _targetValues[FT_MouthCornerSlantLeft]) * 0.5f;
            _targetValues[FT_MouthSmileRight] = smileRight;
            _targetValues[FT_MouthSmileLeft] = smileLeft;

            float sadRight = (_targetValues[FT_MouthFrownRight] + _targetValues[FT_MouthStretchRight]) * 0.5f;
            float sadLeft = (_targetValues[FT_MouthFrownLeft] + _targetValues[FT_MouthStretchLeft]) * 0.5f;
            _targetValues[FT_MouthSadRight] = sadRight;
            _targetValues[FT_MouthSadLeft] = sadLeft;

            _targetValues[FT_SmileFrownRight] = Mathf.Clamp(smileRight - _targetValues[FT_MouthFrownRight], -1f, 1f);
            _targetValues[FT_SmileFrownLeft] = Mathf.Clamp(smileLeft - _targetValues[FT_MouthFrownLeft], -1f, 1f);
            _targetValues[FT_SmileFrown] = (_targetValues[FT_SmileFrownRight] + _targetValues[FT_SmileFrownLeft]) * 0.5f;

            _targetValues[FT_SmileSadRight] = Mathf.Clamp(smileRight - sadRight, -1f, 1f);
            _targetValues[FT_SmileSadLeft] = Mathf.Clamp(smileLeft - sadLeft, -1f, 1f);
            _targetValues[FT_SmileSad] = (_targetValues[FT_SmileSadRight] + _targetValues[FT_SmileSadLeft]) * 0.5f;
        }


        void _WriteToAnimator()
        {
            for (int i = 0; i < _validParamIndices.Length; i++)
            {
                int idx = _validParamIndices[i];
                _animator.SetFloat(_paramHashes[idx], _targetValues[idx]);
            }
        }

        void _ApplyEyeRotation()
        {
            if (_leftEyeBone != null)
            {
                // EyeLeftX: 正=左向き(LookOut) なので反転して正=右向きに統一
                float yaw = _targetValues[FT_EyeLeftX] * _eyeRotationMax.x;
                float pitch = _targetValues[FT_EyeLeftY] * _eyeRotationMax.y;
                Vector3 input = new Vector3(-pitch, -yaw, 0f);
                _leftEyeBone.localRotation = Quaternion.Inverse(_leftEyeOffset) * Quaternion.Euler(input) * _leftEyeOffset * _leftEyeNeutral;
            }

            if (_rightEyeBone != null)
            {
                float yaw = -_targetValues[FT_EyeRightX] * _eyeRotationMax.x;
                float pitch = _targetValues[FT_EyeRightY] * _eyeRotationMax.y;
                Vector3 input = new Vector3(-pitch, -yaw, 0f);
                _rightEyeBone.localRotation = Quaternion.Inverse(_rightEyeOffset) * Quaternion.Euler(input) * _rightEyeOffset * _rightEyeNeutral;
            }
        }

        void _SetShowMeshes(bool visible)
        {
            var renderers = GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                renderer.enabled = visible;
            }
        }

        #region IAvatar

        void IAvatar.BuildAvatar()
        {
            if (_animator == null || _animator.avatar == null)
            {
                return;
            }

            var humanDescription = _animator.avatar.humanDescription;
            var avatarBuildData = AvatarBuildSystem.CreateAvatarBuildData(transform, humanDescription);
            if (avatarBuildData.humanBones == null || avatarBuildData.humanBones.Length == 0)
            {
                Debug.LogError("[Studio] VRCFTAvatar: Failed to extract Avatar data.");
                return;
            }

            AvatarBuildNotifier.NotifyAvatarBuilt(in avatarBuildData);
        }

        void IAvatar.SetExpressionConfig(AvatarExpressionConfig config)
        {
            _expressionConfig = config;
        }

        void IAvatar.SetMotionSource(MotionSourceBase motionSource)
        {
            _motionSource = motionSource;
        }

        bool IExpressionAvatar.SetWeight(FacialKey key, float weight)
        {
            if (string.IsNullOrEmpty(key.name)) return false;
            if (!_ftNameToIndex.TryGetValue(key.name, out int index)) return false;

            _targetValues[index] = Mathf.Clamp(weight, -1f, 1f);
            return true;
        }

        float IExpressionAvatar.GetWeight(FacialKey key)
        {
            if (string.IsNullOrEmpty(key.name)) return 0f;
            if (!_ftNameToIndex.TryGetValue(key.name, out int index)) return 0f;

            return _targetValues[index];
        }

        ReadOnlySpan<FacialKey> IExpressionAvatar.GetExpressions()
        {
            return _expressionKeys ?? ReadOnlySpan<FacialKey>.Empty;
        }

        public void ResetPhysics()
        {
        }

        #endregion
    }
}
