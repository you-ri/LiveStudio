#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace Lilium.LiveStudio
{
    [DefaultExecutionOrder(-99999)]
    public class SceneLoader : MonoBehaviour
    {
#if UNITY_EDITOR
        [SerializeField]
        private SceneAsset _scene;
#endif

        [SerializeField, HideInInspector]
        private string _scenePath;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_scene != null)
            {
                _scenePath = AssetDatabase.GetAssetPath(_scene);
            }
            else
            {
                _scenePath = string.Empty;
            }
        }
#endif

        void Awake()
        {
            if (!string.IsNullOrEmpty(_scenePath) && isActiveAndEnabled)
            {
                // シーンパスからシーン名を取得
                var sceneName = System.IO.Path.GetFileNameWithoutExtension(_scenePath);

                // すでにロードされているかチェック
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName);
                if (!scene.isLoaded)
                {
                    UnityEngine.SceneManagement.SceneManager.LoadScene(_scenePath, UnityEngine.SceneManagement.LoadSceneMode.Additive);
                }
            }
        }
    }
}
