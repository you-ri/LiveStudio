using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.LiveStudio.Expression
{
    public enum ExpressionKey
    {
        Neutral,
        Joy,
        Angry,
        Disgust,
        Fear,
        Sad,
        Surprise,
        Count
    }

    [System.Serializable]
    public unsafe struct ExpressionData
    {

        public fixed float weights[ExpressionDefinition.kARKitBlendShapeCount];

    }



    [System.Serializable]
    public class ExpressionDefinition
    {
        public const int kARKitBlendShapeCount = 52;

        public ExpressionKey expressionType;

        public float[] weights = new float[ExpressionDefinition.kARKitBlendShapeCount];

        public float[] factor = new  float[0];

        public static readonly int[] ExpressionMask = {
            (int)ARKitBlendShapeLocation.BrowDownLeft,
            (int)ARKitBlendShapeLocation.BrowDownRight,
            (int)ARKitBlendShapeLocation.BrowInnerUp,
            (int)ARKitBlendShapeLocation.BrowOuterUpLeft,
            (int)ARKitBlendShapeLocation.BrowOuterUpRight,
            (int)ARKitBlendShapeLocation.CheekPuff,
            (int)ARKitBlendShapeLocation.CheekSquintLeft,
            (int)ARKitBlendShapeLocation.CheekSquintRight,
            //(int)ARKitBlendShapeLocation.EyeBlinkLeft,
            //(int)ARKitBlendShapeLocation.EyeBlinkRight,
            //(int)ARKitBlendShapeLocation.EyeLookDownLeft,
            //(int)ARKitBlendShapeLocation.EyeLookDownRight,
            //(int)ARKitBlendShapeLocation.EyeLookInLeft,
            //(int)ARKitBlendShapeLocation.EyeLookInRight,
            //(int)ARKitBlendShapeLocation.EyeLookOutLeft,
            //(int)ARKitBlendShapeLocation.EyeLookOutRight,
            //(int)ARKitBlendShapeLocation.EyeLookUpLeft,
            //(int)ARKitBlendShapeLocation.EyeLookUpRight,
            (int)ARKitBlendShapeLocation.EyeSquintLeft,
            (int)ARKitBlendShapeLocation.EyeSquintRight,
            (int)ARKitBlendShapeLocation.EyeWideLeft,
            (int)ARKitBlendShapeLocation.EyeWideRight,
            (int)ARKitBlendShapeLocation.JawForward,
            (int)ARKitBlendShapeLocation.JawLeft,
            (int)ARKitBlendShapeLocation.JawOpen,
            (int)ARKitBlendShapeLocation.JawRight,
            (int)ARKitBlendShapeLocation.MouthClose,
            (int)ARKitBlendShapeLocation.MouthDimpleLeft,
            (int)ARKitBlendShapeLocation.MouthDimpleRight,
            (int)ARKitBlendShapeLocation.MouthFrownLeft,
            (int)ARKitBlendShapeLocation.MouthFrownRight,
            (int)ARKitBlendShapeLocation.MouthFunnel,
            (int)ARKitBlendShapeLocation.MouthLeft,
            (int)ARKitBlendShapeLocation.MouthLowerDownLeft,
            (int)ARKitBlendShapeLocation.MouthLowerDownRight,
            (int)ARKitBlendShapeLocation.MouthPressLeft,
            (int)ARKitBlendShapeLocation.MouthPressRight,
            (int)ARKitBlendShapeLocation.MouthPucker,
            (int)ARKitBlendShapeLocation.MouthRight,
            (int)ARKitBlendShapeLocation.MouthRollLower,
            (int)ARKitBlendShapeLocation.MouthRollUpper,
            (int)ARKitBlendShapeLocation.MouthShrugLower,
            (int)ARKitBlendShapeLocation.MouthShrugUpper,
            (int)ARKitBlendShapeLocation.MouthSmileLeft,
            (int)ARKitBlendShapeLocation.MouthSmileRight,
            (int)ARKitBlendShapeLocation.MouthStretchLeft,
            (int)ARKitBlendShapeLocation.MouthStretchRight,
            (int)ARKitBlendShapeLocation.MouthUpperUpLeft,
            (int)ARKitBlendShapeLocation.MouthUpperUpRight,
            (int)ARKitBlendShapeLocation.NoseSneerLeft,
            (int)ARKitBlendShapeLocation.NoseSneerRight,
            (int)ARKitBlendShapeLocation.TongueOut
        };

    }



    public static class ExpressionAnalyzer
    {


        public static unsafe int AnalyzeExpression(in ExpressionData current, ExpressionDefinition[] expressionDefinitions, int[] indecies)
        {
            int nearExpressionIndex = 0;
            float maxScore = 0;

            for (int i = 0; i < expressionDefinitions.Length; i++)
            {
                var expressionDefinition = expressionDefinitions[i];
                if (expressionDefinition.factor.Length == 0)
                {
                    continue;
                }

                float score = 0;
                int count = 0;
                for (int j = 0; j < indecies.Length; j++)
                {
                    var index = indecies[j];
                    score += (1 - Mathf.Abs(current.weights[index] - expressionDefinition.weights[index])) * expressionDefinition.factor[index];
                    count ++;
                }
                score /= count;

                if (score > maxScore)
                {
                    maxScore = score;
                    nearExpressionIndex = i;
                }

                //Debug.Log("index:" + i + " score: " + score );
            }

            return (int)expressionDefinitions[(int)nearExpressionIndex].expressionType;


        }




    }



}

