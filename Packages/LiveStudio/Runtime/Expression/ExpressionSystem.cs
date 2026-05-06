using System;
using System.Collections.Generic;

using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    public enum ARKitBlendShapeGroup
    {
        Blink,
        Eyes,
        Look,
        Mouth,
        Brow,
        Cheek,
        Other
    }

    public unsafe struct ARKitWeightData
    {
        static ARKitWeightData() => CompilerUtility.CheckUnmanaged<ARKitWeightData>();

        public fixed float weights[(int)ARKitBlendShapeLocation.Max];

        public ref float AtWeight(ARKitBlendShapeLocation index)
        {
            if ((int)index < 0 || (int)index >= (int)ARKitBlendShapeLocation.Max)
            {
                throw new System.IndexOutOfRangeException($"ARKitWeightData: index out of range {index}");
            }
            fixed (float* weights = this.weights)
            {
                return ref weights[(int)index];
            }
        }
    }

    /// <summary>
    /// スムージング処理の出力エントリ（unmanaged）
    /// </summary>
    public struct SmoothedWeightOutput
    {
        static SmoothedWeightOutput() => CompilerUtility.CheckUnmanaged<SmoothedWeightOutput>();

        public FixedString32Bytes name;
        public float newWeight;
        public bool shouldRemove;
    }

    /// <summary>
    /// スムージング処理の結果（unmanaged）
    /// </summary>
    public struct SmoothedWeightResult
    {
        static SmoothedWeightResult() => CompilerUtility.CheckUnmanaged<SmoothedWeightResult>();

        public ARKitWeightData arkitWeightData;
        public float totalWeight;
    }

    [System.Serializable]
    [ExposedClass(Icon = "tune")]
    [ExposedHelp("https://www.notion.so/WeightAdjustmentData-2999e258bd3d80999682ea2ed6092106")]
    public struct WeightAdjustmentData
    {
        static WeightAdjustmentData() => CompilerUtility.CheckUnmanaged<WeightAdjustmentData>();

        [ExposedField("name")]
        public ARKitBlendShapeLocation location;

        [ExposedField]
        public bool isSymmetric;

        [ExposedField, Slider(0, 1)]
        public float inputValueMin;

        [ExposedField, Slider(0, 1)]
        public float inputValueMax;

        [ExposedField, Slider(0, 1)]
        public float outputValueMin;

        [ExposedField, Slider(0, 1)]
        public float outputValueMax;

        [ExposedDefault]
        public static WeightAdjustmentData Default => new WeightAdjustmentData
        {
            location = ARKitBlendShapeLocation.JawOpen,
            isSymmetric = false,
            inputValueMin = 0f,
            inputValueMax = 1f,
            outputValueMin = 0f,
            outputValueMax = 1f,
        };

    }

    [System.Serializable]
    [ExposedClass(Icon = "sentiment_satisfied")]
    public struct ExpressionData
    {
        [ExposedField]
        public string name;

        [ExposedField, Slider(0, 1)]
        public float blendTime;

        [ExposedField, Slider(0, 1)]
        public float blinkWeightInfluence;

        [ExposedField, Slider(0, 1)]
        public float eyesWeightInfluence;

        [ExposedField, Slider(0, 1)]
        public float mouthWeightInfluence;

        [ExposedField, Slider(0, 1)]
        public float browWeightInfluence;

        [ExposedField, Slider(0, 1)]
        public float cheekWeightInfluence;

        [ExposedField]
        public WeightAdjustmentData[] adjustments;

        [ExposedDefault]
        public static ExpressionData Default => new ExpressionData
        {
            name = "",
            blendTime = 0.2f,
            blinkWeightInfluence = 1f,
            eyesWeightInfluence = 1f,
            mouthWeightInfluence = 1f,
            browWeightInfluence = 1f,
            cheekWeightInfluence = 1f,
            adjustments = new WeightAdjustmentData[0],
        };

        // unmanagedであることをコンパイル時に保証
        //unsafe static ExpressionData() => _ = sizeof(ExpressionData);
    }

    [System.Serializable]
    [ExposedClass(Icon = "tune")]
    public struct ARKitWeightAdjustmentData
    {
        [ExposedField]
        public WeightAdjustmentData[] adjustments;

        [ExposedDefault]
        public static ARKitWeightAdjustmentData Default => new ARKitWeightAdjustmentData
        {
            adjustments = new WeightAdjustmentData[0],
        };
    }

    public struct ExpressionValue
    {
        static ExpressionValue() => CompilerUtility.CheckUnmanaged<ExpressionValue>();

        public FixedString32Bytes name;

        public float weight;
    }

    public struct ExpressionWeights : IDisposable
    {
        static ExpressionWeights() => CompilerUtility.CheckUnmanaged<ExpressionWeights>();

        public NativeList<ExpressionValue> values;

        public bool IsCreated => values.IsCreated;

        public int Length => values.IsCreated ? values.Length : 0;

        public void SetOrAdd(string name, float weight)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].name.Equals(name))
                {
                    var ev = values[i];
                    ev.weight = weight;
                    values[i] = ev;
                    return;
                }
            }

            values.Add(new ExpressionValue
            {
                name = new FixedString32Bytes(name),
                weight = weight,
            });
        }

        public void Remove(string name)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].name.Equals(name))
                {
                    values.RemoveAt(i);
                }
            }

            //throw new System.Exception($"ExpressionWeights: Expression '{name}' not found.");
        }

        public void RemoveZeroWeights()
        {
            // NativeListはRemoveAllがないので逆順ループで削除
            for (int i = values.Length - 1; i >= 0; i--)
            {
                if (values[i].weight == 0f)
                {
                    values.RemoveAt(i);
                }
            }
        }

        public float Get(string name)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].name.Equals(name))
                {
                    return values[i].weight;
                }
            }

            throw new System.Exception($"ExpressionWeights: Expression '{name}' not found.");
        }

        public bool TryGet(string name, out float weight)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].name.Equals(name))
                {
                    weight = values[i].weight;
                    return true;
                }
            }

            weight = 0f;
            return false;
        }

        public void Dispose()
        {
            if (values.IsCreated)
            {
                values.Dispose();
            }
        }

        public static ExpressionWeights Create(Allocator allocator = Allocator.Persistent)
        {
            return new ExpressionWeights
            {
                values = new NativeList<ExpressionValue>(allocator),
            };
        }
    }

    /// <summary>
    /// 表情の計算結果を格納するワーク構造体
    /// </summary>
    public struct ExpressionWorkData : IDisposable
    {
        static ExpressionWorkData() => CompilerUtility.CheckUnmanaged<ExpressionWorkData>();

        public ARKitWeightData neutralArkitWeight;
        public NativeArray<ARKitWeightData> expressionArkitWeights;

        public bool IsCreated => expressionArkitWeights.IsCreated;

        public static ExpressionWorkData Create(int expressionCount, Allocator allocator)
        {
            return new ExpressionWorkData
            {
                neutralArkitWeight = new ARKitWeightData(),
                expressionArkitWeights = new NativeArray<ARKitWeightData>(expressionCount, allocator)
            };
        }

        public void EnsureCapacity(int expressionCount, Allocator allocator)
        {
            if (!expressionArkitWeights.IsCreated || expressionArkitWeights.Length != expressionCount)
            {
                if (expressionArkitWeights.IsCreated)
                    expressionArkitWeights.Dispose();
                expressionArkitWeights = new NativeArray<ARKitWeightData>(expressionCount, allocator);
            }
        }

        public void Dispose()
        {
            if (expressionArkitWeights.IsCreated)
                expressionArkitWeights.Dispose();
        }
    }


    public static class ExpressionSystem
    {
        /// <summary>
        /// スムージング処理で使用する閾値
        /// </summary>
        public const float kWeightThreshold = 0.01f;

        /// <summary>
        /// 全ウェイトの合計を1以下に収める。先頭の要素を優先。
        /// </summary>
        /// <param name="source">入力ウェイト</param>
        /// <param name="destination">出力ウェイト（事前にCreateで確保済み）</param>
        public static void Mix(in ExpressionWeights source, ref ExpressionWeights destination)
        {
            destination.values.Clear();

            float remaining = 1f;

            for (int i = 0; i < source.values.Length; i++)
            {
                var ev = source.values[source.values.Length-i-1];
                float mixedWeight = Mathf.Min(ev.weight, remaining);
                remaining -= mixedWeight;

                mixedWeight = Mathf.Clamp01(mixedWeight);

                destination.values.Add(new ExpressionValue
                {
                    name = ev.name,
                    weight = mixedWeight,
                });
            }
        }

        /// <summary>
        /// 単一の表情ウェイトをスムージング処理
        /// </summary>
        /// <param name="expressionName">表情名</param>
        /// <param name="targetWeight">目標ウェイト</param>
        /// <param name="currentWeight">現在のウェイト</param>
        /// <param name="config">表情設定</param>
        /// <param name="expressionIndex">使用された表情のインデックス（見つからない場合は-1）</param>
        /// <returns>スムージング後のウェイト</returns>
        public static float ProcessSmoothedWeight(
            string expressionName,
            float targetWeight,
            float currentWeight,
            AvatarExpressionConfig config,
            out int expressionIndex)
        {
            var expressions = config?.expressions;
            ExpressionData expressionData;
            if (expressions != null && TryFindExpressionIndex(expressions, expressionName, out expressionIndex))
            {
                expressionData = expressions[expressionIndex];
            }
            else
            {
                expressionIndex = -1;
                expressionData = config?.neutralExpression ?? ExpressionData.Default;
            }

            // ブレンド処理
            float newWeight = GetBlendingValue(expressionData, targetWeight, currentWeight);

            // 閾値以下なら目標値に設定
            if (Mathf.Abs(newWeight - targetWeight) < kWeightThreshold)
            {
                newWeight = targetWeight;
            }

            return newWeight;
        }

        /// <summary>
        /// 中立表情のウェイトを計算
        /// </summary>
        public static float CalculateNeutralWeight(float totalWeight)
        {
            return Mathf.Clamp01(1.0f - totalWeight);
        }

        /// <summary>
        /// ApplySmoothedWeightsの共通処理
        /// </summary>
        /// <param name="mixedWeights">ミックス済み表情ウェイト（目標値）</param>
        /// <param name="outputs">出力（スムージング後のウェイト、前回の値を現在値として使用）</param>
        /// <param name="sourceWeights">元の表情ウェイト（0になったエントリの削除用）</param>
        /// <param name="config">表情設定</param>
        /// <param name="workData">表情ワークデータ（ARKitWeight用）</param>
        /// <returns>処理結果（ARKitWeightData, totalWeight）</returns>
        public static SmoothedWeightResult Smooth(
            in ExpressionWeights mixedWeights,
            ref ExpressionWeights outputs,
            ref ExpressionWeights sourceWeights,
            AvatarExpressionConfig config,
            in ExpressionWorkData workData)
        {
            var result = new SmoothedWeightResult
            {
                arkitWeightData = new ARKitWeightData(),
                totalWeight = 0f
            };

            for (int i = 0; i < mixedWeights.Length; i++)
            {
                var ev = mixedWeights.values[i];
                string nameStr = ev.name.ToString();

                // outputsの既存値を現在値として取得
                outputs.TryGet(nameStr, out float currentWeight);

                float newWeight = ProcessSmoothedWeight(
                    nameStr,
                    ev.weight,           // target weight
                    currentWeight,       // current weight (from previous outputs)
                    config,
                    out int expressionIndex);

                outputs.SetOrAdd(nameStr, newWeight);

                result.totalWeight += newWeight;

                // workDataから対応するarkitWeightを取得
                if (expressionIndex >= 0 && workData.IsCreated && expressionIndex < workData.expressionArkitWeights.Length)
                {
                    AddARKitWeights(workData.expressionArkitWeights[expressionIndex], ref result.arkitWeightData, newWeight);
                }
                else
                {
                    AddARKitWeights(workData.neutralArkitWeight, ref result.arkitWeightData, newWeight);
                }
            }

            // neutral weightを追加
            float neutralWeight = CalculateNeutralWeight(result.totalWeight);
            AddARKitWeights(workData.neutralArkitWeight, ref result.arkitWeightData, neutralWeight);

            // sourceWeightsの0エントリを削除（outputsは残してVRMに0を適用させる）
            for (int i = outputs.Length - 1; i >= 0; i--)
            {
                if (outputs.values[i].weight == 0f)
                {
                    string name = outputs.values[i].name.ToString();
                    if (sourceWeights.TryGet(name, out float srcWeight) && srcWeight == 0f)
                    {
                        sourceWeights.Remove(name);
                    }
                }
            }

            return result;
        }

        public readonly static ARKitBlendShapeLocation[] kARKitBlendShapeLocationMirroring = new ARKitBlendShapeLocation[]
        {
            ARKitBlendShapeLocation.BrowDownRight,     // BrowDownLeft
            ARKitBlendShapeLocation.BrowDownLeft,      // BrowDownRight
            ARKitBlendShapeLocation.BrowInnerUp,       // BrowInnerUp (no mirror)
            ARKitBlendShapeLocation.BrowOuterUpRight,  // BrowOuterUpLeft
            ARKitBlendShapeLocation.BrowOuterUpLeft,   // BrowOuterUpRight
            ARKitBlendShapeLocation.CheekPuff,         // CheekPuff (no mirror)
            ARKitBlendShapeLocation.CheekSquintRight,  // CheekSquintLeft
            ARKitBlendShapeLocation.CheekSquintLeft,   // CheekSquintRight
            ARKitBlendShapeLocation.EyeBlinkRight,     // EyeBlinkLeft
            ARKitBlendShapeLocation.EyeBlinkLeft,      // EyeBlinkRight
            ARKitBlendShapeLocation.EyeLookDownRight,  // EyeLookDownLeft
            ARKitBlendShapeLocation.EyeLookDownLeft,   // EyeLookDownRight
            ARKitBlendShapeLocation.EyeLookInRight,    // EyeLookInLeft
            ARKitBlendShapeLocation.EyeLookInLeft,     // EyeLookInRight
            ARKitBlendShapeLocation.EyeLookOutRight,   // EyeLookOutLeft
            ARKitBlendShapeLocation.EyeLookOutLeft,    // EyeLookOutRight
            ARKitBlendShapeLocation.EyeLookUpRight,    // EyeLookUpLeft
            ARKitBlendShapeLocation.EyeLookUpLeft,     // EyeLookUpRight
            ARKitBlendShapeLocation.EyeSquintRight,    // EyeSquintLeft
            ARKitBlendShapeLocation.EyeSquintLeft,     // EyeSquintRight
            ARKitBlendShapeLocation.EyeWideRight,      // EyeWideLeft
            ARKitBlendShapeLocation.EyeWideLeft,       // EyeWideRight
            ARKitBlendShapeLocation.JawForward,        // JawForward (no mirror)
            ARKitBlendShapeLocation.JawRight,          // JawLeft
            ARKitBlendShapeLocation.JawOpen,           // JawOpen (no mirror)
            ARKitBlendShapeLocation.JawLeft,           // JawRight
            ARKitBlendShapeLocation.MouthClose,        // MouthClose (no mirror)
            ARKitBlendShapeLocation.MouthDimpleRight,  // MouthDimpleLeft
            ARKitBlendShapeLocation.MouthDimpleLeft,   // MouthDimpleRight
            ARKitBlendShapeLocation.MouthFrownRight,   // MouthFrownLeft
            ARKitBlendShapeLocation.MouthFrownLeft,    // MouthFrownRight
            ARKitBlendShapeLocation.MouthFunnel,       // MouthFunnel (no mirror)
            ARKitBlendShapeLocation.MouthRight,        // MouthLeft
            ARKitBlendShapeLocation.MouthLowerDownRight, // MouthLowerDownLeft
            ARKitBlendShapeLocation.MouthLowerDownLeft,  // MouthLowerDownRight
            ARKitBlendShapeLocation.MouthPressRight,   // MouthPressLeft
            ARKitBlendShapeLocation.MouthPressLeft,    // MouthPressRight
            ARKitBlendShapeLocation.MouthPucker,       // MouthPucker (no mirror)
            ARKitBlendShapeLocation.MouthLeft,         // MouthRight
            ARKitBlendShapeLocation.MouthRollLower,    // MouthRollLower (no mirror)
            ARKitBlendShapeLocation.MouthRollUpper,    // MouthRollUpper (no mirror)
            ARKitBlendShapeLocation.MouthShrugLower,   // MouthShrugLower (no mirror)
            ARKitBlendShapeLocation.MouthShrugUpper,   // MouthShrugUpper (no mirror)
            ARKitBlendShapeLocation.MouthSmileRight,   // MouthSmileLeft
            ARKitBlendShapeLocation.MouthSmileLeft,    // MouthSmileRight
            ARKitBlendShapeLocation.MouthStretchRight, // MouthStretchLeft
            ARKitBlendShapeLocation.MouthStretchLeft,  // MouthStretchRight
            ARKitBlendShapeLocation.MouthUpperUpRight, // MouthUpperUpLeft
            ARKitBlendShapeLocation.MouthUpperUpLeft,  // MouthUpperUpRight
            ARKitBlendShapeLocation.NoseSneerRight,    // NoseSneerLeft
            ARKitBlendShapeLocation.NoseSneerLeft,     // NoseSneerRight
            ARKitBlendShapeLocation.TongueOut,         // TongueOut (no mirror)
        };

        public readonly static ARKitBlendShapeGroup[] kBlendShapeLocationToGroup = new ARKitBlendShapeGroup[]
        {
            ARKitBlendShapeGroup.Brow, // BrowDownLeft
            ARKitBlendShapeGroup.Brow, // BrowDownRight
            ARKitBlendShapeGroup.Brow, // BrowInnerUp
            ARKitBlendShapeGroup.Brow, // BrowOuterUpLeft
            ARKitBlendShapeGroup.Brow, // BrowOuterUpRight
            ARKitBlendShapeGroup.Cheek, // CheekPuff
            ARKitBlendShapeGroup.Cheek, // CheekSquintLeft
            ARKitBlendShapeGroup.Cheek, // CheekSquintRight
            ARKitBlendShapeGroup.Blink, // EyeBlinkLeft
            ARKitBlendShapeGroup.Blink, // EyeBlinkRight
            ARKitBlendShapeGroup.Look, // EyeLookDownLeft
            ARKitBlendShapeGroup.Look, // EyeLookDownRight
            ARKitBlendShapeGroup.Look, // EyeLookInLeft
            ARKitBlendShapeGroup.Look, // EyeLookInRight
            ARKitBlendShapeGroup.Look, // EyeLookOutLeft
            ARKitBlendShapeGroup.Look, // EyeLookOutRight
            ARKitBlendShapeGroup.Look, // EyeLookUpLeft
            ARKitBlendShapeGroup.Look, // EyeLookUpRight
            ARKitBlendShapeGroup.Eyes, // EyeSquintLeft
            ARKitBlendShapeGroup.Eyes, // EyeSquintRight
            ARKitBlendShapeGroup.Eyes, // EyeWideLeft
            ARKitBlendShapeGroup.Eyes, // EyeWideRight
            ARKitBlendShapeGroup.Mouth, // JawForward
            ARKitBlendShapeGroup.Mouth, // JawLeft
            ARKitBlendShapeGroup.Mouth, // JawOpen
            ARKitBlendShapeGroup.Mouth, // JawRight
            ARKitBlendShapeGroup.Mouth, // MouthClose
            ARKitBlendShapeGroup.Mouth, // MouthDimpleLeft
            ARKitBlendShapeGroup.Mouth, // MouthDimpleRight
            ARKitBlendShapeGroup.Mouth, // MouthFrownLeft
            ARKitBlendShapeGroup.Mouth, // MouthFrownRight
            ARKitBlendShapeGroup.Mouth, // MouthFunnel
            ARKitBlendShapeGroup.Mouth, // MouthLeft
            ARKitBlendShapeGroup.Mouth, // MouthLowerDownLeft
            ARKitBlendShapeGroup.Mouth, // MouthLowerDownRight
            ARKitBlendShapeGroup.Mouth, // MouthPressLeft
            ARKitBlendShapeGroup.Mouth, // MouthPressRight
            ARKitBlendShapeGroup.Mouth, // MouthPucker
            ARKitBlendShapeGroup.Mouth, // MouthRight
            ARKitBlendShapeGroup.Mouth, // MouthRollLower
            ARKitBlendShapeGroup.Mouth, // MouthRollUpper
            ARKitBlendShapeGroup.Mouth, // MouthShrugLower
            ARKitBlendShapeGroup.Mouth, // MouthShrugUpper
            ARKitBlendShapeGroup.Mouth, // MouthSmileLeft
            ARKitBlendShapeGroup.Mouth, // MouthSmileRight
            ARKitBlendShapeGroup.Mouth, // MouthStretchLeft
            ARKitBlendShapeGroup.Mouth, // MouthStretchRight
            ARKitBlendShapeGroup.Mouth, // MouthUpperUpLeft
            ARKitBlendShapeGroup.Mouth, // MouthUpperUpRight
            ARKitBlendShapeGroup.Other, // NoseSneerLeft
            ARKitBlendShapeGroup.Other, // NoseSneerRight
            ARKitBlendShapeGroup.Other, // TongueOut
        };


        public static bool TryFindExpressionData(ExpressionData[] data, string expressionName, out ExpressionData result)
        {
            Debug.Assert(data != null, "ExpressionData is null");

            foreach (var exp in data)
            {
                if (exp.name == expressionName)
                {
                    result = exp;
                    return true;
                }
            }

            result = ExpressionData.Default;
            return false;
        }

        public static bool TryFindExpressionIndex(ExpressionData[] data, string expressionName, out int index)
        {
            if (data == null)
            {
                index = -1;
                return false;
            }

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i].name == expressionName)
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        public unsafe static void Copy(in ARKitWeightData src, out ARKitWeightData dst)
        {
            dst = new ARKitWeightData();
            fixed (float* srcPtr = src.weights)
            fixed (float* dstPtr = dst.weights)
            {
                UnsafeUtility.MemCpy(dstPtr, srcPtr, sizeof(float) * (int)ARKitBlendShapeLocation.Max);
            }
        }


        public unsafe static bool UpdateWeights(in ARKitWeightAdjustmentData data, in ARKitWeightData input, out ARKitWeightData output)
        {
            //Debug.Assert(data != null, "ExpressionData is null");

            output = new ARKitWeightData();
            Copy(in input, out output);

            foreach (var wad in data.adjustments)
            {
                var value = input.weights[(int)wad.location];
                float adjustedValue = GetAdjustmentedValue(wad, value);
                output.weights[(int)wad.location] = Mathf.Clamp01(adjustedValue);

                if (wad.isSymmetric)
                {
                    var mirroredName = kARKitBlendShapeLocationMirroring[(int)wad.location];
                    float mirroredValue = input.weights[(int)mirroredName];
                    float mirroredAdjustedValue = GetAdjustmentedValue(wad, mirroredValue);
                    output.weights[(int)mirroredName] = Mathf.Clamp01(mirroredAdjustedValue);
                }
            }

            return true;
        }


        public unsafe static bool UpdateWeights(in ExpressionData data, in ARKitWeightData input, out ARKitWeightData output)
        {
            var influenced = new ARKitWeightData();
            for (int i = 0; i < (int)ARKitBlendShapeLocation.Max; i++)
            {
                var originalValue = input.weights[i];

                float influencedValue = originalValue;

                var group = kBlendShapeLocationToGroup[i];
                switch (group)
                {
                    case ARKitBlendShapeGroup.Blink:
                        influencedValue = data.blinkWeightInfluence * originalValue;
                        break;
                    case ARKitBlendShapeGroup.Eyes:
                        influencedValue = data.eyesWeightInfluence * originalValue;
                        break;
                    case ARKitBlendShapeGroup.Mouth:
                        influencedValue = data.mouthWeightInfluence * originalValue;
                        break;
                    case ARKitBlendShapeGroup.Brow:
                        influencedValue = data.browWeightInfluence * originalValue;
                        break;
                    case ARKitBlendShapeGroup.Cheek:
                        influencedValue = data.cheekWeightInfluence * originalValue;
                        break;

                    case ARKitBlendShapeGroup.Look:
                    case ARKitBlendShapeGroup.Other:
                    default:
                        influencedValue = originalValue;
                        break;
                }
                influenced.weights[i] = influencedValue;
            }

            output = new ARKitWeightData();
            Copy(in influenced, out output);
            if (data.adjustments != null)
            {
                foreach (var wad in data.adjustments)
                {
                    var value = influenced.weights[(int)wad.location];
                    float adjustedValue = GetAdjustmentedValue(wad, value);
                    output.weights[(int)wad.location] = Mathf.Clamp01(adjustedValue);

                    if (wad.isSymmetric)
                    {
                        var mirroredName = kARKitBlendShapeLocationMirroring[(int)wad.location];
                        float mirroredValue = influenced.weights[(int)mirroredName];
                        float mirroredAdjustedValue = GetAdjustmentedValue(wad, mirroredValue);
                        output.weights[(int)mirroredName] = Mathf.Clamp01(mirroredAdjustedValue);
                    }
                }

            }

            return true;
        }

        public static float GetAdjustmentedValue(in WeightAdjustmentData data, float value)
        {
            if (!Mathf.Approximately(data.inputValueMin, data.inputValueMax))
            {
                value = (value - data.inputValueMin) / (data.inputValueMax - data.inputValueMin);   // ex. min:0.5 max:1 value:0.75 -> 0.5 value:0.5 -> 0
            }
            value = Mathf.Lerp(data.outputValueMin, data.outputValueMax, value);                    // ex. min:0.5 max:1 value:0.5 -> 0.75 value:0 -> 0
            return value;
        }

        public static float GetBlendingValue(in ExpressionData data, float value, float prevValue)
        {
            if (data.blendTime <= 0f)
            {
                return value; // ブレンド時間が0の場合は即座に適用
            }

            // 指数減衰: blendTime秒で約99%収束
            float t = 1f - Mathf.Exp(-5f * Time.deltaTime / data.blendTime);
            value = Mathf.Lerp(prevValue, value, t);
            return value;
        }

        public static void AddARKitWeights(in ARKitWeightData arkitWeight, ref ARKitWeightData totalWeights, float multiplier)
        {
            for (int i = 0; i < (int)ARKitBlendShapeLocation.Max; i++)
            {
                var loc = (ARKitBlendShapeLocation)i;
                totalWeights.AtWeight(loc) += arkitWeight.AtWeight(loc) * multiplier;
            }
        }


        /// <summary>
        /// ARKitブレンドシェイプから目の向き（Yaw, Pitch）を計算
        /// </summary>
        public static unsafe void CalculateEyeDirection(in ARKitWeightData src, out Vector2 eyeDirection)
        {
            var eyeYaw = (src.weights[(int)ARKitBlendShapeLocation.EyeLookInRight]
                        - src.weights[(int)ARKitBlendShapeLocation.EyeLookOutRight]) * 0.5f;
            var eyePitch = (src.weights[(int)ARKitBlendShapeLocation.EyeLookUpRight]
                          - src.weights[(int)ARKitBlendShapeLocation.EyeLookDownRight]) * 0.5f;
            eyeYaw += (src.weights[(int)ARKitBlendShapeLocation.EyeLookOutLeft]
                     - src.weights[(int)ARKitBlendShapeLocation.EyeLookInLeft]) * 0.5f;
            eyePitch += (src.weights[(int)ARKitBlendShapeLocation.EyeLookUpLeft]
                       - src.weights[(int)ARKitBlendShapeLocation.EyeLookDownLeft]) * 0.5f;
            eyeDirection = new Vector2(eyeYaw, eyePitch);
        }
    }
}
