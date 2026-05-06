using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Collections.LowLevel.Unsafe;

using Lilium.LiveStudio;

namespace Lilium.LiveStudio.EditorTests
{
    public class ExpressionSystemTests
    {
        [Test]
        public void ARKitWeightData_IsUnmanaged()
        {
            Assert.IsTrue(UnsafeUtility.IsUnmanaged<ARKitWeightData>());
        }

        [Test]
        public void ARKitWeightData_AtWeight_ValidIndex()
        {
            var data = new ARKitWeightData();

            // 有効なインデックスでアクセス
            data.AtWeight(ARKitBlendShapeLocation.BrowDownLeft) = 0.5f;
            Assert.AreEqual(0.5f, data.AtWeight(ARKitBlendShapeLocation.BrowDownLeft));

            data.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft) = 0.75f;
            Assert.AreEqual(0.75f, data.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft));

            data.AtWeight(ARKitBlendShapeLocation.MouthSmileLeft) = 1.0f;
            Assert.AreEqual(1.0f, data.AtWeight(ARKitBlendShapeLocation.MouthSmileLeft));
        }

        [Test]
        public void ARKitWeightData_AtWeight_InvalidIndex_ThrowsException()
        {
            var data = new ARKitWeightData();

            // 無効なインデックスでアクセス（負の値）
            Assert.Throws<System.IndexOutOfRangeException>(() =>
            {
                var _ = data.AtWeight((ARKitBlendShapeLocation)(-1));
            });

            // 無効なインデックスでアクセス（最大値以上）
            Assert.Throws<System.IndexOutOfRangeException>(() =>
            {
                var _ = data.AtWeight(ARKitBlendShapeLocation.Max);
            });
        }

        [Test]
        public void GetAdjustmentedValue_NoAdjustment()
        {
            var adjustment = new WeightAdjustmentData
            {
                inputValueMin = 0f,
                inputValueMax = 1f,
                outputValueMin = 0f,
                outputValueMax = 1f
            };

            Assert.AreEqual(0f, ExpressionSystem.GetAdjustmentedValue(adjustment, 0f), 0.001f);
            Assert.AreEqual(0.5f, ExpressionSystem.GetAdjustmentedValue(adjustment, 0.5f), 0.001f);
            Assert.AreEqual(1f, ExpressionSystem.GetAdjustmentedValue(adjustment, 1f), 0.001f);
        }

        [Test]
        public void GetAdjustmentedValue_ScaleOutput()
        {
            var adjustment = new WeightAdjustmentData
            {
                inputValueMin = 0f,
                inputValueMax = 1f,
                outputValueMin = 0f,
                outputValueMax = 0.5f
            };

            Assert.AreEqual(0f, ExpressionSystem.GetAdjustmentedValue(adjustment, 0f), 0.001f);
            Assert.AreEqual(0.25f, ExpressionSystem.GetAdjustmentedValue(adjustment, 0.5f), 0.001f);
            Assert.AreEqual(0.5f, ExpressionSystem.GetAdjustmentedValue(adjustment, 1f), 0.001f);
        }

        [Test]
        public void GetAdjustmentedValue_RemapInput()
        {
            var adjustment = new WeightAdjustmentData
            {
                inputValueMin = 0.5f,
                inputValueMax = 1f,
                outputValueMin = 0f,
                outputValueMax = 1f
            };

            // 入力が0.5以下の場合は負の値になるが、出力範囲内で計算される
            Assert.AreEqual(0f, ExpressionSystem.GetAdjustmentedValue(adjustment, 0f), 0.001f);
            // 入力が0.5の場合は出力0
            Assert.AreEqual(0f, ExpressionSystem.GetAdjustmentedValue(adjustment, 0.5f), 0.001f);
            // 入力が0.75の場合は出力0.5
            Assert.AreEqual(0.5f, ExpressionSystem.GetAdjustmentedValue(adjustment, 0.75f), 0.001f);
            // 入力が1の場合は出力1
            Assert.AreEqual(1f, ExpressionSystem.GetAdjustmentedValue(adjustment, 1f), 0.001f);
        }

        [Test]
        public void GetAdjustmentedValue_RemapInputAndOutput()
        {
            var adjustment = new WeightAdjustmentData
            {
                inputValueMin = 0.2f,
                inputValueMax = 0.8f,
                outputValueMin = 0.5f,
                outputValueMax = 1f
            };

            // 入力が0.2の場合は出力0.5
            Assert.AreEqual(0.5f, ExpressionSystem.GetAdjustmentedValue(adjustment, 0.2f), 0.001f);
            // 入力が0.5の場合は出力0.75
            Assert.AreEqual(0.75f, ExpressionSystem.GetAdjustmentedValue(adjustment, 0.5f), 0.001f);
            // 入力が0.8の場合は出力1.0
            Assert.AreEqual(1f, ExpressionSystem.GetAdjustmentedValue(adjustment, 0.8f), 0.001f);
        }

        [Test]
        public void GetAdjustmentedValue_SameInputMinMax()
        {
            var adjustment = new WeightAdjustmentData
            {
                inputValueMin = 0.5f,
                inputValueMax = 0.5f, // 同じ値
                outputValueMin = 0f,
                outputValueMax = 1f
            };

            // inputMin == inputMax の場合、入力値をそのまま使用
            Assert.AreEqual(0f, ExpressionSystem.GetAdjustmentedValue(adjustment, 0f), 0.001f);
            Assert.AreEqual(0.5f, ExpressionSystem.GetAdjustmentedValue(adjustment, 0.5f), 0.001f);
            Assert.AreEqual(1f, ExpressionSystem.GetAdjustmentedValue(adjustment, 1f), 0.001f);
        }

        [Test]
        public void UpdateWeights_NoAdjustments()
        {
            var expressionData = new ExpressionData
            {
                name = "TestExpression",
                blinkWeightInfluence = 1f,
                eyesWeightInfluence = 1f,
                mouthWeightInfluence = 1f,
                browWeightInfluence = 1f,
                cheekWeightInfluence = 1f,
                adjustments = new WeightAdjustmentData[0]
            };

            var sourceData = new ARKitWeightData();
            sourceData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft) = 0.8f;
            sourceData.AtWeight(ARKitBlendShapeLocation.MouthSmileLeft) = 0.6f;
            sourceData.AtWeight(ARKitBlendShapeLocation.BrowDownLeft) = 0.4f;

            ExpressionSystem.UpdateWeights(expressionData, in sourceData, out var adjustedData);

            Assert.AreEqual(0.8f, adjustedData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft), 0.001f);
            Assert.AreEqual(0.6f, adjustedData.AtWeight(ARKitBlendShapeLocation.MouthSmileLeft), 0.001f);
            Assert.AreEqual(0.4f, adjustedData.AtWeight(ARKitBlendShapeLocation.BrowDownLeft), 0.001f);
        }

        [Test]
        public void UpdateWeights_WithGroupInfluence()
        {
            var expressionData = new ExpressionData
            {
                name = "TestExpression",
                blinkWeightInfluence = 0.5f,
                eyesWeightInfluence = 0.7f,
                mouthWeightInfluence = 0.3f,
                browWeightInfluence = 0.9f,
                cheekWeightInfluence = 0.6f,
                adjustments = new WeightAdjustmentData[0]
            };

            var sourceData = new ARKitWeightData();
            sourceData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft) = 1.0f;      // Blink group
            sourceData.AtWeight(ARKitBlendShapeLocation.EyeSquintLeft) = 1.0f;     // Eyes group
            sourceData.AtWeight(ARKitBlendShapeLocation.MouthSmileLeft) = 1.0f;    // Mouth group
            sourceData.AtWeight(ARKitBlendShapeLocation.BrowDownLeft) = 1.0f;      // Brow group
            sourceData.AtWeight(ARKitBlendShapeLocation.CheekPuff) = 1.0f;         // Cheek group

            ExpressionSystem.UpdateWeights(expressionData, in sourceData, out var adjustedData);

            Assert.AreEqual(0.5f, adjustedData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft), 0.001f);
            Assert.AreEqual(0.7f, adjustedData.AtWeight(ARKitBlendShapeLocation.EyeSquintLeft), 0.001f);
            Assert.AreEqual(0.3f, adjustedData.AtWeight(ARKitBlendShapeLocation.MouthSmileLeft), 0.001f);
            Assert.AreEqual(0.9f, adjustedData.AtWeight(ARKitBlendShapeLocation.BrowDownLeft), 0.001f);
            Assert.AreEqual(0.6f, adjustedData.AtWeight(ARKitBlendShapeLocation.CheekPuff), 0.001f);
        }

        [Test]
        public void UpdateWeights_WithAdjustments()
        {
            var expressionData = new ExpressionData
            {
                name = "TestExpression",
                blinkWeightInfluence = 1f,
                eyesWeightInfluence = 1f,
                mouthWeightInfluence = 1f,
                browWeightInfluence = 1f,
                cheekWeightInfluence = 1f,
                adjustments = new WeightAdjustmentData[]
                {
                    new WeightAdjustmentData
                    {
                        location = ARKitBlendShapeLocation.EyeBlinkLeft,
                        inputValueMin = 0f,
                        inputValueMax = 1f,
                        outputValueMin = 0f,
                        outputValueMax = 0.5f
                    }
                }
            };

            var sourceData = new ARKitWeightData();
            sourceData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft) = 1.0f;

            ExpressionSystem.UpdateWeights(expressionData, in sourceData, out var adjustedData);

            // グループ影響度1.0 * 調整後の値0.5 = 0.5
            Assert.AreEqual(0.5f, adjustedData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft), 0.001f);
        }

        [Test]
        public void UpdateWeights_WithGroupInfluenceAndAdjustments()
        {
            var expressionData = new ExpressionData
            {
                name = "TestExpression",
                blinkWeightInfluence = 0.8f,
                eyesWeightInfluence = 1f,
                mouthWeightInfluence = 1f,
                browWeightInfluence = 1f,
                cheekWeightInfluence = 1f,
                adjustments = new WeightAdjustmentData[]
                {
                    new WeightAdjustmentData
                    {
                        location = ARKitBlendShapeLocation.EyeBlinkLeft,
                        inputValueMin = 0f,
                        inputValueMax = 1f,
                        outputValueMin = 0f,
                        outputValueMax = 0.5f
                    }
                }
            };

            var sourceData = new ARKitWeightData();
            sourceData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft) = 1.0f;

            ExpressionSystem.UpdateWeights(expressionData, in sourceData, out var adjustedData);

            // グループ影響度0.8が先に適用され、その後調整が適用される
            // ソース値 1.0 → グループ影響 0.8 → 調整後 0.4
            Assert.AreEqual(0.4f, adjustedData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft), 0.001f);
        }

        [Test]
        public void UpdateWeights_ClampTo01()
        {
            var expressionData = new ExpressionData
            {
                name = "TestExpression",
                blinkWeightInfluence = 1f,
                eyesWeightInfluence = 1f,
                mouthWeightInfluence = 1f,
                browWeightInfluence = 1f,
                cheekWeightInfluence = 1f,
                adjustments = new WeightAdjustmentData[]
                {
                    new WeightAdjustmentData
                    {
                        location = ARKitBlendShapeLocation.EyeBlinkLeft,
                        inputValueMin = 0f,
                        inputValueMax = 1f,
                        outputValueMin = 0f,
                        outputValueMax = 2f  // 1.0を超える値
                    }
                }
            };

            var sourceData = new ARKitWeightData();
            sourceData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft) = 1.0f;

            ExpressionSystem.UpdateWeights(expressionData, in sourceData, out var adjustedData);

            // 調整後の値が2.0だが、Clamp01で1.0にクランプされる
            Assert.AreEqual(1f, adjustedData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft), 0.001f);
        }

        [Test]
        public void UpdateWeights_ARKitWeightAdjustmentData()
        {
            var adjustmentData = new ARKitWeightAdjustmentData
            {
                adjustments = new WeightAdjustmentData[]
                {
                    new WeightAdjustmentData
                    {
                        location = ARKitBlendShapeLocation.EyeBlinkLeft,
                        inputValueMin = 0f,
                        inputValueMax = 1f,
                        outputValueMin = 0f,
                        outputValueMax = 0.5f
                    }
                }
            };

            var sourceData = new ARKitWeightData();
            sourceData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft) = 1.0f;
            sourceData.AtWeight(ARKitBlendShapeLocation.MouthSmileLeft) = 0.8f;

            ExpressionSystem.UpdateWeights(adjustmentData, in sourceData, out var adjustedData);

            Assert.AreEqual(0.5f, adjustedData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft), 0.001f);
            Assert.AreEqual(0.8f, adjustedData.AtWeight(ARKitBlendShapeLocation.MouthSmileLeft), 0.001f);
        }

        [Test]
        public void UpdateWeights_IsSymmetric_False()
        {
            var adjustmentData = new ARKitWeightAdjustmentData
            {
                adjustments = new WeightAdjustmentData[]
                {
                    new WeightAdjustmentData
                    {
                        location = ARKitBlendShapeLocation.EyeBlinkLeft,
                        isSymmetric = false,
                        inputValueMin = 0f,
                        inputValueMax = 1f,
                        outputValueMin = 0f,
                        outputValueMax = 0.5f
                    }
                }
            };

            var sourceData = new ARKitWeightData();
            sourceData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft) = 1.0f;
            sourceData.AtWeight(ARKitBlendShapeLocation.EyeBlinkRight) = 1.0f;

            ExpressionSystem.UpdateWeights(adjustmentData, in sourceData, out var adjustedData);

            // isSymmetric=falseの場合、左側のみ調整される
            Assert.AreEqual(0.5f, adjustedData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft), 0.001f);
            Assert.AreEqual(1.0f, adjustedData.AtWeight(ARKitBlendShapeLocation.EyeBlinkRight), 0.001f);
        }

        [Test]
        public void UpdateWeights_IsSymmetric_True()
        {
            var adjustmentData = new ARKitWeightAdjustmentData
            {
                adjustments = new WeightAdjustmentData[]
                {
                    new WeightAdjustmentData
                    {
                        location = ARKitBlendShapeLocation.EyeBlinkLeft,
                        isSymmetric = true,
                        inputValueMin = 0f,
                        inputValueMax = 1f,
                        outputValueMin = 0f,
                        outputValueMax = 0.5f
                    }
                }
            };

            var sourceData = new ARKitWeightData();
            sourceData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft) = 1.0f;
            sourceData.AtWeight(ARKitBlendShapeLocation.EyeBlinkRight) = 0.8f;

            ExpressionSystem.UpdateWeights(adjustmentData, in sourceData, out var adjustedData);

            // isSymmetric=trueの場合、左右両方に調整が適用される
            Assert.AreEqual(0.5f, adjustedData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft), 0.001f);
            Assert.AreEqual(0.4f, adjustedData.AtWeight(ARKitBlendShapeLocation.EyeBlinkRight), 0.001f);
        }

        [Test]
        public void UpdateWeights_IsSymmetric_WithExpressionData()
        {
            var expressionData = new ExpressionData
            {
                name = "TestExpression",
                blinkWeightInfluence = 0.8f,
                eyesWeightInfluence = 1f,
                mouthWeightInfluence = 1f,
                browWeightInfluence = 1f,
                cheekWeightInfluence = 1f,
                adjustments = new WeightAdjustmentData[]
                {
                    new WeightAdjustmentData
                    {
                        location = ARKitBlendShapeLocation.EyeBlinkLeft,
                        isSymmetric = true,
                        inputValueMin = 0f,
                        inputValueMax = 1f,
                        outputValueMin = 0f,
                        outputValueMax = 0.5f
                    }
                }
            };

            var sourceData = new ARKitWeightData();
            sourceData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft) = 1.0f;
            sourceData.AtWeight(ARKitBlendShapeLocation.EyeBlinkRight) = 0.5f;

            ExpressionSystem.UpdateWeights(expressionData, in sourceData, out var adjustedData);

            // EyeBlinkはBlinkグループなので、まず影響度0.8が適用される
            // Left: 1.0 * 0.8 = 0.8 → 調整 0.5で 0.4
            // Right: 0.5 * 0.8 = 0.4 → 調整 0.5で 0.2
            Assert.AreEqual(0.4f, adjustedData.AtWeight(ARKitBlendShapeLocation.EyeBlinkLeft), 0.001f);
            Assert.AreEqual(0.2f, adjustedData.AtWeight(ARKitBlendShapeLocation.EyeBlinkRight), 0.001f);
        }

        [Test]
        public void UpdateWeights_IsSymmetric_DifferentValues()
        {
            var adjustmentData = new ARKitWeightAdjustmentData
            {
                adjustments = new WeightAdjustmentData[]
                {
                    new WeightAdjustmentData
                    {
                        location = ARKitBlendShapeLocation.BrowDownLeft,
                        isSymmetric = true,
                        inputValueMin = 0.2f,
                        inputValueMax = 0.8f,
                        outputValueMin = 0.5f,
                        outputValueMax = 1f
                    }
                }
            };

            var sourceData = new ARKitWeightData();
            sourceData.AtWeight(ARKitBlendShapeLocation.BrowDownLeft) = 0.5f;   // (0.5-0.2)/(0.8-0.2) = 0.5 → Lerp(0.5,1,0.5) = 0.75
            sourceData.AtWeight(ARKitBlendShapeLocation.BrowDownRight) = 0.8f;  // (0.8-0.2)/(0.8-0.2) = 1.0 → Lerp(0.5,1,1.0) = 1.0

            ExpressionSystem.UpdateWeights(adjustmentData, in sourceData, out var adjustedData);

            // 左右で異なる入力値でも、それぞれ独立に調整が適用される
            Assert.AreEqual(0.75f, adjustedData.AtWeight(ARKitBlendShapeLocation.BrowDownLeft), 0.001f);
            Assert.AreEqual(1.0f, adjustedData.AtWeight(ARKitBlendShapeLocation.BrowDownRight), 0.001f);
        }

        [Test]
        public void UpdateWeights_IsSymmetric_NoMirrorSide()
        {
            var adjustmentData = new ARKitWeightAdjustmentData
            {
                adjustments = new WeightAdjustmentData[]
                {
                    new WeightAdjustmentData
                    {
                        location = ARKitBlendShapeLocation.TongueOut,
                        isSymmetric = true,  // TongueOutは左右対称でないが、isSymmetric=trueでも問題ない
                        inputValueMin = 0f,
                        inputValueMax = 1f,
                        outputValueMin = 0f,
                        outputValueMax = 0.5f
                    }
                }
            };

            var sourceData = new ARKitWeightData();
            sourceData.AtWeight(ARKitBlendShapeLocation.TongueOut) = 1.0f;

            ExpressionSystem.UpdateWeights(adjustmentData, in sourceData, out var adjustedData);

            // TongueOutは自分自身をミラーとして持つので、同じ値が適用される
            Assert.AreEqual(0.5f, adjustedData.AtWeight(ARKitBlendShapeLocation.TongueOut), 0.001f);
        }
    }
}
