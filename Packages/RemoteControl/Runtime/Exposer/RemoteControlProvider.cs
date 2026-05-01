using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;
using Lilium.RemoteControl.UI;
using Unity.Collections;

namespace Lilium.RemoteControl
{

    [DefaultExecutionOrder(32760)]
    public class RemoteControlProvider : MonoBehaviour
    {
        /// <summary>
        /// 「名前を付けて保存」ダイアログの既定ディレクトリ (絶対パス)。
        /// 上位アプリ (例: virgo.studio) が起動時に <see cref="SetSaveAsDefaultDirectory"/> で登録する。
        /// 未設定の場合は汎用フォールバック (MyDocuments/Virgo Motion/Saved) が使われる。
        /// </summary>
        private static string _saveAsDefaultDirectoryOverride;

        /// <summary>
        /// 「名前を付けて保存」ダイアログの既定ディレクトリを設定する。
        /// 上位アプリが起動時に一度呼ぶことで、ダイアログがそのフォルダから開くようになる。
        /// </summary>
        public static void SetSaveAsDefaultDirectory(string absolutePath)
        {
            _saveAsDefaultDirectoryOverride = absolutePath;
        }

        public ExposedObjectContainer objectContainer => _objectContainer;

        [SerializeField]
        private ExposedObjectContainer _objectContainer;

        [SerializeField]
        private string _defaultFileName;

        public bool autoSaveOnQuit = true;

        /// <summary>
        /// 現在のシーンファイルパス。相対パスの場合はpersistentDataPath基準。
        /// Load/Saveの度に更新・永続化される。
        /// </summary>
        public string currentFilePath
        {
            get => _currentFilePath;
            set
            {
                _currentFilePath = value;
                PlayerPrefs.SetString(_prefsKey, value ?? "");
            }
        }

        /// <summary>
        /// currentFilePathをフルパスに解決して返す。
        /// </summary>
        public string currentFullPath => _ResolvePath(_currentFilePath);

        /// <summary>
        /// デフォルトのファイル名。
        /// </summary>
        public string defaultFileName => _defaultFileName;

        private string _currentFilePath;
        private string _prefsKey;
        private bool _allowQuit;
        private bool _dialogPending;
        private string _baselineJson;

        private const string kSceneFileExtension = ".scene.json";
        private const string kSceneFileDefaultName = "Untitled.scene.json";
        private const string kSceneFileDefaultSubDir = "Virgo Motion/Saved";

        void Awake()
        {
            // 同じGameObjectから自動取得
            if (_objectContainer == null)
                _objectContainer = GetComponent<ExposedObjectContainer>();

            _prefsKey = "RemoteControl_ScenePath_" + _defaultFileName;
            _currentFilePath = PlayerPrefs.GetString(_prefsKey, _defaultFileName);
        }

        void OnEnable()
        {
            // ContainerのInitialize
            if (_objectContainer != null)
                _objectContainer.Initialize();

            LoadCurrentData();

            RemoteControlService.onResetData += OnResetData;

            // Editor は Play 停止時に wantsToQuit の戻り値が尊重されないため
            // playModeStateChanged を使う。ビルドでは wantsToQuit が確実に機能する。
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#else
            Application.wantsToQuit += _OnWantsToQuit;
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#else
            Application.wantsToQuit -= _OnWantsToQuit;
#endif

            if (_objectContainer != null)
                _objectContainer.Shutdown();

            RemoteControlService.onResetData -= OnResetData;
        }

        private void OnResetData()
        {
            ClearCurrentData();
            // 破棄済みデータを確認ダイアログなしで終了させる
            _allowQuit = true;
            Application.Quit();
        }

        void Start()
        {
        }

        void OnApplicationQuit()
        {
            // 保存は _OnWantsToQuit の経路で行う。ここでは何もしない。
        }

        void LateUpdate()
        {
            if (_objectContainer != null)
                _objectContainer.UpdateObjects();
        }


        public void LoadCurrentData()
        {
            var fullPath = currentFullPath;
            // 前回のパスが保存されていてファイルが存在すればそちらを使用
            if (_currentFilePath != _defaultFileName && System.IO.File.Exists(fullPath))
            {
                _LoadFrom(fullPath);
                return;
            }
            // デフォルトにフォールバック
            currentFilePath = _defaultFileName;
            _LoadFrom(_ResolvePath(_defaultFileName));
        }

        public void LoadCurrentDataFrom(string filePath)
        {
            if (_objectContainer == null) return;

            currentFilePath = filePath;
            _LoadFrom(_ResolvePath(filePath));
        }

        public void SaveCurrentData()
        {
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                Debug.LogWarning("[RemoteControl] SaveCurrentData: current file path is empty. Use SaveCurrentDataTo or set currentFilePath first.");
                return;
            }
            SaveCurrentDataTo(_currentFilePath);
        }

