// Copyright (c) You-Ri, 2026

using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Lilium.RemoteControl.Editor;
using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl.UI.Editor
{
    public class UIDesignerWindow : EditorWindow
    {
        [SerializeField] private UIDefinition _definition;
        [SerializeField] private GameObject _providerObject;
        private string _selectedMenuItemId;
        private VisualElement _sideMenuMain;
        private VisualElement _sideMenuBottom;
        private VisualElement _contentArea;
        private VisualElement _objectList;
        private VisualElement _propertyArea;
        private ObjectField _providerField;
        private ObjectField _definitionField;

        private LiveObjectHandle? _selectedObject;
        private ScrollView _propertyScrollView;
        private MenuItem _selectedMenuItem;

        private const string kStyleSheet = "UI/Editor/UIDesignerWindow.uss";

        private int _dirtyCount;
        private bool _isUpdatingUI;
        private bool _suppressRebuild;


        [SerializeField]
        private string _providerPath;

        static UIDesignerWindow()
        {
            EditorApplication.playModeStateChanged += _OnPlayModeStateChanged;
        }

        private static void _OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                if (HasOpenInstances<UIDesignerWindow>())
                {
                    var window = GetWindow<UIDesignerWindow>(null, focus: false);
                    if (window != null)
                        window._RestoreProvider();
                }
            }
        }

        private UIRemoteControlBehaviour _GetProvider()
        {
            if (_providerObject != null)
                return _providerObject.GetComponent<UIRemoteControlBehaviour>();
            return null;
        }

        private void _UpdateDefinitionProviderVisibility()
        {
            var hasProvider = _GetProvider() != null;
            if (_definitionField != null)
                _definitionField.style.display = hasProvider ? DisplayStyle.None : DisplayStyle.Flex;
            if (_providerField != null)
                _providerField.style.display = hasProvider ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Re-resolves the prefab GUIDs held by the Factory on the _definition assigned to this Designer
        /// from the AssetDatabase. Does not touch other UIDefinition assets.
        /// </summary>
        private void _RefreshPrefabKeys()
        {
            if (_definition == null)
            {
                UnityEngine.Debug.LogWarning("[RemoteControl] UI Designer Reset: no UIDefinition is set. Assign one in the Definition field before pressing Reset.");
                return;
            }
            UIDefinitionPrefabKeyRefresher.Refresh(_definition);
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
            EditorApplication.hierarchyChanged += _OnDefinitionChanged;
            Selection.selectionChanged += _OnSelectionChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= _CheckDirty;
            EditorApplication.hierarchyChanged -= _OnDefinitionChanged;
            Selection.selectionChanged -= _OnSelectionChanged;

            var provider = _GetProvider();
            if (provider != null && provider.objectContainer != null)
                provider.objectContainer.Shutdown();
        }

        private void _OnSelectionChanged()
        {
            // Project ビューで UIDefinition アセットを選択したときは、
            // それを Definition に差し替え、Provider は null にクリアする。
            var def = Selection.activeObject as UIDefinition;
            if (def != null)
            {
                _SetDefinition(def);
                return;
            }

            // ヒエラルキーで UIRemoteControlBehaviour を含む GameObject を選択したとき、
            // それを Provider に設定する。該当しない選択では現在の Provider を維持する。
            var go = Selection.activeGameObject;
            if (go == null) return;

            var behaviour = go.GetComponent<UIRemoteControlBehaviour>();
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

        // Definition を差し替える。Provider が付いている間は Definition を持つのは Provider 側なので、
        // アセットを直接編集するときは Provider をクリアしてから差し替える。
        private void _SetDefinition(UIDefinition definition)
        {
            if (definition == null) return;
            if (definition == _definition && _GetProvider() == null) return;

            if (_definitionField == null)
            {
                // CreateGUI 前 (ウィンドウを開いた直後) はフィールドがまだ無いので、
                // CreateGUI が初期値として読むフィールドへ直接入れる。
                _definition = definition;
                _dirtyCount = EditorUtility.GetDirtyCount(definition);
                _selectedMenuItemId = null;
                _providerObject = null;
                _providerPath = null;
                return;
            }

            if (_GetProvider() != null)
                _providerField.value = null;

            _definitionField.value = definition;
        }

        [UnityEditor.MenuItem("Window/Lilium Remote Control/UI Designer")]
        public static void ShowWindow()
        {
            var window = GetWindow<UIDesignerWindow>();
            window.titleContent = new GUIContent("UI Designer");
            window.minSize = new Vector2(600, 400);
        }

        /// <summary>
        /// Opens the Designer on a specific definition asset.
        /// </summary>
        public static void ShowWindow(UIDefinition definition)
        {
            var window = GetWindow<UIDesignerWindow>();
            window.titleContent = new GUIContent("UI Designer");
            window.minSize = new Vector2(600, 400);
            window._SetDefinition(definition);
            window.Focus();
        }

        // Project ビューで UIDefinition アセットをダブルクリックしたら Designer で開く。
        // true を返すと Unity 既定の処理を差し止められる。コールバックの引数は int 固定なので、
        // EntityId 化のバージョン差を吸収する LiveObjectUtility 経由で逆引きする。
        [UnityEditor.Callbacks.OnOpenAsset]
        private static bool _OnOpenAsset(int instanceId, int line)
        {
            var definition = LiveObjectUtility.InstanceIDToObject(instanceId) as UIDefinition;
            if (definition == null) return false;
            ShowWindow(definition);
            return true;
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            RemoteControlEditorStyles.Apply(root, kStyleSheet);
            root.AddToClassList("uid-root");

            // ツールバー
            var toolbar = new VisualElement();
            toolbar.AddToClassList("uid-toolbar");

            // Definition ObjectField
            _definitionField = new ObjectField();
            _definitionField.objectType = typeof(UIDefinition);
            _definitionField.value = _definition;
            _definitionField.AddToClassList("uid-toolbar-field");
            _definitionField.SetEnabled(false);
            _definitionField.RegisterValueChangedCallback(evt =>
            {
                _definition = evt.newValue as UIDefinition;
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
            _providerField = new ObjectField();
            _providerField.objectType = typeof(UIRemoteControlBehaviour);
            _providerField.value = _GetProvider();
            _providerField.AddToClassList("uid-toolbar-field");
            _providerField.SetEnabled(false);
            _providerField.RegisterValueChangedCallback(evt =>
            {
                var oldProvider = _GetProvider();
                if (oldProvider != null && oldProvider.objectContainer != null)
                    oldProvider.objectContainer.Shutdown();

                var newProvider = evt.newValue as UIRemoteControlBehaviour;
                _providerObject = newProvider != null ? newProvider.gameObject : null;
                _SaveProviderPath();

                // Sync definition from the provider (provider holds the authoritative UIDefinition).
                var providerDefinition = newProvider != null ? newProvider.uiDefinition : null;
                _definition = providerDefinition;
                _dirtyCount = _definition != null ? EditorUtility.GetDirtyCount(_definition) : 0;
                _selectedMenuItemId = null;
                if (_definitionField != null)
                    _definitionField.SetValueWithoutNotify(_definition);
                _RebuildSideMenu();
                _ClearContent();

                if (newProvider != null && newProvider.objectContainer != null)
                    newProvider.objectContainer.Initialize();

                _UpdateDefinitionProviderVisibility();
            });
            toolbar.Add(_providerField);

            _UpdateDefinitionProviderVisibility();

            // リセットボタン
            var resetButton = new Button(() =>
            {
                // UIDefinition に紐づく各 Factory の prefab GUID を再解決する。
                // OnValidate を待たずに Reset ボタンで一括登録できるようにする。
                _RefreshPrefabKeys();

                var provider = _GetProvider();
                if (provider != null && provider.objectContainer != null)
                {
                    provider.objectContainer.Shutdown();
                    LiveObjectRegistry.ClearAll();
                    LiveClass.Reset();
                    LiveEnum.Reset();
                    provider.objectContainer.Initialize();
                }
            })
            {
                text = "Reset"
            };
            resetButton.AddToClassList("uid-reset-button");
            toolbar.Add(resetButton);

            root.Add(toolbar);

            // メインコンテンツ (サイドメニュー + コンテンツエリア)
            var mainContent = new VisualElement();
            mainContent.AddToClassList("uid-main");

            // サイドメニューパネル
            var sidePanel = new VisualElement();
            sidePanel.AddToClassList("uid-side-panel");

            // Main メニュー領域
            _sideMenuMain = new VisualElement();
            _sideMenuMain.AddToClassList("uid-side-main");
            sidePanel.Add(_sideMenuMain);

            // セパレーター
            var separator = new VisualElement();
            separator.AddToClassList("uid-side-separator");
            sidePanel.Add(separator);

            // Bottom メニュー領域
            _sideMenuBottom = new VisualElement();
            _sideMenuBottom.AddToClassList("uid-side-bottom");
            sidePanel.Add(_sideMenuBottom);

            mainContent.Add(sidePanel);

            // コンテンツエリア
            var contentPanel = new VisualElement();
            contentPanel.AddToClassList("uid-content-panel");

            // コンテンツ本体（オブジェクトリスト＋プロパティエリア）
            _contentArea = new VisualElement();
            _contentArea.AddToClassList("uid-content");

            _objectList = new VisualElement();
            _objectList.AddToClassList("uid-object-list");
            _contentArea.Add(_objectList);

            _propertyArea = new VisualElement();
            _propertyArea.AddToClassList("uid-property-area");
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
            button.AddToClassList("uid-menu-button");

            // アイコン表示（editorIcon優先、フォールバックでlabel先頭2文字）
            if (item.editorIcon != null)
            {
                var iconImage = new Image();
                iconImage.image = item.editorIcon;
                iconImage.AddToClassList("uid-menu-icon");
                button.Add(iconImage);
            }
            else
            {
                var displayText = item.label ?? item.id ?? "";
                if (displayText.Length > 2) displayText = displayText.Substring(0, 2);
                var iconLabel = new Label(displayText);
                iconLabel.AddToClassList("uid-menu-initials");
                button.Add(iconLabel);
            }

            // 選択状態のハイライト
            button.EnableInClassList("uid-menu-button--selected", _selectedMenuItemId == item.id);

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
                placeholder.AddToClassList("uid-placeholder");
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
                addButton.AddToClassList("uid-add-button");
                _objectList.Add(addButton);
            }

            if (objects == null || objects.Length == 0)
            {
                var noObjects = new Label("No objects found");
                noObjects.AddToClassList("uid-placeholder");
                _objectList.Add(noObjects);
                return;
            }

            // オブジェクトリスト表示
            LiveObjectHandle? firstLive = null;
            foreach (var obj in objects)
            {
                var exposed = LiveObjectRegistry.FindByTarget(obj);
                if (exposed == null) continue;

                if (firstLive == null)
                    firstLive = exposed;

                var row = new VisualElement();
                row.AddToClassList("uid-object-row");

                var capturedLive = exposed;
                var objButton = new Button(() => _SelectObject(capturedLive.Value));
                objButton.text = exposed.Value.name;
                objButton.AddToClassList("uid-object-button");
                objButton.name = "obj-button";
                objButton.userData = exposed;

                // 選択状態のハイライト
                objButton.EnableInClassList("uid-object-button--selected",
                    _selectedObject != null && _selectedObject == exposed);

                row.Add(objButton);

                {
                    var capturedItem = item;
                    var capturedFactory = factory;
                    var deleteButton = new Button(() =>
                    {
                        if (capturedFactory != null && capturedLive != null)
                        {
                            capturedFactory.DestroyObject(capturedLive.Value.id);
                        }
                        if (_selectedObject == capturedLive)
                        {
                            _selectedObject = null;
                            _propertyScrollView = null;
                        }
                        _ShowPage(capturedItem);
                    });
                    deleteButton.text = "×";
                    deleteButton.AddToClassList("uid-object-delete");
                    row.Add(deleteButton);
                }

                _objectList.Add(row);
            }

            // 最初のオブジェクトを自動選択
            if (firstLive != null)
            {
                _SelectObject(firstLive.Value);
            }
        }

        private void _SelectObject(LiveObjectHandle obj)
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

                var isSelected = button.userData as LiveObjectHandle? == _selectedObject;
                button.EnableInClassList("uid-object-button--selected", isSelected);
            }
        }

        private void _ShowObjectProperties(LiveObjectHandle obj)
        {
            _propertyArea.Clear();
            _propertyScrollView = null;

            if (obj == null || !obj.isValid)
            {
                if (obj != null)
                {
                    var invalid = new Label("Invalid object");
                    invalid.AddToClassList("uid-invalid");
                    _propertyArea.Add(invalid);
                }
                return;
            }

            // ヘッダー
            var headerContainer = new VisualElement();
            headerContainer.AddToClassList("uid-detail-header");

            // クラス名（上段）
            var classLabel = new Label(obj.targetTypeName);
            classLabel.AddToClassList("uid-detail-meta");
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
                nameField.AddToClassList("uid-name-field");
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
                            if (button != null && button.userData as LiveObjectHandle? == capturedObj)
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
                header.AddToClassList("uid-name-field");
                headerContainer.Add(header);
            }

            // ID
            var idLabel = new Label($"ID: {obj.id}");
            idLabel.AddToClassList("uid-detail-meta");
            headerContainer.Add(idLabel);

            _propertyArea.Add(headerContainer);

            // スクロール可能なコンテンツ
            _propertyScrollView = new ScrollView(ScrollViewMode.Vertical);
            _propertyScrollView.AddToClassList("uid-detail-scroll");

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

        private VisualElement _CreateObjectPropertyRow(LiveObjectHandle obj, LivePropertyType propType)
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
                wrapper.AddToClassList("uid-prop-wrapper");

                // ラベル + ドロップダウン行
                var headerRow = new VisualElement();
                headerRow.AddToClassList("uid-prop-header-row");

                var nameLabel = _CreatePropertyNameLabel(obj, propType);
                headerRow.Add(nameLabel);

                // 型切り替え時のネストプロパティ再構築先をwrapperに設定
                tsControl.nestedPropsTarget = wrapper;

                var control = tsControl.CreateControl(ctx);
                control.name = "prop-control";
                control.AddToClassList("uid-prop-control");
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
                wrapper.AddToClassList("uid-prop-wrapper");

                var headerRow = new VisualElement();
                headerRow.AddToClassList("uid-prop-header-row");

                var nameLabel = _CreatePropertyNameLabel(obj, propType);
                headerRow.Add(nameLabel);

                ccControl.nestedPropsTarget = wrapper;

                var control = ccControl.CreateControl(ctx);
                control.name = "prop-control";
                control.AddToClassList("uid-prop-control");
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
            row.AddToClassList("uid-prop-row");

            var label = _CreatePropertyNameLabel(obj, propType);
            row.Add(label);

            var ctrl = propertyControl.CreateControl(ctx);
            ctrl.name = "prop-control";
            ctrl.AddToClassList("uid-prop-control");
            row.Add(ctrl);

            // プロパティ名をuserDataに保存（値更新用）
            row.userData = propType.name;

            return row;
        }

        private Label _CreatePropertyNameLabel(LiveObjectHandle obj, LivePropertyType propType)
        {
            var nameLabel = new Label(ObjectNames.NicifyVariableName(propType.name));
            nameLabel.AddToClassList("uid-prop-name");
            nameLabel.name = "prop-name";
            nameLabel.EnableInClassList("uid-prop-name--dirty", obj.IsPropertyDirty(propType.name));

            return nameLabel;
        }

        private void _UpdateObjectPropertyValues()
        {
            if (_selectedObject == null || !_selectedObject.Value.isValid || _propertyScrollView == null) return;
            var selected = _selectedObject.Value;

            _isUpdatingUI = true;
            try
            {
                // ヘッダーのname TextField更新
                var headerNameField = _propertyArea.Q<TextField>("header-name-field");
                if (headerNameField != null)
                {
                    var nameProp = selected.FindProperty("name");
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
                                    if (button != null && button.userData as LiveObjectHandle? == _selectedObject)
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

                    var propType = selected.targetType?.FindProperty(propName);
                    if (propType == null) continue;

                    var prop = selected.FindProperty(propName);
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
                        nameLabel.EnableInClassList("uid-prop-name--dirty", selected.IsPropertyDirty(propName));
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
    }
}
