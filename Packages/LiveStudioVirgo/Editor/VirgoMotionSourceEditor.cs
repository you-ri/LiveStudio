// Copyright (c) You-Ri, 2026

using UnityEngine;
using UnityEditor;

namespace Lilium.LiveStudio.Virgo.Editor
{
    /// <summary>
    /// VirgoMotionSource のカスタムインスペクタ。
    /// UDP でモーションデータが届いているかを示す受信インジケータを表示する。
    /// </summary>
    [CustomEditor(typeof(VirgoMotionSource))]
    public class VirgoMotionSourceEditor : UnityEditor.Editor
    {
        // Repaint をこの間隔(秒)に制限してエディタ負荷を抑える。
        private const double kRepaintInterval = 0.1;

        // 最後の受信からこの秒数を超えたら「受信中」ではなく「待機中」とみなす。
        private const double kReceiveTimeout = 0.5;

        // 受信レート(fps)の指数移動平均の係数。
        private const float kFpsSmoothing = 0.2f;

        private VirgoMotionSource _target;

        private int _lastFrameCount;
        private double _lastChangeTime;
        private double _lastSampleTime;
        private double _lastRepaintTime;
        private float _receiveFps;

        private static readonly Color kColorReceiving = new Color(0.30f, 0.80f, 0.30f);
        private static readonly Color kColorWaiting = new Color(0.85f, 0.70f, 0.20f);
        private static readonly Color kColorClosed = new Color(0.45f, 0.45f, 0.45f);

        void OnEnable()
        {
            _target = (VirgoMotionSource)target;
            _lastFrameCount = _target.receivedFrameCount;
            _lastSampleTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += _OnEditorUpdate;
        }

        void OnDisable()
        {
            EditorApplication.update -= _OnEditorUpdate;
        }

        private void _OnEditorUpdate()
        {
            if (_target == null) return;

            double now = EditorApplication.timeSinceStartup;
            int current = _target.receivedFrameCount;

            if (current != _lastFrameCount)
            {
                double delta = now - _lastSampleTime;
                if (delta > 0.0)
                {
                    float instantFps = (float)((current - _lastFrameCount) / delta);
                    _receiveFps = Mathf.Lerp(_receiveFps, instantFps, kFpsSmoothing);
                }

                _lastFrameCount = current;
                _lastSampleTime = now;
                _lastChangeTime = now;
            }
            else if (now - _lastChangeTime >= kReceiveTimeout)
            {
                // 受信が途絶えたらレート表示を 0 に収束させる。
                _receiveFps = 0f;
            }

            if (now - _lastRepaintTime >= kRepaintInterval)
            {
                _lastRepaintTime = now;
                Repaint();
            }
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();

            if (_target == null) return;

            _DrawStatusIndicator();
        }

        private void _DrawStatusIndicator()
        {
            bool isOpened = _target.isOpened;
            bool isReceiving = isOpened && (EditorApplication.timeSinceStartup - _lastChangeTime) < kReceiveTimeout;

            Color lampColor;
            string statusLabel;
            if (isReceiving)
            {
                lampColor = kColorReceiving;
                statusLabel = "Receiving";
            }
            else if (isOpened)
            {
                lampColor = kColorWaiting;
                statusLabel = "Waiting for data";
            }
            else
            {
                lampColor = kColorClosed;
                statusLabel = "Not listening";
            }

            // 状態ランプ + ラベル + 詳細を 1 行に並べる
            EditorGUILayout.BeginHorizontal();
            Rect lampRect = GUILayoutUtility.GetRect(14f, 14f, GUILayout.Width(14f), GUILayout.Height(14f));
            lampRect.y += 5f;
            lampRect.width = 12f;
            lampRect.height = 12f;
            EditorGUI.DrawRect(lampRect, lampColor);
            EditorGUILayout.LabelField(statusLabel, EditorStyles.label, GUILayout.Width(110f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"{_target.receivedFrameCount} frames", EditorStyles.label, GUILayout.Width(110f));
            EditorGUILayout.LabelField($"{_receiveFps:F1} fps", EditorStyles.label, GUILayout.Width(70f));
            EditorGUILayout.EndHorizontal();
        }
    }
}
