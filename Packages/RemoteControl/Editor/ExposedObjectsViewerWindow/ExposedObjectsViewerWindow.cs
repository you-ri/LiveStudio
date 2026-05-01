// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.Editor
{
    public class ExposedObjectsViewerWindow : EditorWindow
    {
        private const float kSideMenuWidth = 240f;
        private const float kPropertyNameWidth = 180f;

        private enum ViewMode { Types, Enums, Objects }

        private ViewMode _viewMode = ViewMode.Objects;
        private VisualElement _typeList;
        private VisualElement _contentArea;
        private TextField _filterField;
        private ScrollView _contentScrollView;

        private string _filterText = "";
        private object _selectedItem; // ExposedClass, ExposedEnum, or ExposedObject

        // Objects用
        private int _lastInstanceCount;

        [MenuItem("Window/Remote Control/ExposedObjects Viewer")]
        public static void ShowWindow()
        {
            var window = GetWindow<ExposedObjectsViewerWindow>();
            window.titleContent = new GUIContent("ExposedObjects Viewer");
            window.minSize = new Vector2(700, 400);
        }

        private void OnEnable()
        {
            EditorApplication.update += _OnUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= _OnUpdate;
        }

        private void _OnUpdate()
        {
            if (_viewMode == ViewMode.Objects)
            {
                // インスタンス数の変化を検出してリスト再構築
                var count = ExposedObjectRegistry.instances.Count;
                if (count != _lastInstanceCount)
                {
                    _lastInstanceCount = count;
                    _RebuildSidePanel();
                }

                // 選択中オブジェクトのプロパティ値をリアルタイム更新
                var selectedObject = _selectedItem as ExposedObject;
                if (selectedObject != null && _contentScrollView != null)
                {
                    _UpdateObjectPropertyValues(selectedObject);
                }
            }
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Row;

            // サイドパネル
            var sidePanel = new VisualElement();
            sidePanel.style.width = kSideMenuWidth;
            sidePanel.style.minWidth = kSideMenuWidth;
            sidePanel.style.flexShrink = 0;
            sidePanel.style.borderRightWidth = 1;
            sidePanel.style.borderRightColor = new Color(0.15f, 0.15f, 0.15f);
            sidePanel.style.flexDirection = FlexDirection.Column;

            // タブ切り替え
            var tabRow = new VisualElement();
            tabRow.style.flexDirection = FlexDirection.Row;
            tabRow.style.marginLeft = 4;
            tabRow.style.marginRight = 4;
            tabRow.style.marginTop = 4;
            tabRow.style.marginBottom = 4;

            var objectTab = new Button(() => _SwitchViewMode(ViewMode.Objects));
            objectTab.text = "Objects";
            objectTab.style.flexGrow = 1;
            objectTab.name = "tab-objects";
            tabRow.Add(objectTab);

            var classTab = new Button(() => _SwitchViewMode(ViewMode.Types));
            classTab.text = "Types";
            classTab.style.flexGrow = 1;
            classTab.name = "tab-classes";
            tabRow.Add(classTab);

            var enumTab = new Button(() => _SwitchViewMode(ViewMode.Enums));
            enumTab.text = "Enums";
            enumTab.style.flexGrow = 1;
            enumTab.name = "tab-enums";
            tabRow.Add(enumTab);

            sidePanel.Add(tabRow);

            // フィルタ
            _filterField = new TextField();
            _filterField.style.marginLeft = 4;
            _filterField.style.marginRight = 4;
            _filterField.style.marginBottom = 4;
            _filterField.value = "";
            var placeholder = new Label("Filter...");
            placeholder.style.position = Position.Absolute;
            placeholder.style.left = 16;
            placeholder.style.top = 3;
            placeholder.style.color = new Color(0.5f, 0.5f, 0.5f);
            placeholder.pickingMode = PickingMode.Ignore;
            _filterField.Add(placeholder);
            _filterField.RegisterValueChangedCallback(evt =>
            {
                _filterText = evt.newValue ?? "";
                placeholder.style.display = string.IsNullOrEmpty(_filterText) ? DisplayStyle.Flex : DisplayStyle.None;
                _RebuildSidePanel();
            });
            sidePanel.Add(_filterField);

            // カウントラベル
            var countLabel = new Label();
            countLabel.name = "count-label";
            countLabel.style.marginLeft = 8;
            countLabel.style.marginBottom = 4;
            countLabel.style.fontSize = 10;
            countLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            sidePanel.Add(countLabel);

            // 型リスト (ScrollView)
            var sideScrollView = new ScrollView(ScrollViewMode.Vertical);
            sideScrollView.style.flexGrow = 1;

            _typeList = new VisualElement();
            sideScrollView.Add(_typeList);

            sidePanel.Add(sideScrollView);

            // リセットボタン
            var resetButton = new Button(() =>
            {
                if (EditorUtility.DisplayDialog(
                    "Reset All",
                    "Are you sure you want to reset all ExposedObjects, Types, and Enums?",
                    "Reset",
                    "Cancel"))
                {
                    ExposedObjectRegistry.ClearAll();
                    ExposedClass.Reset();
                    ExposedEnum.Reset();
                    _selectedItem = null;
                    _contentArea?.Clear();
                    _contentScrollView = null;
                    _RebuildSidePanel();
                }
            });
            resetButton.text = "Reset All";
            resetButton.style.marginLeft = 4;
            resetButton.style.marginRight = 4;
            resetButton.style.marginBottom = 4;
            resetButton.style.marginTop = 4;
            sidePanel.Add(resetButton);

            root.Add(sidePanel);

            // コンテンツエリア
            _contentArea = new VisualElement();
            _contentArea.style.flexGrow = 1;
            _contentArea.style.flexDirection = FlexDirection.Column;
            root.Add(_contentArea);

            _UpdateTabHighlight();
            _RebuildSidePanel();
        }

        private void _SwitchViewMode(ViewMode mode)
        {
            _viewMode = mode;
            _selectedItem = null;
            _contentArea?.Clear();
            _contentScrollView = null;
            _UpdateTabHighlight();
            _RebuildSidePanel();
        }

        private void _UpdateTabHighlight()
        {
            var root = rootVisualElement;
            var classTab = root.Q<Button>("tab-classes");
            var enumTab = root.Q<Button>("tab-enums");
            var objectTab = root.Q<Button>("tab-objects");
            if (classTab == null || enumTab == null || objectTab == null) return;

            var activeColor = new Color(0.2f, 0.4f, 0.6f, 0.5f);
            classTab.style.backgroundColor = _viewMode == ViewMode.Types ? activeColor : StyleKeyword.Null;
            enumTab.style.backgroundColor = _viewMode == ViewMode.Enums ? activeColor : StyleKeyword.Null;
            objectTab.style.backgroundColor = _viewMode == ViewMode.Objects ? activeColor : StyleKeyword.Null;
        }

        private void _RebuildSidePanel()
        {
            if (_typeList == null) return;
            _typeList.Clear();

            if (_viewMode == ViewMode.Types)
                _RebuildClassList();
            else if (_viewMode == ViewMode.Enums)
                _RebuildEnumList();
            else
                _RebuildObjectList();
        }

        // --- ExposedClass リスト ---

        private void _RebuildClassList()
        {
            var filtered = new List<ExposedClass>();
            foreach (var kvp in ExposedClass.all)
            {
                var ec = kvp.Value;
                if (_MatchesClassFilter(ec))
                    filtered.Add(ec);
            }

            filtered.Sort((a, b) => string.Compare(a.typeName, b.typeName, System.StringComparison.Ordinal));

            _UpdateCountLabel(filtered.Count, "types");

            foreach (var ec in filtered)
            {
                var button = new Button(() =>
                {
                    _selectedItem = ec;
                    _ShowClassDetails(ec);
                    _UpdateTypeListHighlight();
                });

                var displayText = ec.typeName;
                if (!string.IsNullOrEmpty(ec.category))
                    displayText += $"  [{ec.category}]";

                button.text = displayText;
                button.style.height = 24;
                button.style.marginLeft = 4;
                button.style.marginRight = 4;
                button.style.marginTop = 1;
                button.style.marginBottom = 1;
                button.style.overflow = Overflow.Hidden;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                button.style.fontSize = 11;

                if (ec.isStatic)
                    button.style.color = new Color(0.4f, 0.7f, 0.4f);

                if (_selectedItem == (object)ec)
                    button.style.backgroundColor = new Color(0.2f, 0.4f, 0.6f, 0.5f);

                button.userData = ec;
                _typeList.Add(button);
            }
        }

        private bool _MatchesClassFilter(ExposedClass ec)
        {
            if (string.IsNullOrEmpty(_filterText)) return true;
            if (ec.typeName != null && ec.typeName.IndexOf(_filterText, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (ec.type != null && ec.type.FullName != null && ec.type.FullName.IndexOf(_filterText, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (ec.category != null && ec.category.IndexOf(_filterText, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // --- ExposedEnum リスト ---

        private void _RebuildEnumList()
        {
            var filtered = new List<ExposedEnum>();
            foreach (var kvp in ExposedEnum.all)
            {
                var ee = kvp.Value;
                if (_MatchesEnumFilter(ee))
                    filtered.Add(ee);
            }

            filtered.Sort((a, b) => string.Compare(a.typeName, b.typeName, System.StringComparison.Ordinal));

            _UpdateCountLabel(filtered.Count, "types");

            foreach (var ee in filtered)
            {
                var button = new Button(() =>
                {
                    _selectedItem = ee;
                    _ShowEnumDetails(ee);
                    _UpdateTypeListHighlight();
                });

                button.text = ee.typeName;
                button.style.height = 24;
                button.style.marginLeft = 4;
                button.style.marginRight = 4;
                button.style.marginTop = 1;
                button.style.marginBottom = 1;
                button.style.overflow = Overflow.Hidden;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                button.style.fontSize = 11;

                if (_selectedItem == (object)ee)
                    button.style.backgroundColor = new Color(0.2f, 0.4f, 0.6f, 0.5f);

                button.userData = ee;
                _typeList.Add(button);
            }
        }

        private bool _MatchesEnumFilter(ExposedEnum ee)
        {
            if (string.IsNullOrEmpty(_filterText)) return true;
            if (ee.typeName != null && ee.typeName.IndexOf(_filterText, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (ee.type != null && ee.type.FullName != null && ee.type.FullName.IndexOf(_filterText, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // --- ExposedObject リスト ---

        private void _RebuildObjectList()
        {
            var filtered = new List<ExposedObject>();
            foreach (var obj in ExposedObjectRegistry.instances)
            {
                if (obj == null) continue;
                if (_MatchesObjectFilter(obj))
                    filtered.Add(obj);
            }

            _UpdateCountLabel(filtered.Count, "objects");

            foreach (var obj in filtered)
            {
                var button = new Button(() =>
                {
                    _selectedItem = obj;
                    _ShowObjectDetails(obj);
                    _UpdateTypeListHighlight();
                });

                var displayName = obj.name ?? obj.id;
                if (obj.isDirty) displayName += " *";
                button.text = displayName;

                button.style.height = 24;
                button.style.marginLeft = 4;
                button.style.marginRight = 4;
                button.style.marginTop = 1;
                button.style.marginBottom = 1;
                button.style.overflow = Overflow.Hidden;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                button.style.fontSize = 11;

                if (!obj.isValid)
                {
                    button.style.color = new Color(0.8f, 0.3f, 0.3f);
                }
                else if (!obj.hasId)
                {
                    button.style.color = new Color(0.5f, 0.8f, 0.9f);
                }
                else if (obj.targetType != null && obj.targetType.isStatic)
                {
                    button.style.color = new Color(0.4f, 0.7f, 0.4f);
                }

                if (_selectedItem == (object)obj)
                {
                    button.style.backgroundColor = new Color(0.2f, 0.4f, 0.6f, 0.5f);
                }

                button.userData = obj;
                _typeList.Add(button);
            }

            // 選択中オブジェクトが無効になった場合クリア
            var selectedObject = _selectedItem as ExposedObject;
            if (selectedObject != null && !selectedObject.isValid)
            {
                _selectedItem = null;
                _ShowObjectDetails(null);
            }
        }

        private bool _MatchesObjectFilter(ExposedObject obj)
        {
            if (string.IsNullOrEmpty(_filterText)) return true;
            if (obj.name != null && obj.name.IndexOf(_filterText, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (obj.id != null && obj.id.IndexOf(_filterText, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (obj.targetTypeName != null && obj.targetTypeName.IndexOf(_filterText, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            var cat = obj.targetType?.category;
            if (cat != null && cat.IndexOf(_filterText, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // --- 共通 ---

        private void _UpdateCountLabel(int count, string unit)
        {
            var label = rootVisualElement.Q<Label>("count-label");
            if (label != null)
                label.text = $"{count} {unit}";
        }

        private void _UpdateTypeListHighlight()
        {
            foreach (var child in _typeList.Children())
            {
                if (child is Button button)
                {
                    button.style.backgroundColor = (button.userData == _selectedItem)
                        ? new Color(0.2f, 0.4f, 0.6f, 0.5f)
                        : StyleKeyword.Null;
                }
            }
        }

        // --- ExposedClass 詳細 ---

        private void _ShowClassDetails(ExposedClass ec)
        {
            _contentArea.Clear();
            _contentScrollView = null;

            // ヘッダー
            var headerContainer = new VisualElement();
            headerContainer.style.paddingLeft = 8;
            headerContainer.style.paddingTop = 8;
            headerContainer.style.paddingBottom = 8;
            headerContainer.style.borderBottomWidth = 1;
            headerContainer.style.borderBottomColor = new Color(0.15f, 0.15f, 0.15f);

            var header = new Label(ec.typeName);
            header.style.fontSize = 14;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 4;
            headerContainer.Add(header);

            var typeLabel = new Label($"Type: {ec.type.FullName}");
            typeLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            typeLabel.style.fontSize = 11;
            headerContainer.Add(typeLabel);

            if (!string.IsNullOrEmpty(ec.category))
            {
                var catLabel = new Label($"Category: {ec.category}");
                catLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                catLabel.style.fontSize = 11;
                headerContainer.Add(catLabel);
            }

            if (!string.IsNullOrEmpty(ec.icon))
            {
                var iconLabel = new Label($"Icon: {ec.icon}");
                iconLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                iconLabel.style.fontSize = 11;
                headerContainer.Add(iconLabel);
            }

            if (ec.isStatic)
            {
                var staticLabel = new Label("static class");
                staticLabel.style.color = new Color(0.4f, 0.7f, 0.4f);
                staticLabel.style.fontSize = 11;
                headerContainer.Add(staticLabel);
            }

            if (!string.IsNullOrEmpty(ec.help))
            {
                var helpLabel = new Label(ec.help);
                helpLabel.style.color = new Color(0.6f, 0.6f, 0.5f);
                helpLabel.style.fontSize = 11;
                helpLabel.style.marginTop = 4;
                helpLabel.style.whiteSpace = WhiteSpace.Normal;
                headerContainer.Add(helpLabel);
            }

            _contentArea.Add(headerContainer);

            // スクロール可能なコンテンツ
            _contentScrollView = new ScrollView(ScrollViewMode.Vertical);
            _contentScrollView.style.flexGrow = 1;
            _contentScrollView.style.paddingLeft = 8;
            _contentScrollView.style.paddingTop = 8;
            _contentScrollView.style.paddingRight = 8;

            // プロパティ一覧
            if (ec.propertyTypes != null && ec.propertyTypes.Length > 0)
            {
                var propHeader = new Label($"Properties ({ec.propertyTypes.Length})");
                propHeader.style.fontSize = 12;
                propHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                propHeader.style.marginBottom = 4;
                _contentScrollView.Add(propHeader);

                var sorted = ec.propertyTypes.OrderBy(p => p.order).ToArray();
                foreach (var propType in sorted)
                {
                    var row = _CreatePropertyRow(propType);
                    _contentScrollView.Add(row);
                }
            }

            // ファンクション一覧
            if (ec.functionTypes != null && ec.functionTypes.Length > 0)
            {
                var funcHeader = new Label($"Functions ({ec.functionTypes.Length})");
                funcHeader.style.fontSize = 12;
                funcHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                funcHeader.style.marginTop = 12;
                funcHeader.style.marginBottom = 4;
                _contentScrollView.Add(funcHeader);

                var sortedFuncs = ec.functionTypes.OrderBy(f => f.order).ToArray();
                foreach (var funcType in sortedFuncs)
                {
                    var row = _CreateFunctionRow(funcType);
                    _contentScrollView.Add(row);
                }
            }

            if ((ec.propertyTypes == null || ec.propertyTypes.Length == 0) &&
                (ec.functionTypes == null || ec.functionTypes.Length == 0))
            {
                var empty = new Label("No properties or functions");
                empty.style.color = new Color(0.5f, 0.5f, 0.5f);
                empty.style.marginTop = 8;
                _contentScrollView.Add(empty);
            }

            _contentArea.Add(_contentScrollView);
        }

        private VisualElement _CreatePropertyRow(ExposedPropertyType propType)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;
            row.style.alignItems = Align.Center;

            // 名前
            var nameLabel = new Label(propType.name);
            nameLabel.style.width = kPropertyNameWidth;
            nameLabel.style.minWidth = kPropertyNameWidth;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(nameLabel);

            // 型名
            var typeName = propType.valueType != null ? propType.valueType.Name : "?";
            var typeLabel = new Label(typeName);
            typeLabel.style.color = new Color(0.6f, 0.7f, 0.8f);
            typeLabel.style.width = 120;
            typeLabel.style.minWidth = 120;
            typeLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(typeLabel);

            // コントロールタイプ
            if (propType.controlAttribute != null && propType.controlAttribute.controlName != "default")
            {
                var ctrlLabel = new Label(propType.controlAttribute.controlName);
                ctrlLabel.style.color = new Color(0.7f, 0.6f, 0.5f);
                ctrlLabel.style.marginLeft = 4;
                ctrlLabel.style.flexShrink = 0;
                row.Add(ctrlLabel);
            }

            // バッジ
            if (propType.isReadOnly)
            {
                var badge = new Label("[R]");
                badge.style.color = new Color(0.4f, 0.4f, 0.6f);
                badge.style.marginLeft = 4;
                badge.style.flexShrink = 0;
                row.Add(badge);
            }

            if (propType.isStatic)
            {
                var badge = new Label("[S]");
                badge.style.color = new Color(0.4f, 0.6f, 0.4f);
                badge.style.marginLeft = 4;
                badge.style.flexShrink = 0;
                row.Add(badge);
            }

            if (propType.isPersistable)
            {
                var badge = new Label("[P]");
                badge.style.color = new Color(0.6f, 0.6f, 0.4f);
                badge.style.marginLeft = 4;
                badge.style.flexShrink = 0;
                row.Add(badge);
            }

            return row;
        }

        private VisualElement _CreateFunctionRow(ExposedFunctionType funcType)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;
            row.style.alignItems = Align.Center;

            // 関数名 + パラメータ
            var paramText = "";
            if (funcType.parameters != null && funcType.parameters.Length > 0)
            {
                paramText = string.Join(", ", funcType.parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
            }

            var returnTypeName = funcType.returnType != null && funcType.returnType != typeof(void) ? funcType.returnType.Name : "void";
            var displayName = $"{returnTypeName}  {funcType.name}({paramText})";

            var nameLabel = new Label(displayName);
            nameLabel.style.flexGrow = 1;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(nameLabel);

            if (funcType.isStatic)
            {
                var badge = new Label("[S]");
                badge.style.color = new Color(0.4f, 0.6f, 0.4f);
                badge.style.marginLeft = 4;
                badge.style.flexShrink = 0;
                row.Add(badge);
            }

            return row;
        }

        // --- ExposedEnum 詳細 ---

        private void _ShowEnumDetails(ExposedEnum ee)
        {
            _contentArea.Clear();
            _contentScrollView = null;

            // ヘッダー
            var headerContainer = new VisualElement();
            headerContainer.style.paddingLeft = 8;
            headerContainer.style.paddingTop = 8;
            headerContainer.style.paddingBottom = 8;
            headerContainer.style.borderBottomWidth = 1;
            headerContainer.style.borderBottomColor = new Color(0.15f, 0.15f, 0.15f);

            var header = new Label(ee.typeName);
            header.style.fontSize = 14;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 4;
            headerContainer.Add(header);

            var typeLabel = new Label($"Type: {ee.type.FullName}");
            typeLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            typeLabel.style.fontSize = 11;
            headerContainer.Add(typeLabel);

            if (!string.IsNullOrEmpty(ee.help))
            {
                var helpLabel = new Label(ee.help);
                helpLabel.style.color = new Color(0.6f, 0.6f, 0.5f);
                helpLabel.style.fontSize = 11;
                helpLabel.style.marginTop = 4;
                helpLabel.style.whiteSpace = WhiteSpace.Normal;
                headerContainer.Add(helpLabel);
            }

            _contentArea.Add(headerContainer);

            // スクロール可能なコンテンツ
            _contentScrollView = new ScrollView(ScrollViewMode.Vertical);
            _contentScrollView.style.flexGrow = 1;
            _contentScrollView.style.paddingLeft = 8;
            _contentScrollView.style.paddingTop = 8;
            _contentScrollView.style.paddingRight = 8;

            if (ee.values != null && ee.values.Length > 0)
            {
                var valuesHeader = new Label($"Values ({ee.values.Length})");
                valuesHeader.style.fontSize = 12;
                valuesHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                valuesHeader.style.marginBottom = 4;
                _contentScrollView.Add(valuesHeader);

                foreach (var val in ee.values)
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.marginBottom = 2;
                    row.style.paddingTop = 2;
                    row.style.paddingBottom = 2;
                    row.style.alignItems = Align.Center;

                    var nameLabel = new Label(val.name);
                    nameLabel.style.width = kPropertyNameWidth;
                    nameLabel.style.minWidth = kPropertyNameWidth;
                    nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                    row.Add(nameLabel);

                    var valueLabel = new Label($"= {val.value}");
                    valueLabel.style.color = new Color(0.6f, 0.7f, 0.8f);
                    valueLabel.style.width = 80;
                    valueLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                    row.Add(valueLabel);

                    if (val.displayName != val.name)
                    {
                        var displayLabel = new Label(val.displayName);
                        displayLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                        displayLabel.style.marginLeft = 8;
                        row.Add(displayLabel);
                    }

                    _contentScrollView.Add(row);
                }
            }
            else
            {
                var empty = new Label("No values");
                empty.style.color = new Color(0.5f, 0.5f, 0.5f);
                _contentScrollView.Add(empty);
            }

            _contentArea.Add(_contentScrollView);
        }

        // --- ExposedObject 詳細 ---

        private void _ShowObjectDetails(ExposedObject obj)
        {
            _contentArea.Clear();
            _contentScrollView = null;

            if (obj == null || !obj.isValid)
            {
                if (obj != null)
                {
                    var invalid = new Label("Invalid object");
                    invalid.style.color = new Color(0.8f, 0.3f, 0.3f);
                    invalid.style.paddingLeft = 8;
                    invalid.style.paddingTop = 8;
                    _contentArea.Add(invalid);
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
            var header = new Label(obj.name);
            header.style.fontSize = 14;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 4;
            headerContainer.Add(header);

            // ID
            var idLabel = new Label($"ID: {obj.id}");
            idLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            idLabel.style.fontSize = 11;
            headerContainer.Add(idLabel);

            _contentArea.Add(headerContainer);

            // スクロール可能なコンテンツ
            _contentScrollView = new ScrollView(ScrollViewMode.Vertical);
            _contentScrollView.style.flexGrow = 1;
            _contentScrollView.style.paddingLeft = 8;
            _contentScrollView.style.paddingTop = 8;
            _contentScrollView.style.paddingRight = 8;

            // プロパティ一覧
            var propertyTypes = obj.propertyTypes;
            if (propertyTypes != null && propertyTypes.Length > 0)
            {
                var propHeader = new Label("Properties");
                propHeader.style.fontSize = 12;
                propHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                propHeader.style.marginBottom = 4;
                _contentScrollView.Add(propHeader);

                var sorted = propertyTypes.OrderBy(p => p.order).ToArray();
                foreach (var propType in sorted)
                {
                    var row = _CreateObjectPropertyRow(obj, propType);
                    _contentScrollView.Add(row);
                }
            }

            // ファンクション一覧
            var functionTypes = obj.targetType?.functionTypes;
            if (functionTypes != null && functionTypes.Length > 0)
            {
                var funcHeader = new Label("Functions");
                funcHeader.style.fontSize = 12;
                funcHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                funcHeader.style.marginTop = 12;
                funcHeader.style.marginBottom = 4;
                _contentScrollView.Add(funcHeader);

                var sortedFuncs = functionTypes.OrderBy(f => f.order).ToArray();
                foreach (var funcType in sortedFuncs)
                {
                    var row = _CreateObjectFunctionRow(obj, funcType);
                    _contentScrollView.Add(row);
                }
            }

            _contentArea.Add(_contentScrollView);
        }

        private VisualElement _CreateObjectPropertyRow(ExposedObject obj, ExposedPropertyType propType)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;
            row.style.alignItems = Align.Center;

            // 名前ラベル
            var nameLabel = new Label(propType.name);
            nameLabel.style.width = kPropertyNameWidth;
            nameLabel.style.minWidth = kPropertyNameWidth;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            nameLabel.name = "prop-name";

            if (obj.IsPropertyDirty(propType.name))
            {
                nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            }

            row.Add(nameLabel);

            // 値ラベル
            var valueText = _GetObjectPropertyValueText(obj, propType);
            var valueLabel = new Label(valueText);
            valueLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            valueLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            valueLabel.style.flexGrow = 1;
            valueLabel.style.flexShrink = 1;
            valueLabel.style.overflow = Overflow.Hidden;
            valueLabel.name = "prop-value";
            row.Add(valueLabel);

            // バッジ
            if (propType.isReadOnly)
            {
                var badge = new Label("[R]");
                badge.style.color = new Color(0.4f, 0.4f, 0.6f);
                badge.style.marginLeft = 4;
                badge.style.flexShrink = 0;
                row.Add(badge);
            }

            if (propType.isStatic)
            {
                var badge = new Label("[S]");
                badge.style.color = new Color(0.4f, 0.6f, 0.4f);
                badge.style.marginLeft = 4;
                badge.style.flexShrink = 0;
                row.Add(badge);
            }

            // プロパティ名をuserDataに保存（値更新用）
            row.userData = propType.name;

            return row;
        }

        private VisualElement _CreateObjectFunctionRow(ExposedObject obj, ExposedFunctionType funcType)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;
            row.style.alignItems = Align.Center;

            // 関数名 + パラメータ
            var paramText = "";
            if (funcType.parameters != null && funcType.parameters.Length > 0)
            {
                paramText = string.Join(", ", funcType.parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
            }
            var displayName = $"{funcType.name}({paramText})";

            var nameLabel = new Label(displayName);
            nameLabel.style.flexGrow = 1;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(nameLabel);

            if (funcType.isStatic)
            {
                var badge = new Label("[S]");
                badge.style.color = new Color(0.4f, 0.6f, 0.4f);
                badge.style.marginLeft = 4;
                badge.style.flexShrink = 0;
                row.Add(badge);
            }

            // Invokeボタン（引数なしメソッドのみ）
            if (funcType.parameters == null || funcType.parameters.Length == 0)
            {
                var invokeButton = new Button(() =>
                {
                    obj.InvokeFunction(funcType.apiName, null);
                });
                invokeButton.text = "Invoke";
                invokeButton.style.width = 60;
                invokeButton.style.flexShrink = 0;
                row.Add(invokeButton);
            }

            return row;
        }

        private string _GetObjectPropertyValueText(ExposedObject obj, ExposedPropertyType propType)
        {
            var prop = obj.FindProperty(propType.name);
            if (prop == null) return "(not found)";

            try
            {
                var value = prop.Value.GetValue();
                return value != null ? value.ToString() : "null";
            }
            catch
            {
                return "(error)";
            }
        }

        private void _UpdateObjectPropertyValues(ExposedObject obj)
        {
            if (obj == null || !obj.isValid || _contentScrollView == null) return;

            foreach (var child in _contentScrollView.Children())
            {
                var propName = child.userData as string;
                if (propName == null) continue;

                var propType = obj.targetType?.FindProperty(propName);
                if (propType == null) continue;

                // 値ラベル更新
                var valueLabel = child.Q<Label>("prop-value");
                if (valueLabel != null)
                {
                    valueLabel.text = _GetObjectPropertyValueText(obj, propType);
                }

                // dirty状態更新
                var nameLabel = child.Q<Label>("prop-name");
                if (nameLabel != null)
                {
                    nameLabel.style.unityFontStyleAndWeight = obj.IsPropertyDirty(propName)
                        ? FontStyle.Bold
                        : FontStyle.Normal;
                }
            }
        }
    }
}