        public void SaveCurrentDataTo(string filePath)
        {
            if (_objectContainer == null) return;
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogWarning("[RemoteControl] SaveCurrentDataTo: empty file path.");
                return;
            }

            currentFilePath = filePath;

            var fullPath = _ResolvePath(filePath);
            var dir = System.IO.Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            var json = ExposedSceneSerializer.BuildSceneJson(_objectContainer);
            System.IO.File.WriteAllText(fullPath, json);
            _baselineJson = json;
        }

        /// <summary>
        /// 現在のシーン状態とロード/セーブ直後にキャッシュされた基準JSONを比較し、
        /// 未保存の変更があればtrueを返す。
        /// </summary>
        public bool HasUnsavedChanges()
            => ExposedSceneSerializer.HasChanges(_objectContainer, _baselineJson);

        private string _GetSaveAsDefaultName()
        {
            if (!string.IsNullOrEmpty(_defaultFileName)) return _defaultFileName;
            return kSceneFileDefaultName;
        }

        /// <summary>
        /// 「名前を付けて保存」ダイアログの既定ディレクトリ。RemoteApp(Tauri) の save_scene_file と揃える。
        /// 上位アプリが <see cref="SetSaveAsDefaultDirectory"/> で登録していればその値を採用、
        /// そうでなければ汎用フォールバック (MyDocuments/Virgo Motion/Saved) を返す。
        /// 存在しなければ作成する。
        /// </summary>
        private static string _GetSaveAsDefaultDirectory()
        {
            string dir;
            string fallback;
            if (!string.IsNullOrEmpty(_saveAsDefaultDirectoryOverride))
            {
                dir = _saveAsDefaultDirectoryOverride;
                fallback = _saveAsDefaultDirectoryOverride;
            }
            else
            {
                var docs = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(docs))
                {
                    return Application.persistentDataPath;
                }
                dir = System.IO.Path.Combine(docs, kSceneFileDefaultSubDir);
                fallback = docs;
            }

            try
            {
                if (!System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[RemoteControl] Failed to create default save directory '{dir}': {ex.Message}");
                return fallback;
            }
            return dir;
        }

#if !UNITY_EDITOR
        private bool _OnWantsToQuit()
        {
            bool hasUnsaved = HasUnsavedChanges();
            Debug.Log($"[Debug][RemoteControl] wantsToQuit: allowQuit={_allowQuit} " +
                      $"hasUnsaved={hasUnsaved} autoSave={autoSaveOnQuit} dialogPending={_dialogPending}");

            if (_allowQuit)
            {
                Debug.Log("[Debug][RemoteControl] wantsToQuit -> true (allowQuit)");
                return true;
            }
            if (!hasUnsaved)
            {
                Debug.Log("[Debug][RemoteControl] wantsToQuit -> true (no unsaved changes)");
                return true;
            }

            if (autoSaveOnQuit)
            {
                // 有効時は確認ダイアログなしで強制上書き保存して終了を続行
                SaveCurrentData();
                _allowQuit = true;
                Debug.Log("[Debug][RemoteControl] wantsToQuit -> true (autoSave)");
                return true;
            }

            // wantsToQuit 内で MessageBox を表示するとUnity内部の終了処理が並行で進むため
            // ダイアログが即座に閉じられてしまう。ここではfalseを返して終了をキャンセルし、
            // 次フレームのコルーチンでダイアログを表示→ユーザー応答後にApplication.Quit()を再呼び出しする。
            if (!_dialogPending)
            {
                _dialogPending = true;
                StartCoroutine(_ShowDialogAndQuit());
            }
            Debug.Log("[Debug][RemoteControl] wantsToQuit -> false (dialog pending)");
            return false;
        }

        private IEnumerator _ShowDialogAndQuit()
        {
            // wantsToQuit のコンテキストから抜けるため1フレーム待つ
            yield return null;

            string title = LocalizationSystem.Translate("DIALOG_UNSAVED_CHANGES_TITLE");
            string message = LocalizationSystem.Translate("DIALOG_UNSAVED_CHANGES_MESSAGE");
            string okLabel = LocalizationSystem.Translate("DIALOG_OK");
            string cancelLabel = LocalizationSystem.Translate("DIALOG_CANCEL");

            bool save = ConfirmDialog.Show(title, message, okLabel, cancelLabel);
            Debug.Log($"[Debug][RemoteControl] dialog answered: save={save}, calling Application.Quit() again");
            if (save && !_TrySaveOrPrompt())
            {
                // 名前を付けて保存ダイアログがキャンセルされた場合は終了もキャンセル
                _dialogPending = false;
                yield break;
            }
            _allowQuit = true;
            Application.Quit();
        }

        /// <summary>
        /// 現在のシーンを保存する。パス未設定なら「名前を付けて保存」ダイアログを表示する。
        /// ダイアログがキャンセルされた場合は false を返す。
        /// </summary>
        private bool _TrySaveOrPrompt()
        {
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                SaveCurrentData();
                return true;
            }

            string savePath = SaveFileDialog.Show(
                title: LocalizationSystem.Translate("DIALOG_SAVE_AS_TITLE"),
                initialDirectory: _GetSaveAsDefaultDirectory(),
                defaultFileName: _GetSaveAsDefaultName(),
                extension: kSceneFileExtension);

            if (string.IsNullOrEmpty(savePath))
            {
                Debug.Log("[Debug][RemoteControl] Save As dialog cancelled");
                return false;
            }

            SaveCurrentDataTo(savePath);
            return true;
        }
