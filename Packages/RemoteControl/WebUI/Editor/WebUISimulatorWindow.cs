// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl.WebUI.Editor
{
    public class WebUISimulatorWindow : EditorWindow
    {
        [SerializeField] private WebUIDefinition _definition;
        [SerializeField] private GameObject _providerObject;
        private string _selectedMenuItemId;
        private VisualElement _sideMenuMain;
        private VisualElement _sideMenuBottom;
        private VisualElement _contentArea;
        private VisualElement _objectList;
        private VisualElement _propertyArea;
        private ObjectField _providerField;
        private ObjectField _definitionField;

        private ExposedObject _selectedObject;
        private ScrollView _propertyScrollView;
        private MenuItem _selectedMenuItem;

        private const float kSideMenuWidth = 48f;
        private const float kObjectListWidth = 160f;
        private const float kPropertyNameWidth = 160f;

        private int _dirtyCount;
        private bool _isUpdatingUI;
        private bool _suppressRebuild;

        private Process _remoteAppProcess;
        private Button _remoteAppButton;

        [SerializeField]
        private string _providerPath;

        static WebUISimulatorWindow()
        {
            EditorApplication.playModeStateChanged += _OnPlayModeStateChanged;
        }

        private static void _OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                if (HasOpenInstances<WebUISimulatorWindow>())
                {
                    var window = GetWindow<WebUISimulatorWindow>(null, focus: false);
                    if (window != null)
                        window._RestoreProvider();
                }
            }
        }

        private WebUIRemoteControlBehaviour _GetProvider()
        {
            if (_providerObject != null)
                return _providerObject.GetComponent<WebUIRemoteControlBehaviour>();
            return null;
        }

        /// <summary>
        /// Simulator に設定されている _definition の Factory が持つ prefab GUID を AssetDatabase から
        /// 再解決する。他の WebUIDefinition アセットには手を出さない。
        /// </summary>
        private void _RefreshPrefabKeys()
        {
            if (_definition == null)
            {
                UnityEngine.Debug.LogWarning("[RemoteControl] WebUI Simulator Reset: no WebUIDefinition is set. Assign one in the Definition field before pressing Reset.");
                return;
            }
            WebUIDefinitionPrefabKeyRefresher.Refresh(_definition);
        }

        private void _SaveProviderPath()
        {
            if (_providerObject != null)
            {
                _providerPath = _GetGameObjectPath(_providerObject);
            }
        }

        private static string _GetGameObjectPath(GameObject go)
        {
            if (go == null) return string.Empty;

            var path = go.name;
            var parent = go.transform.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        private void _RestoreProvider()
        {
            // GameObjectのSerializeFieldで通常は維持される
            // フォールバック: パスから復元
            if (_providerObject == null && !string.IsNullOrEmpty(_providerPath))
            {
                _providerObject = GameObject.Find(_providerPath);
            }

            // UI更新
            if (_providerField != null)
            {
                var provider = _GetProvider();
                _providerField.SetValueWithoutNotify(null);
                _providerField.SetValueWithoutNotify(provider);
            }
        }

        private void OnEnable()
        {
            EditorApplication.update += _CheckDirty;
            EditorApplication.update += _UpdateRemoteAppStatus;
            EditorApplication.hierarchyChanged += _OnDefinitionChanged;
            Selection.selectionChanged += _OnSelectionChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= _CheckDirty;
            EditorApplication.update -= _UpdateRemoteAppStatus;
            EditorApplication.hierarchyChanged -= _OnDefinitionChanged;
            Selection.selectionChanged -= _OnSelectionChanged;

            var provider = _GetProvider();
            if (provider != null && provider.objectContainer != null)
                provider.objectContainer.Shutdown();
        }

        private void _OnSelectionChanged()
        {
            // ヒエラルキーで WebUIRemoteControlBehaviour を含む GameObject を選択したとき、
            // それを Provider に設定する。該当しない選択では現在の Provider を維持する。
            var go = Selection.activeGameObject;
            if (go == null) return;

            var behaviour = go.GetComponent<WebUIRemoteControlBehaviour>();
            if (behaviour == null) return;

            if (behaviour == _GetProvider()) return;

            if (_providerField != null)
                _providerField.value = behaviour;
        }

        private void _CheckDirty()
        {
            if (_definition == null) return;
            var count = EditorUtility.GetDirtyCount(_definition);
            if (count != _dirtyCount)
            {
                _dirtyCount = count;
                _OnDefinitionChanged();
            }

            // リアルタイム値更新
            _UpdateObjectPropertyValues();
        }

        private void _OnDefinitionChanged()
        {
            if (_definition == null) return;
            if (_suppressRebuild) return;
            _RebuildSideMenu();

            // 選択中のメニュー項目があればコンテンツも再構築
            if (_selectedMenuItemId != null && _definition.menuItems != null)
            {
                var selectedItem = _definition.menuItems.FirstOrDefault(item => item.id == _selectedMenuItemId);
                if (selectedItem != null)
                    _ShowPage(selectedItem);
                else
                    _ClearContent();
            }
        }

        [UnityEditor.MenuItem("Window/Lilium Remote Control/WebUI Simulator")]
        public static void ShowWindow()
        {
            var window = GetWindow<WebUISimulatorWindow>();
            window.titleContent = new GUIContent("WebUI Simulator");
            window.minSize = new Vector2(600, 400);
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;

            // ツールバー
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.flexShrink = 0;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.paddingLeft = 4;
            toolbar.style.paddingRight = 4;
            toolbar.style.paddingTop = 2;
            toolbar.style.paddingBottom = 2;
            toolbar.style.borderBottomWidth = 1;
            toolbar.style.borderBottomColor = new Color(0.15f, 0.15f, 0.15f);

            // Definition ObjectField
            _definitionField = new ObjectField("Definition");
            _definitionField.objectType = typeof(WebUIDefinition);
            _definitionField.value = _definition;
            _definitionField.style.flexGrow = 1;
            _definitionField.style.maxWidth = 300;
            _definitionField.style.marginRight = 8;
            _definitionField.RegisterValueChangedCallback(evt =>
            {
                _definition = evt.newValue as WebUIDefinition;
                _dirtyCount = _definition != null ? EditorUtility.GetDirtyCount(_definition) : 0;
                _selectedMenuItemId = null;
                _RebuildSideMenu();
                _ClearContent();
            });
            toolbar.Add(_definitionField);

            // Provider復元（ドメインリロードやUnity再起動後にSerializeField参照が切れた場合）
            if (_providerObject == null && !string.IsNullOrEmpty(_providerPath))
            {
                _providerObject = GameObject.Find(_providerPath);
            }

            // Provider ObjectField
            _providerField = new ObjectField("Provider");
            _providerField.objectType = typeof(WebUIRemoteControlBehaviour);
            _providerField.value = _GetProvider();
            _providerField.style.flexGrow = 1;
            _providerField.style.maxWidth = 300;
            _providerField.style.marginRight = 8;
            _providerField.RegisterValueChangedCallback(evt =>
            {
                var oldProvider = _GetProvider();
                if (oldProvider != null && oldProvider.objectContainer != null)
                    oldProvider.objectContainer.Shutdown();

                var newProvider = evt.newValue as WebUIRemoteControlBehaviour;
                _providerObject = newProvider != null ? newProvider.gameObject : null;
                _SaveProviderPath();

                // Sync definition from the provider (provider holds the authoritative WebUIDefinition).
                var providerDefinition = newProvider != null ? newProvider.webUIDefinition : null;
                _definition = providerDefinition;
                _dirtyCount = _definition != null ? EditorUtility.GetDirtyCount(_definition) : 0;
                _selectedMenuItemId = null;
                if (_definitionField != null)
                    _definitionField.SetValueWithoutNotify(_definition);
                _RebuildSideMenu();
                _ClearContent();

                if (newProvider != null && newProvider.objectContainer != null)
                    newProvider.objectContainer.Initialize();
            });
            toolbar.Add(_providerField);

            // リセットボタン
            var resetButton = new Button(() =>
            {
                // WebUIDefinition に紐づく各 Factory の prefab GUID を再解決する。
                // OnValidate を待たずに Reset ボタンで一括登録できるようにする。
                _RefreshPrefabKeys();

                var provider = _GetProvider();
                if (provider != null && provider.objectContainer != null)
                {
                    provider.objectContainer.Shutdown();
                    ExposedObjectRegistry.ClearAll();
                    ExposedClass.Reset();
                    ExposedEnum.Reset();
                    provider.objectContainer.Initialize();
                }
            })
            {
                text = "Reset"
            };
            resetButton.style.width = 60;
            toolbar.Add(resetButton);

            // Export ボタン
            var exportIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Settings/Web UI/Icons/file_upload.png");
            var exportButton = new Button(() =>
            {
                var provider = _GetProvider();
                if (provider == null || provider.objectContainer == null) return;

                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                var savedFolder = Path.Combine(projectRoot, "Saved");
                var fileName = "export_" + DateTime.Now.ToString("yyMMddHHmmss") + ".json";
                var filePath = Path.Combine(savedFolder, fileName);

                if (!Directory.Exists(savedFolder))
                    Directory.CreateDirectory(savedFolder);

                var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), provider.objectContainer);
                File.WriteAllText(filePath, json);
                UnityEngine.Debug.Log($"[RemoteControl] Saved to {filePath}");
            });
            exportButton.tooltip = "Export";
            exportButton.style.width = 28;
            exportButton.style.height = 20;
            exportButton.style.marginLeft = 4;
            if (exportIcon != null)
            {
                var img = new Image { image = exportIcon };
                img.style.width = 16;
                img.style.height = 16;
                exportButton.Add(img);
            }
            else
            {
                exportButton.text = "E";
            }
            toolbar.Add(exportButton);

            // Import ボタン
            var importIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Settings/Web UI/Icons/file_download.png");
            var importButton = new Button(() =>
            {
                var provider = _GetProvider();
                if (provider == null || provider.objectContainer == null) return;

                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                var savedFolder = Path.Combine(projectRoot, "Saved");

                var filePath = EditorUtility.OpenFilePanel("Select file to load", savedFolder, "json");
                if (string.IsNullOrEmpty(filePath)) return;

                if (!File.Exists(filePath))
                {
                    UnityEngine.Debug.LogWarning($"[RemoteControl] File not found: {filePath}");
                    return;
                }

                var json = File.ReadAllText(filePath);
                ExposedSceneSerializer.SceneFromJson(json, provider.objectContainer);
                UnityEngine.Debug.Log($"[RemoteControl] Loaded from {filePath}");
            });
            importButton.tooltip = "Import";
            importButton.style.width = 28;
            importButton.style.height = 20;
            importButton.style.marginLeft = 4;
            if (importIcon != null)
            {
                var img = new Image { image = importIcon };
                img.style.width = 16;
                img.style.height = 16;
                importButton.Add(img);
            }
            else
            {
                importButton.text = "I";
            }
            toolbar.Add(importButton);

            // Open ボタン
            var openIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Settings/Web UI/Icons/folder_open.png");
            var openButton = new Button(() =>
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                var savedFolder = Path.Combine(projectRoot, "Saved");

                if (!Directory.Exists(savedFolder))
                    Directory.CreateDirectory(savedFolder);

                Process.Start(savedFolder);
            });
            openButton.tooltip = "Open Saved Folder";
            openButton.style.width = 28;
            openButton.style.height = 20;
            openButton.style.marginLeft = 4;
            if (openIcon != null)
            {
                var img = new Image { image = openIcon };
                img.style.width = 16;
                img.style.height = 16;
                openButton.Add(img);
            }
            else
            {
                openButton.text = "O";
            }
            toolbar.Add(openButton);

            // RemoteApp起動ボタン
            _remoteAppButton = new Button(_OnRemoteAppButtonClicked)
            {
                text = "\u25B6",
                tooltip = "Launch Remote App"
            };
            _remoteAppButton.style.width = 28;
            _remoteAppButton.style.height = 20;
            _remoteAppButton.style.marginLeft = 4;
            _remoteAppButton.style.fontSize = 12;
            _remoteAppButton.style.unityTextAlign = TextAnchor.MiddleCenter;
            toolbar.Add(_remoteAppButton);

            root.Add(toolbar);

            // メインコンテンツ (サイドメニュー + コンテンツエリア)
            var mainContent = new VisualElement();
            mainContent.style.flexDirection = FlexDirection.Row;
            mainContent.style.flexGrow = 1;

            // サイドメニューパネル
            var sidePanel = new VisualElement();
            sidePanel.style.width = kSideMenuWidth;
            sidePanel.style.minWidth = kSideMenuWidth;
            sidePanel.style.flexShrink = 0;
            sidePanel.style.borderRightWidth = 1;
            sidePanel.style.borderRightColor = new Color(0.15f, 0.15f, 0.15f);
            sidePanel.style.flexDirection = FlexDirection.Column;

            // Main メニュー領域
            _sideMenuMain = new VisualElement();
            _sideMenuMain.style.flexGrow = 1;
            sidePanel.Add(_sideMenuMain);

            // セパレーター
            var separator = new VisualElement();
            separator.style.height = 1;
            separator.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
            separator.style.marginTop = 4;
            separator.style.marginBottom = 4;
            sidePanel.Add(separator);

            // Bottom メニュー領域
            _sideMenuBottom = new VisualElement();
            _sideMenuBottom.style.flexShrink = 0;
            sidePanel.Add(_sideMenuBottom);

            mainContent.Add(sidePanel);

            // コンテンツエリア
            var contentPanel = new VisualElement();
            contentPanel.style.flexGrow = 1;
            contentPanel.style.flexDirection = FlexDirection.Column;

            // コンテンツ本体（オブジェクトリスト＋プロパティエリア）
            _contentArea = new VisualElement();
            _contentArea.style.flexGrow = 1;
            _contentArea.style.flexDirection = FlexDirection.Row;

            _objectList = new VisualElement();
            _objectList.style.width = kObjectListWidth;
            _objectList.style.minWidth = kObjectListWidth;
            _objectList.style.flexShrink = 0;
            _objectList.style.borderRightWidth = 1;
            _objectList.style.borderRightColor = new Color(0.15f, 0.15f, 0.15f);
            _contentArea.Add(_objectList);

            _propertyArea = new VisualElement();
            _propertyArea.style.flexGrow = 1;
            _propertyArea.style.flexDirection = FlexDirection.Column;
            _contentArea.Add(_propertyArea);

            contentPanel.Add(_contentArea);
            mainContent.Add(contentPanel);

            root.Add(mainContent);

            _RebuildSideMenu();
        }

        private void _RebuildSideMenu()
        {
            if (_sideMenuMain == null || _sideMenuBottom == null)
                return;

            _sideMenuMain.Clear();
            _sideMenuBottom.Clear();

            if (_definition == null || _definition.menuItems == null)
                return;

            var sorted = _definition.menuItems
                .OrderBy(item => item.order)
                .ToList();

            foreach (var item in sorted)
            {
                var button = _CreateMenuButton(item);
                if (item.position == MenuItemPosition.Bottom)
                    _sideMenuBottom.Add(button);
                else
                    _sideMenuMain.Add(button);
            }
        }

        private VisualElement _CreateMenuButton(MenuItem item)
        {
            var button = new Button(() => _OnMenuItemClicked(item));
            button.tooltip = item.label ?? item.id;
            button.style.flexDirection = FlexDirection.Row;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;
            button.style.height = 36;
            button.style.marginLeft = 4;
            button.style.marginRight = 4;
            button.style.marginTop = 1;
            button.style.marginBottom = 1;
            button.style.paddingLeft = 0;
            button.style.paddingRight = 0;

            // アイコン表示（editorIcon優先、フォールバックでlabel先頭2文字）
            if (item.editorIcon != null)
            {
                var iconImage = new Image();
                iconImage.image = item.editorIcon;
                iconImage.style.width = 20;
                iconImage.style.height = 20;
                iconImage.style.flexShrink = 0;
                button.Add(iconImage);
            }
            else
            {
                var displayText = item.label ?? item.id ?? "";
                if (displayText.Length > 2) displayText = displayText.Substring(0, 2);
                var iconLabel = new Label(displayText);
                iconLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                iconLabel.style.fontSize = 14;
                iconLabel.style.flexShrink = 0;
                button.Add(iconLabel);
            }

            // 選択状態のハイライト
            if (_selectedMenuItemId == item.id)
            {
                button.style.backgroundColor = new Color(0.2f, 0.4f, 0.6f, 0.5f);
            }

            return button;
        }

        private void _OnMenuItemClicked(MenuItem item)
        {
            _selectedMenuItemId = item.id;
            _RebuildSideMenu();
            _ShowPage(item);
        }

        private void _ShowPage(MenuItem item)
        {
            if (_objectList == null || _propertyArea == null)
                return;

            _selectedMenuItem = item;
            _objectList.Clear();
            _propertyArea.Clear();
            _selectedObject = null;
            _propertyScrollView = null;

            var categoryPage = item.page as CategoryPage;
            if (categoryPage == null || categoryPage.selector == null)
            {
                var placeholder = new Label("Custom page");
                placeholder.style.paddingLeft = 8;
                placeholder.style.paddingTop = 8;
                placeholder.style.color = new Color(0.5f, 0.5f, 0.5f);
                _propertyArea.Add(placeholder);
                return;
            }

            // セレクタからオブジェクトを取得
            var objects = categoryPage.selector.objects;

            // factoryのInitialize
            var factory = categoryPage.factory;
            if (factory is ObjectFactoryBase factoryBase)
            {
                var provider = _GetProvider();
                var container = provider != null ? provider.objectContainer : null;
                factoryBase.Initialize(container);
            }

            // Add ボタン（factoryが有効な場合のみ表示）
            if (factory != null)
            {
                var addButton = new Button(() =>
                {
                    var names = factory.objectNames;
                    if (names == null || names.Length == 0) return;
                    var menu = new GenericMenu();
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (string.IsNullOrEmpty(names[i])) continue;
                        var idx = i;
                        menu.AddItem(new GUIContent(names[i]), false, () =>
                        {
                            factory.CreateObject(idx);
                            // 再描画
                            _ShowPage(item);
                        });
                    }
                    menu.ShowAsContext();
                });
                addButton.text = "+";
                addButton.style.width = 24;
                addButton.style.height = 24;
                addButton.style.marginLeft = 2;
                addButton.style.marginRight = 2;
                addButton.style.marginTop = 4;
                addButton.style.marginBottom = 4;
                addButton.style.fontSize = 14;
                addButton.style.unityTextAlign = TextAnchor.MiddleCenter;
                _objectList.Add(addButton);
            }

            if (objects == null || objects.Length == 0)
            {
                var noObjects = new Label("No objects found");
                noObjects.style.paddingLeft = 8;
                noObjects.style.paddingTop = 8;
                noObjects.style.color = new Color(0.5f, 0.5f, 0.5f);
                _objectList.Add(noObjects);
                return;
            }

            // オブジェクトリスト表示
            ExposedObject firstExposed = null;
            foreach (var obj in objects)
            {
                var exposed = ExposedObjectRegistry.FindByTarget(obj);
                if (exposed == null) continue;

                if (firstExposed == null)
                    firstExposed = exposed;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginLeft = 2;
                row.style.marginRight = 2;
                row.style.marginTop = 1;
                row.style.marginBottom = 1;

                var capturedExposed = exposed;
                var objButton = new Button(() => _SelectObject(capturedExposed));
                objButton.text = exposed.name;
                objButton.style.height = 28;
                objButton.style.flexGrow = 1;
                objButton.style.flexShrink = 1;
                objButton.style.overflow = Overflow.Hidden;
                objButton.name = "obj-button";
                objButton.userData = exposed;

                // 選択状態のハイライト
                if (_selectedObject != null && _selectedObject == exposed)
                {
                    objButton.style.backgroundColor = new Color(0.2f, 0.4f, 0.6f, 0.5f);
                }

                row.Add(objButton);

                {
                    var capturedItem = item;
                    var capturedFactory = factory;
                    var deleteButton = new Button(() =>
                    {
                        if (capturedFactory != null && capturedExposed != null)
                        {
                            capturedFactory.DestroyObject(capturedExposed.id);
                        }
                        if (_selectedObject == capturedExposed)
                        {
                            _selectedObject = null;
                            _propertyScrollView = null;
                        }
                        _ShowPage(capturedItem);
                    });
                    deleteButton.text = "×";
                    deleteButton.style.width = 24;
                    deleteButton.style.minWidth = 24;
                    deleteButton.style.height = 28;
                    deleteButton.style.flexShrink = 0;
                    row.Add(deleteButton);
                }

                _objectList.Add(row);
            }

            // 最初のオブジェクトを自動選択
            if (firstExposed != null)
            {
                _SelectObject(firstExposed);
            }
        }

        private void _SelectObject(ExposedObject obj)
        {
            _selectedObject = obj;
            _ShowObjectProperties(obj);
            _UpdateObjectListHighlight();
        }

        private void _UpdateObjectListHighlight()
        {
            if (_objectList == null) return;

            foreach (var child in _objectList.Children())
            {
                var button = child.Q<Button>("obj-button");
                if (button == null) continue;

                var isSelected = button.userData as ExposedObject == _selectedObject;
                button.style.backgroundColor = isSelected
                    ? new Color(0.2f, 0.4f, 0.6f, 0.5f)
                    : StyleKeyword.Null;
            }
        }

        private void _ShowObjectProperties(ExposedObject obj)
        {
            _propertyArea.Clear();
            _propertyScrollView = null;

            if (obj == null || !obj.isValid)
            {
                if (obj != null)
                {
                    var invalid = new Label("Invalid object");
                    invalid.style.color = new Color(0.8f, 0.3f, 0.3f);
                    invalid.style.paddingLeft = 8;
                    invalid.style.paddingTop = 8;
                    _propertyArea.Add(invalid);
                }
                return;
            }

            // ヘッダー
            var headerContainer = new VisualElement();
            headerContainer.style.paddingLeft = 8;
            headerContainer.style.paddingTop = 8;
            headerContainer.style.paddingBottom = 8;
            headerContainer.style.borderBottomWidth = 1;
            headerContainer.style.borderBottomColor = new Color(0.15f, 0.15f, 0.15f);

            // クラス名（上段）
            var classLabel = new Label(obj.targetTypeName);
            classLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            classLabel.style.fontSize = 11;
            headerContainer.Add(classLabel);

            // オブジェクト名（メインタイトル）
            var propertyTypes = obj.propertyTypes;
            var namePropertyType = propertyTypes?.FirstOrDefault(p => p.name == "name");
            if (namePropertyType != null)
            {
                var nameField = new TextField();
                nameField.name = "header-name-field";
                nameField.label = "";
                nameField.value = obj.name;
                nameField.style.marginBottom = 4;
                var capturedObj = obj;
                nameField.RegisterValueChangedCallback(evt =>
                {
                    if (_isUpdatingUI) return;
                    var prop = capturedObj.FindProperty("name");
                    if (prop.HasValue)
                    {
                        _suppressRebuild = true;
                        prop.Value.SetValue(evt.newValue);
                        // hierarchyChanged等の遅延コールバックに備え、次フレームで解除
                        EditorApplication.delayCall += () => _suppressRebuild = false;
                    }
                    // オブジェクトリストのボタンテキストも連動更新
                    if (_objectList != null)
                    {
                        foreach (var child in _objectList.Children())
                        {
                            var button = child.Q<Button>("obj-button");
                            if (button != null && button.userData as ExposedObject == capturedObj)
                            {
                                button.text = evt.newValue;
                                break;
                            }
                        }
                    }
                });
                headerContainer.Add(nameField);
            }
            else
            {
                var header = new TextField();
                header.label = "";
                header.value = obj.name;
                header.isReadOnly = true;
                header.style.marginBottom = 4;
                headerContainer.Add(header);
            }

            // ID
            var idLabel = new Label($"ID: {obj.id}");
            idLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            idLabel.style.fontSize = 11;
            headerContainer.Add(idLabel);

            _propertyArea.Add(headerContainer);

            // スクロール可能なコンテンツ
            _propertyScrollView = new ScrollView(ScrollViewMode.Vertical);
            _propertyScrollView.style.flexGrow = 1;
            _propertyScrollView.style.paddingLeft = 8;
            _propertyScrollView.style.paddingTop = 8;
            _propertyScrollView.style.paddingRight = 8;

            // プロパティ一覧
            if (propertyTypes != null && propertyTypes.Length > 0)
            {
                var sorted = propertyTypes.OrderBy(p => p.order).ToArray();
                foreach (var propType in sorted)
                {
                    // nameプロパティはヘッダーで表示済みなのでスキップ
                    if (namePropertyType != null && propType.name == "name") continue;

                    var row = _CreateObjectPropertyRow(obj, propType);
                    _propertyScrollView.Add(row);
                }
            }

            // ファンクション一覧
            var functionTypes = obj.targetType?.functionTypes;
            if (functionTypes != null && functionTypes.Length > 0)
            {
                var sortedFuncs = functionTypes.OrderBy(f => f.order).ToArray();
                foreach (var funcType in sortedFuncs)
                {
                    var row = FunctionRowControl.CreateFunctionRow(obj, funcType);
                    _propertyScrollView.Add(row);
                }
            }

            _propertyArea.Add(_propertyScrollView);
        }

        private VisualElement _CreateObjectPropertyRow(ExposedObject obj, ExposedPropertyType propType)
        {
            // 編集コントロール生成
            var prop = obj.FindProperty(propType.name);
            var currentValue = prop != null ? prop.Value.GetValue() : null;

            var propertyControl = PropertyControlFactory.GetControl(propType, prop.HasValue, propType.valueType);
            var ctx = new PropertyControlContext
            {
                obj = obj,
                propType = propType,
                prop = prop.HasValue ? prop.Value : default,
                currentValue = currentValue,
                isReadOnly = propType.isReadOnly,
                isUpdatingUI = () => _isUpdatingUI
            };

            // TypeSelectorの場合: ラベル+ドロップダウン行 + フラットなネストプロパティ
            if (propertyControl is TypeSelectorPropertyControl tsControl)
            {
                var wrapper = new VisualElement();
                wrapper.style.flexDirection = FlexDirection.Column;
                wrapper.style.marginBottom = 1;

                // ラベル + ドロップダウン行
                var headerRow = new VisualElement();
                headerRow.style.flexDirection = FlexDirection.Row;
                headerRow.style.paddingTop = 1;
                headerRow.style.paddingBottom = 1;
                headerRow.style.alignItems = Align.Center;
                headerRow.style.minHeight = 20;

                var nameLabel = _CreatePropertyNameLabel(obj, propType);
                headerRow.Add(nameLabel);

                // 型切り替え時のネストプロパティ再構築先をwrapperに設定
                tsControl.nestedPropsTarget = wrapper;

                var control = tsControl.CreateControl(ctx);
                control.name = "prop-control";
                control.style.flexGrow = 1;
                control.style.flexShrink = 1;
                headerRow.Add(control);

                wrapper.Add(headerRow);

                // ネストプロパティをheaderRowの外（wrapper直下）に移動
                var nestedProps = control.Q("type-selector-props");
                if (nestedProps != null)
                {
                    nestedProps.RemoveFromHierarchy();
                    wrapper.Add(nestedProps);
                }

                wrapper.userData = propType.name;
                return wrapper;
            }

            // CameraControlの場合: TypeSelectorと同様のレイアウト
            if (propertyControl is CameraControlPropertyControl ccControl)
            {
                var wrapper = new VisualElement();
                wrapper.style.flexDirection = FlexDirection.Column;
                wrapper.style.marginBottom = 1;

                var headerRow = new VisualElement();
                headerRow.style.flexDirection = FlexDirection.Row;
                headerRow.style.paddingTop = 1;
                headerRow.style.paddingBottom = 1;
                headerRow.style.alignItems = Align.Center;
                headerRow.style.minHeight = 20;

                var nameLabel = _CreatePropertyNameLabel(obj, propType);
                headerRow.Add(nameLabel);

                ccControl.nestedPropsTarget = wrapper;

                var control = ccControl.CreateControl(ctx);
                control.name = "prop-control";
                control.style.flexGrow = 1;
                control.style.flexShrink = 1;
                headerRow.Add(control);

                wrapper.Add(headerRow);

                var nestedProps = control.Q("camera-control-props");
                if (nestedProps != null)
                {
                    nestedProps.RemoveFromHierarchy();
                    wrapper.Add(nestedProps);
                }

                wrapper.userData = propType.name;
                return wrapper;
            }

            // 通常のプロパティ行
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 1;
            row.style.paddingTop = 1;
            row.style.paddingBottom = 1;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 20;

            var label = _CreatePropertyNameLabel(obj, propType);
            row.Add(label);

            var ctrl = propertyControl.CreateControl(ctx);
            ctrl.name = "prop-control";
            ctrl.style.flexGrow = 1;
            ctrl.style.flexShrink = 1;
            row.Add(ctrl);

            // プロパティ名をuserDataに保存（値更新用）
            row.userData = propType.name;

            return row;
        }

        private Label _CreatePropertyNameLabel(ExposedObject obj, ExposedPropertyType propType)
        {
            var nameLabel = new Label(ObjectNames.NicifyVariableName(propType.name));
            nameLabel.style.width = kPropertyNameWidth;
            nameLabel.style.minWidth = kPropertyNameWidth;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            nameLabel.name = "prop-name";

            if (obj.IsPropertyDirty(propType.name))
            {
                nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            }

            return nameLabel;
        }

        private void _UpdateObjectPropertyValues()
        {
            if (_selectedObject == null || !_selectedObject.isValid || _propertyScrollView == null) return;

            _isUpdatingUI = true;
            try
            {
                // ヘッダーのname TextField更新
                var headerNameField = _propertyArea.Q<TextField>("header-name-field");
                if (headerNameField != null)
                {
                    var nameProp = _selectedObject.FindProperty("name");
                    if (nameProp.HasValue)
                    {
                        var nameValue = nameProp.Value.GetValue() as string;
                        if (nameValue != null && headerNameField.value != nameValue)
                        {
                            headerNameField.value = nameValue;
                            // オブジェクトリストのボタンテキストも連動更新
                            if (_objectList != null)
                            {
                                foreach (var child in _objectList.Children())
                                {
                                    var button = child.Q<Button>("obj-button");
                                    if (button != null && button.userData as ExposedObject == _selectedObject)
                                    {
                                        button.text = nameValue;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                foreach (var child in _propertyScrollView.Children())
                {
                    var propName = child.userData as string;
                    if (propName == null) continue;

                    var propType = _selectedObject.targetType?.FindProperty(propName);
                    if (propType == null) continue;

                    var prop = _selectedObject.FindProperty(propName);
                    object value = null;
                    try { if (prop.HasValue) value = prop.Value.GetValue(); } catch { }

                    // Foldout展開の場合は再帰更新
                    var control = child.Q(name: "prop-control");
                    if (control is Foldout foldout)
                    {
                        ReferencePropertyControl.UpdateNestedPropertyValues(foldout, prop);
                    }
                    else if (control != null)
                    {
                        var propertyControl = PropertyControlFactory.GetControl(propType, prop.HasValue, propType.valueType);
                        propertyControl.UpdateValue(control, value);
                    }

                    // dirty状態更新
                    var nameLabel = child.Q<Label>("prop-name");
                    if (nameLabel != null)
                    {
                        nameLabel.style.unityFontStyleAndWeight = _selectedObject.IsPropertyDirty(propName)
                            ? FontStyle.Bold
                            : FontStyle.Normal;
                    }
                }
            }
            finally
            {
                _isUpdatingUI = false;
            }
        }

        private void _ClearContent()
        {
            if (_objectList == null || _propertyArea == null)
                return;

            _objectList.Clear();
            _propertyArea.Clear();
            _selectedObject = null;
            _propertyScrollView = null;
            _selectedMenuItem = null;
        }

        private void _OnRemoteAppButtonClicked()
        {
            if (_IsRemoteAppRunning())
                _CloseRemoteApp();
            else
                _LaunchRemoteApp();
        }

        private void _LaunchRemoteApp()
        {
            var appPath = _ResolveRemoteAppPath();
            if (string.IsNullOrEmpty(appPath) || !File.Exists(appPath))
            {
                UnityEngine.Debug.LogError($"[RemoteControl] Remote App not found: {appPath}");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = appPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };

            _remoteAppProcess = Process.Start(startInfo);

            if (_remoteAppProcess == null)
            {
                UnityEngine.Debug.LogError($"[RemoteControl] Failed to launch Remote App: {appPath}");
            }
        }

        private void _CloseRemoteApp()
        {
            if (_remoteAppProcess == null) return;

            try
            {
                if (!_remoteAppProcess.HasExited)
                    _remoteAppProcess.CloseMainWindow();
            }
            catch (InvalidOperationException)
            {
                // プロセスが既に終了している
            }
            finally
            {
                _remoteAppProcess.Dispose();
                _remoteAppProcess = null;
            }
        }

        private bool _IsRemoteAppRunning()
        {
            if (_remoteAppProcess == null) return false;

            try
            {
                if (_remoteAppProcess.HasExited)
                {
                    _remoteAppProcess.Dispose();
                    _remoteAppProcess = null;
                    return false;
                }
                return true;
            }
            catch (InvalidOperationException)
            {
                _remoteAppProcess = null;
                return false;
            }
        }

        private void _UpdateRemoteAppStatus()
        {
            if (_remoteAppButton == null) return;

            bool running = _IsRemoteAppRunning();
            _remoteAppButton.tooltip = running ? "Close Remote App" : "Launch Remote App";
            _remoteAppButton.style.color = running
                ? new Color(0.3f, 0.85f, 0.3f)
                : StyleKeyword.Null;
        }

        private static string _ResolveRemoteAppPath()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/jp.lilium.virgo.studio");
            if (packageInfo != null)
                return Path.GetFullPath(Path.Combine(packageInfo.resolvedPath, "Tools~/VirgoMotionRemote/VirgoMotionRemote.exe"));
            // フォールバック: プロジェクト相対
            return Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/VirgoMotionRemote/VirgoMotionRemote.exe"));
        }
    }
}
