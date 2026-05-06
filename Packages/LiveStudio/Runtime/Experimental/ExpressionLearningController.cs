using System;
using UnityEngine;
using Lilium.LiveStudio.Expression;
using ExpressionDataCore = Lilium.LiveStudio.Expression.ExpressionData;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Expression Learning機能を管理するコンポーネント
    /// ActorProviderから表情学習関連の機能を分離
    /// </summary>
    public class ExpressionLearningController : MonoBehaviour
    {
        [SerializeField]
        private ExpressionDefinition[] _expressionDefinitions;

        private bool _isExpressionLearning = false;

        private ExpressionLearningContext _expressionLearnContext;

        private int _currentExpressionType = 0;

        /// <summary>
        /// 現在の表情タイプを取得
        /// </summary>
        public int CurrentExpressionType => _currentExpressionType;

        /// <summary>
        /// 表情学習を開始する
        /// </summary>
        /// <param name="index">学習する表情のインデックス</param>
        public unsafe void StartExpressionLearning(int index)
        {
            if (_isExpressionLearning)
            {
                StopExpressionLearning();
                return;
            }
            else
            {
                _isExpressionLearning = true;
                _expressionLearnContext = ExpressionLearning.CreateContext();
                _expressionLearnContext.key = (ExpressionKey)index;
            }
        }

        /// <summary>
        /// 表情学習を停止する
        /// </summary>
        /// <param name="finalExpressionData">最終的な表情データ（省略可能）</param>
        public unsafe void StopExpressionLearning(ExpressionDataCore? finalExpressionData = null)
        {
            if (_isExpressionLearning)
            {
                _isExpressionLearning = false;
                var expressionData = finalExpressionData ?? new ExpressionDataCore();
                var definition = ExpressionLearning.Finish(ref _expressionLearnContext, expressionData);
                _expressionDefinitions[(int)_expressionLearnContext.key] = definition;
            }
        }

        /// <summary>
        /// 表情学習の処理を実行する
        /// </summary>
        /// <param name="expressionData">現在の表情データ</param>
        public void ProcessExpressionLearning(in ExpressionDataCore expressionData)
        {
            if (_isExpressionLearning)
            {
                ExpressionLearning.Process(ref _expressionLearnContext, expressionData);
            }
        }


        /// <summary>
        /// 表情をキャプチャする
        /// </summary>
        /// <param name="current">現在の表情データ</param>
        /// <param name="type">表情のタイプ</param>
        public unsafe void CaptureExpressions(ExpressionDataCore current, ExpressionKey type)
        {
            var expressionDefinition = _expressionDefinitions[(int)type];

            expressionDefinition.weights = new float[ExpressionDefinition.kARKitBlendShapeCount];
            for (int i = 0; i < ExpressionDefinition.kARKitBlendShapeCount; i++)
            {
                expressionDefinition.weights[i] = current.weights[i];
            }
        }

        /// <summary>
        /// 表情定義の配列を取得する
        /// </summary>
        public ExpressionDefinition[] GetExpressionDefinitions()
        {
            return _expressionDefinitions;
        }

        /// <summary>
        /// 表情学習中かどうかを取得する
        /// </summary>
        public bool IsExpressionLearning()
        {
            return _isExpressionLearning;
        }

#if false
        /// <summary>
        /// ARKitCaptureFrameDataからExpressionDataを生成する
        /// </summary>
        /// <param name="faceData">顔データ</param>
        /// <returns>表情データ</returns>
        public unsafe ExpressionDataCore CreateExpressionData(in ARKitCaptureFrameData faceData)
        {
            var expressionData = new ExpressionDataCore();

            for (int i = 0; i < ExpressionDefinition.kARKitBlendShapeCount; i++)
            {
                expressionData.weights[i] = faceData.blendShapes[i];
            }

            return expressionData;
        }
#endif

        /// <summary>
        /// 表情データを分析して表情タイプを判定する
        /// </summary>
        /// <param name="expressionData">表情データ</param>
        /// <returns>表情タイプ</returns>
        public int AnalyzeExpression(in ExpressionDataCore expressionData)
        {
            if (_expressionDefinitions == null || _expressionDefinitions.Length == 0)
            {
                return 0;
            }

            _currentExpressionType = ExpressionAnalyzer.AnalyzeExpression(expressionData, _expressionDefinitions, ExpressionDefinition.ExpressionMask);
            return _currentExpressionType;
        }
    }
}
