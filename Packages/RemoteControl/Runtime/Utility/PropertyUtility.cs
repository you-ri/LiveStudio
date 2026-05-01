using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Lilium.RemoteControl
{
    public static class PropertyUtility
    {
        /// <summary>
        /// 値が変化したことをEditorに通知する
        /// </summary>
        public static void Apply<T>(T value) where T : Object
        {
            if (value == null) return;
#if UNITY_EDITOR
            // Editorモードでは変更を記録してUndo機能をサポート
            if (!EditorApplication.isPlaying)
            {
                Undo.RecordObject(value, "Property Changed");
            }
            EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        /// <summary>
        /// EditorのPlayerLoopを更新する（Undo記録なし）
        /// </summary>
        public static void Apply()
        {
#if UNITY_EDITOR
            EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        public static int EditorMainThreadId { get; private set; }
        public static int PlayModeMainThreadId { get; private set; }

        static PropertyUtility()
        {
            // エディタ起動時またはスクリプト再コンパイル後に呼ばれる (エディタのメインスレッド上)  
            EditorMainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

#if UNITY_EDITOR
            // Play モード起動時もキャプチャできるようフック
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

#if UNITY_EDITOR
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                // プレイモードへの遷移直後（メインスレッド上）で記録
                PlayModeMainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            }
        }
#endif

        public static bool IsMainThread()
        {
            var currentThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            return currentThreadId == EditorMainThreadId || currentThreadId == PlayModeMainThreadId;
        }

        public static bool IsOnEditorMainThread()
        {
            return System.Threading.Thread.CurrentThread.ManagedThreadId == EditorMainThreadId;
        }

        public static bool IsOnPlayModeMainThread()
        {
            return System.Threading.Thread.CurrentThread.ManagedThreadId == PlayModeMainThreadId;
        }
    }
}
