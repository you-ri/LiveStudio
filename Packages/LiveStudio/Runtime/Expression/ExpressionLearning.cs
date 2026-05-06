using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.LiveStudio.Expression
{


    [System.Serializable]
    public struct ExpressionLearningContext
    {
        public ExpressionKey key;

        public ExpressionData max;

        public ExpressionData min;
    }



    public static class ExpressionLearning
    {
        public static unsafe ExpressionLearningContext CreateContext()
        {
            ExpressionLearningContext context = new ExpressionLearningContext();
            for (int i = 0; i < ExpressionDefinition.kARKitBlendShapeCount; i++)
            {
                context.max.weights[i] = 0;
                context.min.weights[i] = 1;
            }
            return context;
        }


        public static unsafe void Process(ref ExpressionLearningContext context, in ExpressionData current)
        {
             for (int i = 0; i < ExpressionDefinition.kARKitBlendShapeCount; i++)
            {
                context.max.weights[i] = Mathf.Max(context.max.weights[i], current.weights[i]);
                context.min.weights[i] = Mathf.Min(context.min.weights[i], current.weights[i]);
            }
        }

        public static unsafe ExpressionDefinition Finish(ref ExpressionLearningContext context, in ExpressionData current)
        {
            var definition = new ExpressionDefinition();
            definition.weights = new float[ExpressionDefinition.kARKitBlendShapeCount];
            definition.factor = new float[ExpressionDefinition.kARKitBlendShapeCount];
            for (int i = 0; i < ExpressionDefinition.kARKitBlendShapeCount; i++)
            {
                definition.weights[i] = (context.max.weights[i] + context.min.weights[i]) / 2;
                definition.factor[i] = 1 - (context.max.weights[i] - context.min.weights[i]);
            }
            return definition;
        }


    }


}

