using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Lilium.LiveStudio
{
    public enum PathType
    {
        ProjectRelative,  // プロジェクトルート相対
        PackageRelative   // このスクリプトが所属するパッケージのルートからの相対（Editor時のみ有効）
    }

    public class ToolAppLauncher : MonoBehaviour
    {
        [Header("Application Settings")]
        [SerializeField] private PathType pathType = PathType.ProjectRelative;
        [SerializeField] private string applicationPath = "";

        [Tooltip("PackageRelative 時に Tools~ を解決するパッケージ名 (例: jp.lilium.livestudio)。空なら ToolAppLauncher 自身が属するパッケージを使う。")]
        [SerializeField] private string packageName = "";

        [SerializeField] private string arguments = "";
        [SerializeField] private bool hideWindow = false;

        [SerializeField]
        private bool _autoClose = true;

        private Process _childProcess;

        [SerializeField]
        private bool _applicationOnly = true;

        private bool _editorHookRegistered;

        /// <summary>
        /// ツールアプリケーションのフルパスを解決します
        /// </summary>
        /// <param name="pathType">パスの種類</param>
        /// <param name="applicationPath">アプリケーションの相対パス</param>
        /// <param name="packageName">PackageRelative 時に Tools~ を解決するパッケージ名 (例: jp.lilium.livestudio)。空なら ToolAppLauncher 自身が属するパッケージを使う。</param>
        /// <param name="packageRootPath">パッケージのルートパス。指定があれば packageName より優先。</param>
        /// <returns>解決されたフルパス</returns>
        public static string ResolveToolApplicationPath(PathType pathType, string applicationPath, string packageName = null, string packageRootPath = null)
        {
#if UNITY_EDITOR
            if (pathType == PathType.PackageRelative && string.IsNullOrEmpty(packageRootPath))
            {
                if (!string.IsNullOrEmpty(packageName))
                {
                    var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName(packageName);
                    if (packageInfo != null)
                    {
                        packageRootPath = packageInfo.resolvedPath;
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"[Studio] Package not found: {packageName}. Falling back to ProjectRelative.");
                    }
                }
                else
                {
                    // このスクリプトが所属するパッケージのルートパスを取得
                    var guids = UnityEditor.AssetDatabase.FindAssets($"t:MonoScript {nameof(ToolAppLauncher)}");
                    if (guids.Length > 0)
                    {
                        var scriptPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(scriptPath);

                        if (packageInfo != null)
                        {
                            packageRootPath = packageInfo.resolvedPath;
                        }
                        else
                        {
                            UnityEngine.Debug.LogWarning("[Studio] Package information not found. Falling back to ProjectRelative.");
                        }
                    }
                }
            }
#endif



            if (string.IsNullOrEmpty(applicationPath))
            {
                return "";
            }

            if (pathType == PathType.PackageRelative && !string.IsNullOrEmpty(packageRootPath))
            {
                return Path.GetFullPath(Path.Join(packageRootPath, "./Tools~/", applicationPath));
            }
            else
            {
                // ProjectRelative or ビルドアプリ時
                return Path.GetFullPath(Path.Join(Application.dataPath, "../Tools/", applicationPath));
            }
        }

        private void Awake()
        {
        }

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
#if UNITY_EDITOR
            if (_applicationOnly)
            {
                return;
            }
#endif
            StartChildApplication();
        }

        private void _RegisterEditorHook()
        {
#if UNITY_EDITOR
            if (!_editorHookRegistered)
            {
                UnityEditor.EditorApplication.playModeStateChanged += _OnPlayModeStateChanged;
                _editorHookRegistered = true;
            }
#endif
        }

        private void _UnregisterEditorHook()
        {
#if UNITY_EDITOR
            if (_editorHookRegistered)
            {
                UnityEditor.EditorApplication.playModeStateChanged -= _OnPlayModeStateChanged;
                _editorHookRegistered = false;
            }
#endif
        }

#if UNITY_EDITOR
        private void _OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                if (_autoClose)
                {
                    StopChildApplication();
                }
                _UnregisterEditorHook();
            }
        }
#endif

        /// <summary>
        /// 子アプリケーションを起動します
        /// </summary>
        public void StartChildApplication()
        {
            string applicationFullpath = ResolveToolApplicationPath(pathType, applicationPath, packageName);

            if (string.IsNullOrEmpty(applicationFullpath))
            {
                UnityEngine.Debug.LogWarning("[Studio] Application path is not set.");
                return;
            }

            if (_childProcess != null && !_childProcess.HasExited)
            {
                UnityEngine.Debug.LogWarning("[Studio] Child application is already running.");
                return;
            }

            // ダウンロードしたファイルの MOTW を除去して SmartScreen ブロックを回避
            WindowsFileUnblock.UnblockFile(applicationFullpath);
            string appDir = Path.GetDirectoryName(applicationFullpath);
            if (!string.IsNullOrEmpty(appDir))
                WindowsFileUnblock.UnblockDirectory(appDir);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = applicationFullpath,
                Arguments = arguments,
                UseShellExecute = true,
                WindowStyle = hideWindow ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
            };

            _childProcess = Process.Start(startInfo);

            if (_childProcess == null)
            {
                UnityEngine.Debug.LogError($"[Studio] Failed to start child application. path:{applicationFullpath}");
            }
            else
            {
                _RegisterEditorHook();
            }
        }

        /// <summary>
        /// 子アプリケーションを停止します
        /// </summary>
        public void StopChildApplication()
        {
            if (_childProcess == null || _childProcess.HasExited)
            {
                _childProcess?.Dispose();
                _childProcess = null;
                return;
            }

            try
            {
                // CloseMainWindow を試行
                if (_childProcess.CloseMainWindow())
                {
                    if (_childProcess.WaitForExit(5000))
                    {
                        UnityEngine.Debug.Log("[Studio] Child application terminated via CloseMainWindow.");
                        return;
                    }
                }

                // 強制終了
                _childProcess.Kill();
                UnityEngine.Debug.LogWarning("[Studio] Child application was forcibly terminated.");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[Studio] Error stopping child application: {ex.Message}");
            }
            finally
            {
                _childProcess?.Dispose();
                _childProcess = null;
            }
        }

        /// <summary>
        /// 子アプリケーションが実行中かどうかを確認します
        /// </summary>
        public bool IsChildApplicationRunning()
        {
            return _childProcess != null && !_childProcess.HasExited;
        }

        /// <summary>
        /// アプリケーションパスを設定します
        /// </summary>
        public void SetApplicationPath(string path)
        {
            applicationPath = path;
        }

        /// <summary>
        /// 引数を設定します
        /// </summary>
        public void SetArguments(string args)
        {
            arguments = args;
        }

        private void OnDestroy()
        {
            _UnregisterEditorHook();
            if (_autoClose)
            {
                StopChildApplication();
            }
        }

        private void OnApplicationQuit()
        {
            if (_autoClose)
            {
                StopChildApplication();
            }
        }
    }
}
