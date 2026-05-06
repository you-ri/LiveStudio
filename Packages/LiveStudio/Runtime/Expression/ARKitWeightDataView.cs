using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// ARKitWeightDataの各ウェイトをゲージ表示するビュー（純粋なC#クラス）
    /// OnGUIを使用した描画で実装
    /// </summary>
    public class ARKitWeightDataView
    {
        public Vector2 startPosition = new Vector2(10, 10);
        public float gaugeWidth = 150*1.5f;
        public float gaugeHeight = 15f*1.5f;
        public float gaugeSpacing = 3f*1.5f;
        public float labelWidth = 120f*1.5f;
        public int columnCount = 2;
        public float columnSpacing = 20f*1.5f;

        public Color gaugeBackgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        public Color gaugeFillColor = new Color(0.1f, 0.7f, 0.1f, 0.9f);
        public Color labelColor = Color.white;
        public Color valueColor = new Color(0.8f, 0.8f, 0.8f);

        public bool showZeroValues = true;
        public bool showNumericValues = true;

        private Texture2D _whiteTexture;
        private GUIStyle _labelStyle;
        private GUIStyle _valueStyle;

        private ARKitWeightData _weightData = new ARKitWeightData();

        public ARKitWeightDataView()
        {
        }

        void _CreateResource()
        {
            _whiteTexture = new Texture2D(1, 1);
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply();
        }

        /// <summary>
        /// ARKitWeightDataを設定
        /// </summary>
        public unsafe void SetData(in ARKitWeightData weightData)
        {
            fixed (float* src = weightData.weights)
            fixed (float* dst = _weightData.weights)
            {
                Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemCpy(dst, src, sizeof(float) * (int)ARKitBlendShapeLocation.Max);
            }
        }

        /// <summary>
        /// ゲージを描画（OnGUIから呼び出す）
        /// </summary>
        public unsafe void Draw()
        {
            if (_whiteTexture == null)
            {
                _CreateResource();
                if (_whiteTexture == null) return;
            }

            _InitializeStyles();

            int maxIndex = (int)ARKitBlendShapeLocation.Max;
            int itemsPerColumn = Mathf.CeilToInt((float)maxIndex / columnCount);
            float singleColumnWidth = gaugeWidth;

            for (int i = 0; i < maxIndex; i++)
            {
                int column = i / itemsPerColumn;
                int row = i % itemsPerColumn;

                if (column >= columnCount) break;

                float value = _weightData.weights[i];

                // ゼロ値をスキップする設定の場合
                if (!showZeroValues && Mathf.Approximately(value, 0f)) continue;

                float x = startPosition.x + column * (singleColumnWidth + columnSpacing);
                float y = startPosition.y + row * (gaugeHeight + gaugeSpacing);

                // ゲージ背景
                _DrawRect(
                    new Rect(x, y, gaugeWidth, gaugeHeight),
                    gaugeBackgroundColor
                );

                // ゲージ前景（値に応じた幅）
                if (value > 0.001f)
                {
                    _DrawRect(
                        new Rect(x, y, gaugeWidth * Mathf.Clamp01(value), gaugeHeight),
                        gaugeFillColor
                    );
                }

                // ラベル名
                string label = ((ARKitBlendShapeLocation)i).ToString();
                GUI.Label(new Rect(x+5, y, gaugeWidth - 5f, gaugeHeight), label, _labelStyle);

                // 数値表示
                if (showNumericValues)
                {
                    string valueText = value.ToString("F2");
                    float valueTextWidth = 35f;
                    GUI.Label(
                        new Rect(x + gaugeWidth -50f, y, valueTextWidth, gaugeHeight),
                        valueText,
                        _valueStyle
                    );
                }
            }
        }

        void _DrawRect(Rect rect, Color color)
        {
            Color oldColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _whiteTexture);
            GUI.color = oldColor;
        }

        void _InitializeStyles()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.normal.textColor = labelColor;
                _labelStyle.fontSize = 14;
                _labelStyle.alignment = TextAnchor.MiddleLeft;
            }

            if (_valueStyle == null)
            {
                _valueStyle = new GUIStyle(GUI.skin.label);
                _valueStyle.normal.textColor = valueColor;
                _valueStyle.fontSize = 14;
                _valueStyle.alignment = TextAnchor.MiddleRight;
            }
        }

        /// <summary>
        /// リソースを解放
        /// </summary>
        public void Dispose()
        {
            if (_whiteTexture != null)
            {
                Object.Destroy(_whiteTexture);
                _whiteTexture = null;
            }
        }
    }
}