#endif

        /// <summary>
        /// 全ExposedObjectのdirtyプロパティをデフォルト値に戻す。
        /// エディタPlay終了時にScriptableObject等の状態を復元するために使用。
        /// </summary>
        public void RevertAllToDefault()
        {
            if (_objectContainer == null) return;

            var objects = ExposedSceneSerializer.ResolveExposedObjects(
                _objectContainer.objects, _objectContainer);

            foreach (var obj in objects)
            {
                var dirtyProps = obj.GetDirtyProperties();
                if (dirtyProps.Count == 0) continue;

                // デフォルトJSONを取得してFromJsonで再適用する。
                // Revert(path)はSetValue経由で動作するため、read-onlyコンポーネント配列内の
                // ScriptableObject値をrevertできない。FromJsonはDeserializeExposedObject内の
                // SetValueRawで直接フィールドを変更するため、read-onlyを経由しても動作する。
                var defaultJson = ExposedObjectDefaultRegistry.GetDefaults(obj);
                if (defaultJson != null)
                {
                    ExposedPropertySerializer.FromJson(
                        defaultJson.ToString(), obj, _objectContainer, captureDefaults: false);
                }
            }
        }

#if UNITY_EDITOR
        private void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state != UnityEditor.PlayModeStateChange.ExitingPlayMode) return;
            if (_allowQuit) return;
            if (!HasUnsavedChanges()) return;

            _allowQuit = true;

            if (autoSaveOnQuit)
            {
                // 有効時は確認ダイアログなしで強制上書き保存
                SaveCurrentData();
                return;
            }

            // ExitingPlayMode は同期でハンドラが実行されるため DisplayDialog が確実にブロックする。
            bool save = UnityEditor.EditorUtility.DisplayDialog(
                LocalizationSystem.Translate("DIALOG_UNSAVED_CHANGES_TITLE"),
                LocalizationSystem.Translate("DIALOG_UNSAVED_CHANGES_MESSAGE"),
                LocalizationSystem.Translate("DIALOG_OK"),
                LocalizationSystem.Translate("DIALOG_CANCEL"));
            if (save) _EditorTrySaveOrPrompt();
        }

        private void _EditorTrySaveOrPrompt()
        {
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                SaveCurrentData();
                return;
            }

            // パス未設定なら Editor の名前を付けて保存パネルを表示する
            string savePath = UnityEditor.EditorUtility.SaveFilePanel(
                LocalizationSystem.Translate("DIALOG_SAVE_AS_TITLE"),
                _GetSaveAsDefaultDirectory(),
                _GetSaveAsDefaultName(),
                kSceneFileExtension.TrimStart('.'));
            if (string.IsNullOrEmpty(savePath))
            {
                Debug.Log("[Debug][RemoteControl] Save As dialog cancelled (Editor)");
                return;
            }
            // EditorUtility.SaveFilePanel は単一拡張子しか補完しないため、複合拡張子を手動で補う
            if (!savePath.EndsWith(kSceneFileExtension, System.StringComparison.OrdinalIgnoreCase))
            {
                savePath += kSceneFileExtension;
            }
            SaveCurrentDataTo(savePath);
        }
#endif

        public void ClearCurrentData()
        {
            var fullPath = _ResolvePath(_currentFilePath);
            _currentFilePath = _defaultFileName;
            PlayerPrefs.DeleteKey(_prefsKey);
            _baselineJson = null;

            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }

        /// <summary>
        /// 相対パスならpersistentDataPath基準で解決、絶対パスならそのまま返す。
        /// </summary>
        private string _ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return System.IO.Path.Combine(Application.persistentDataPath, _defaultFileName);

            if (System.IO.Path.IsPathRooted(path))
                return path;

            return System.IO.Path.Combine(Application.persistentDataPath, path);
        }

        private void _LoadFrom(string fullPath)
        {
            if (_objectContainer == null) return;

            if (System.IO.File.Exists(fullPath))
            {
                var json = System.IO.File.ReadAllText(fullPath);
                ExposedSceneSerializer.SceneFromJson(json, _objectContainer);
            }

            // ロード直後の状態を基準JSONとしてキャッシュ（HasUnsavedChanges用）
            _baselineJson = ExposedSceneSerializer.BuildSceneJson(_objectContainer);
        }
    }
}
