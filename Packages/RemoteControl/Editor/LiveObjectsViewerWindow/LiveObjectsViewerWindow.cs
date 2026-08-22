// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.Editor
{
    public class LiveObjectsViewerWindow : EditorWindow
    {
        private const string kStyleSheet = "Editor/LiveObjectsViewerWindow/LiveObjectsViewerWindow.uss";

        private enum ViewMode { Types, Enums, Objects }

        private ViewMode _viewMode = ViewMode.Objects;
        private VisualElement _typeList;
        private VisualElement _contentArea;
        private TextField _filterField;
        private ScrollView _contentScrollView;

        private string _filterText = "";
        private object _selectedItem; // LiveClass, LiveEnum, or LiveObjectHandle

        // Objects用
        private int _lastInstanceCount;

        // Whether to append the dirty mark (*) to object list entries. Off by default: each mark
        // costs a full persistence-shape serialization plus a JSON diff of that object
        // (LiveObjectDefaultRegistry.IsDirty), so computing it for every entry makes rebuilding the
        // list quadratic. Turn it on only when the marks are actually wanted.
        private bool _showDirtyMarks;

        // Throttle for the selected object's live property refresh. Refreshing every editor tick
        // re-serializes every property of that object each frame and slows the whole editor down.
        private const double kPropertyRefreshInterval = 0.25;
        private double _nextPropertyRefreshTime;

        [MenuItem("Window/Lilium Remote Control/LiveObjects Viewer")]
        public static void ShowWindow()
        {
            var window = GetWindow<LiveObjectsViewerWindow>();
            window.titleContent = new GUIContent("LiveObjects Viewer");
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
                var count = LiveObjectRegistry.instances.Count;
                if (count != _lastInstanceCount)
                {
                    _lastInstanceCount = count;
                    _RebuildSidePanel();
                }

                // 選択中オブジェクトのプロパティ値をリアルタイム更新 (kPropertyRefreshInterval で間引く)
                var selectedObject = _selectedItem as LiveObjectHandle?;
                if (selectedObject != null && _contentScrollView != null)
                {
                    var now = EditorApplication.timeSinceStartup;
                    if (now >= _nextPropertyRefreshTime)
                    {
                        _nextPropertyRefreshTime = now + kPropertyRefreshInterval;
                        _UpdateObjectPropertyValues(selectedObject.Value);
                    }
                }
            }
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            RemoteControlEditorStyles.Apply(root, kStyleSheet);
            root.AddToClassList("lov-root");

            // サイドパネル
            var sidePanel = new VisualElement();
            sidePanel.AddToClassList("lov-side-panel");

            // タブ切り替え
            var tabRow = new VisualElement();
            tabRow.AddToClassList("lov-tab-row");

            var objectTab = new Button(() => _SwitchViewMode(ViewMode.Objects));
            objectTab.text = "Objects";
            objectTab.AddToClassList("lov-tab");
            objectTab.name = "tab-objects";
            tabRow.Add(objectTab);

            var classTab = new Button(() => _SwitchViewMode(ViewMode.Types));
            classTab.text = "Types";
            classTab.AddToClassList("lov-tab");
            classTab.name = "tab-classes";
            tabRow.Add(classTab);

            var enumTab = new Button(() => _SwitchViewMode(ViewMode.Enums));
            enumTab.text = "Enums";
            enumTab.AddToClassList("lov-tab");
            enumTab.name = "tab-enums";
            tabRow.Add(enumTab);

            sidePanel.Add(tabRow);

            // フィルタ
            _filterField = new TextField();
            _filterField.AddToClassList("lov-filter");
            _filterField.value = "";
            var placeholder = new Label("Filter...");
            placeholder.AddToClassList("lov-filter__placeholder");
            placeholder.pickingMode = PickingMode.Ignore;
            _filterField.Add(placeholder);
            _filterField.RegisterValueChangedCallback(evt =>
            {
                _filterText = evt.newValue ?? "";
                placeholder.style.display = string.IsNullOrEmpty(_filterText) ? DisplayStyle.Flex : DisplayStyle.None;
                _RebuildSidePanel();
            });
            sidePanel.Add(_filterField);

            // Dirty mark toggle. See _showDirtyMarks for why this is opt-in.
            var dirtyToggle = new Toggle("Show dirty (*)");
            dirtyToggle.name = "dirty-toggle";
            dirtyToggle.value = _showDirtyMarks;
            dirtyToggle.AddToClassList("lov-dirty-toggle");
            dirtyToggle.RegisterValueChangedCallback(evt =>
            {
                _showDirtyMarks = evt.newValue;
                if (_viewMode == ViewMode.Objects)
                    _RebuildSidePanel();
            });
            sidePanel.Add(dirtyToggle);

            // カウントラベル
            var countLabel = new Label();
            countLabel.name = "count-label";
            countLabel.AddToClassList("lov-count");
            sidePanel.Add(countLabel);

            // 型リスト (ScrollView)
            var sideScrollView = new ScrollView(ScrollViewMode.Vertical);
            sideScrollView.AddToClassList(RemoteControlEditorStyles.kScroll);

            _typeList = new VisualElement();
            sideScrollView.Add(_typeList);

            sidePanel.Add(sideScrollView);

            // リセットボタン
            var resetButton = new Button(() =>
            {
                if (EditorUtility.DisplayDialog(
                    "Reset All",
                    "Are you sure you want to reset all LiveObjects, Types, and Enums?",
                    "Reset",
                    "Cancel"))
                {
                    LiveObjectRegistry.ClearAll();
                    LiveClass.Reset();
                    LiveEnum.Reset();
                    _selectedItem = null;
                    _contentArea?.Clear();
                    _contentScrollView = null;
                    _RebuildSidePanel();
                }
            });
            resetButton.text = "Reset All";
            resetButton.AddToClassList("lov-reset-button");
            sidePanel.Add(resetButton);

            root.Add(sidePanel);

            // コンテンツエリア
            _contentArea = new VisualElement();
            _contentArea.AddToClassList("lov-content");
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

            classTab.EnableInClassList("lov-tab--active", _viewMode == ViewMode.Types);
            enumTab.EnableInClassList("lov-tab--active", _viewMode == ViewMode.Enums);
            objectTab.EnableInClassList("lov-tab--active", _viewMode == ViewMode.Objects);

            var dirtyToggle = root.Q<Toggle>("dirty-toggle");
            if (dirtyToggle != null)
            {
                dirtyToggle.style.display = _viewMode == ViewMode.Objects ? DisplayStyle.Flex : DisplayStyle.None;
            }
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

        // --- LiveClass リスト ---

        private void _RebuildClassList()
        {
            var filtered = new List<LiveClass>();
            foreach (var kvp in LiveClass.all)
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
                button.AddToClassList("lov-list-item");
                button.EnableInClassList("lov-list-item--static", ec.isStatic);
                button.EnableInClassList("lov-list-item--selected", _selectedItem == (object)ec);

                button.userData = ec;
                _typeList.Add(button);
            }
        }

        private bool _MatchesClassFilter(LiveClass ec)
        {
            if (string.IsNullOrEmpty(_filterText)) return true;
            if (ec.typeName != null && ec.typeName.IndexOf(_filterText, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (ec.type != null && ec.type.FullName != null && ec.type.FullName.IndexOf(_filterText, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (ec.category != null && ec.category.IndexOf(_filterText, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // --- LiveEnum リスト ---

        private void _RebuildEnumList()
        {
            var filtered = new List<LiveEnum>();
            foreach (var kvp in LiveEnum.all)
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
                button.AddToClassList("lov-list-item");
                button.EnableInClassList("lov-list-item--selected", _selectedItem == (object)ee);

                button.userData = ee;
                _typeList.Add(button);
            }
        }

        private bool _MatchesEnumFilter(LiveEnum ee)
        {
            if (string.IsNullOrEmpty(_filterText)) return true;
            if (ee.typeName != null && ee.typeName.IndexOf(_filterText, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (ee.type != null && ee.type.FullName != null && ee.type.FullName.IndexOf(_filterText, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // --- LiveObjectHandle リスト ---

        private void _RebuildObjectList()
        {
            var filtered = new List<LiveObjectHandle>();
            foreach (var obj in LiveObjectRegistry.instances)
            {
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
                if (_showDirtyMarks && obj.isDirty) displayName += " *";
                button.text = displayName;

                button.AddToClassList("lov-list-item");
                button.EnableInClassList("lov-list-item--invalid", !obj.isValid);
                button.EnableInClassList("lov-list-item--unregistered", obj.isValid && !obj.hasId);
                button.EnableInClassList("lov-list-item--static",
                    obj.isValid && obj.hasId && obj.targetType != null && obj.targetType.isStatic);
                button.EnableInClassList("lov-list-item--selected",
                    _selectedItem is LiveObjectHandle selObj && selObj.Equals(obj));

                button.userData = obj;
                _typeList.Add(button);
            }

            // 選択中オブジェクトが無効になった場合クリア
            var selectedObject = _selectedItem as LiveObjectHandle?;
            if (selectedObject != null && !selectedObject.Value.isValid)
            {
                _selectedItem = null;
                _ShowObjectDetails(null);
            }
        }

        private bool _MatchesObjectFilter(LiveObjectHandle obj)
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
                    button.EnableInClassList("lov-list-item--selected", button.userData == _selectedItem);
                }
            }
        }

        // --- LiveClass 詳細 ---

        private void _ShowClassDetails(LiveClass ec)
        {
            _contentArea.Clear();
            _contentScrollView = null;

            // ヘッダー
            var headerContainer = new VisualElement();
            headerContainer.AddToClassList("lov-detail-header");

            var header = new Label(ec.typeName);
            header.AddToClassList("lov-detail-title");
            headerContainer.Add(header);

            var typeLabel = new Label($"Type: {ec.type.FullName}");
            typeLabel.AddToClassList("lov-detail-meta");
            headerContainer.Add(typeLabel);

            if (!string.IsNullOrEmpty(ec.category))
            {
                var catLabel = new Label($"Category: {ec.category}");
                catLabel.AddToClassList("lov-detail-meta");
                headerContainer.Add(catLabel);
            }

            if (!string.IsNullOrEmpty(ec.icon))
            {
                var iconLabel = new Label($"Icon: {ec.icon}");
                iconLabel.AddToClassList("lov-detail-meta");
                headerContainer.Add(iconLabel);
            }

            if (ec.isStatic)
            {
                var staticLabel = new Label("static class");
                staticLabel.AddToClassList("lov-detail-static");
                headerContainer.Add(staticLabel);
            }

            if (!string.IsNullOrEmpty(ec.help))
            {
                var helpLabel = new Label(ec.help);
                helpLabel.AddToClassList("lov-detail-help");
                headerContainer.Add(helpLabel);
            }

            _contentArea.Add(headerContainer);

            // スクロール可能なコンテンツ
            _contentScrollView = new ScrollView(ScrollViewMode.Vertical);
            _contentScrollView.AddToClassList("lov-detail-scroll");

            // プロパティ一覧
            if (ec.propertyTypes != null && ec.propertyTypes.Length > 0)
            {
                var propHeader = new Label($"Properties ({ec.propertyTypes.Length})");
                propHeader.AddToClassList("lov-section-title");
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
                funcHeader.AddToClassList("lov-section-title");
                funcHeader.AddToClassList("lov-section-title--spaced");
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
                empty.AddToClassList("lov-empty");
                _contentScrollView.Add(empty);
            }

            _contentArea.Add(_contentScrollView);
        }

        private VisualElement _CreatePropertyRow(LivePropertyType propType)
        {
            var row = new VisualElement();
            row.AddToClassList("lov-row");

            // 名前
            var nameLabel = new Label(propType.name);
            nameLabel.AddToClassList("lov-row__name");
            row.Add(nameLabel);

            // 型名
            var typeName = propType.valueType != null ? propType.valueType.Name : "?";
            var typeLabel = new Label(typeName);
            typeLabel.AddToClassList("lov-row__type");
            row.Add(typeLabel);

            // コントロールタイプ
            if (propType.controlAttribute != null && propType.controlAttribute.controlName != "default")
            {
                var ctrlLabel = new Label(propType.controlAttribute.controlName);
                ctrlLabel.AddToClassList("lov-row__control");
                row.Add(ctrlLabel);
            }

            // バッジ
            if (propType.isReadOnly)
            {
                var badge = new Label("[R]");
                badge.AddToClassList("lov-badge");
                badge.AddToClassList("lov-badge--readonly");
                row.Add(badge);
            }

            if (propType.isStatic)
            {
                var badge = new Label("[S]");
                badge.AddToClassList("lov-badge");
                badge.AddToClassList("lov-badge--static");
                row.Add(badge);
            }

            if (propType.isPersistable)
            {
                var badge = new Label("[P]");
                badge.AddToClassList("lov-badge");
                badge.AddToClassList("lov-badge--persistable");
                row.Add(badge);
            }

            return row;
        }

        private VisualElement _CreateFunctionRow(LiveFunctionType funcType)
        {
            var row = new VisualElement();
            row.AddToClassList("lov-row");

            // 関数名 + パラメータ
            var paramText = "";
            if (funcType.parameters != null && funcType.parameters.Length > 0)
            {
                paramText = string.Join(", ", funcType.parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
            }

            var returnTypeName = funcType.returnType != null && funcType.returnType != typeof(void) ? funcType.returnType.Name : "void";
            var displayName = $"{returnTypeName}  {funcType.name}({paramText})";

            var nameLabel = new Label(displayName);
            nameLabel.AddToClassList("lov-row__function");
            row.Add(nameLabel);

            if (funcType.isStatic)
            {
                var badge = new Label("[S]");
                badge.AddToClassList("lov-badge");
                badge.AddToClassList("lov-badge--static");
                row.Add(badge);
            }

            return row;
        }

        // --- LiveEnum 詳細 ---

        private void _ShowEnumDetails(LiveEnum ee)
        {
            _contentArea.Clear();
            _contentScrollView = null;

            // ヘッダー
            var headerContainer = new VisualElement();
            headerContainer.AddToClassList("lov-detail-header");

            var header = new Label(ee.typeName);
            header.AddToClassList("lov-detail-title");
            headerContainer.Add(header);

            var typeLabel = new Label($"Type: {ee.type.FullName}");
            typeLabel.AddToClassList("lov-detail-meta");
            headerContainer.Add(typeLabel);

            if (!string.IsNullOrEmpty(ee.help))
            {
                var helpLabel = new Label(ee.help);
                helpLabel.AddToClassList("lov-detail-help");
                headerContainer.Add(helpLabel);
            }

            _contentArea.Add(headerContainer);

            // スクロール可能なコンテンツ
            _contentScrollView = new ScrollView(ScrollViewMode.Vertical);
            _contentScrollView.AddToClassList("lov-detail-scroll");

            if (ee.values != null && ee.values.Length > 0)
            {
                var valuesHeader = new Label($"Values ({ee.values.Length})");
                valuesHeader.AddToClassList("lov-section-title");
                _contentScrollView.Add(valuesHeader);

                foreach (var val in ee.values)
                {
                    var row = new VisualElement();
                    row.AddToClassList("lov-row");

                    var nameLabel = new Label(val.name);
                    nameLabel.AddToClassList("lov-row__name");
                    row.Add(nameLabel);

                    var valueLabel = new Label($"= {val.value}");
                    valueLabel.AddToClassList("lov-row__enum-value");
                    row.Add(valueLabel);

                    if (val.displayName != val.name)
                    {
                        var displayLabel = new Label(val.displayName);
                        displayLabel.AddToClassList("lov-row__enum-display");
                        row.Add(displayLabel);
                    }

                    _contentScrollView.Add(row);
                }
            }
            else
            {
                var empty = new Label("No values");
                empty.AddToClassList("lov-empty");
                _contentScrollView.Add(empty);
            }

            _contentArea.Add(_contentScrollView);
        }

        // --- LiveObjectHandle 詳細 ---

        private void _ShowObjectDetails(LiveObjectHandle? objOrNull)
        {
            _contentArea.Clear();
            _contentScrollView = null;

            if (objOrNull == null || !objOrNull.Value.isValid)
            {
                if (objOrNull != null)
                {
                    var invalid = new Label("Invalid object");
                    invalid.AddToClassList("lov-invalid");
                    _contentArea.Add(invalid);
                }
                return;
            }

            var obj = objOrNull.Value;

            // ヘッダー
            var headerContainer = new VisualElement();
            headerContainer.AddToClassList("lov-detail-header");

            // クラス名（上段）
            var classLabel = new Label(obj.targetTypeName);
            classLabel.AddToClassList("lov-detail-meta");
            headerContainer.Add(classLabel);

            // オブジェクト名（メインタイトル）
            var header = new Label(obj.name);
            header.AddToClassList("lov-detail-title");
            headerContainer.Add(header);

            // ID
            var idLabel = new Label($"ID: {obj.id}");
            idLabel.AddToClassList("lov-detail-meta");
            headerContainer.Add(idLabel);

            _contentArea.Add(headerContainer);

            // スクロール可能なコンテンツ
            _contentScrollView = new ScrollView(ScrollViewMode.Vertical);
            _contentScrollView.AddToClassList("lov-detail-scroll");

            // プロパティ一覧
            var propertyTypes = obj.propertyTypes;
            if (propertyTypes != null && propertyTypes.Length > 0)
            {
                var propHeader = new Label("Properties");
                propHeader.AddToClassList("lov-section-title");
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
                funcHeader.AddToClassList("lov-section-title");
                funcHeader.AddToClassList("lov-section-title--spaced");
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

        private VisualElement _CreateObjectPropertyRow(LiveObjectHandle obj, LivePropertyType propType)
        {
            var row = new VisualElement();
            row.AddToClassList("lov-row");

            // 名前ラベル
            var nameLabel = new Label(propType.name);
            nameLabel.AddToClassList("lov-row__name");
            nameLabel.name = "prop-name";

            nameLabel.EnableInClassList("lov-row__name--dirty", obj.IsPropertyDirty(propType.name));

            row.Add(nameLabel);

            // 値ラベル
            var valueText = _GetObjectPropertyValueText(obj, propType);
            var valueLabel = new Label(valueText);
            valueLabel.AddToClassList("lov-row__value");
            valueLabel.name = "prop-value";
            row.Add(valueLabel);

            // バッジ
            if (propType.isReadOnly)
            {
                var badge = new Label("[R]");
                badge.AddToClassList("lov-badge");
                badge.AddToClassList("lov-badge--readonly");
                row.Add(badge);
            }

            if (propType.isStatic)
            {
                var badge = new Label("[S]");
                badge.AddToClassList("lov-badge");
                badge.AddToClassList("lov-badge--static");
                row.Add(badge);
            }

            // プロパティ名をuserDataに保存（値更新用）
            row.userData = propType.name;

            return row;
        }

        private VisualElement _CreateObjectFunctionRow(LiveObjectHandle obj, LiveFunctionType funcType)
        {
            var row = new VisualElement();
            row.AddToClassList("lov-row");

            // 関数名 + パラメータ
            var paramText = "";
            if (funcType.parameters != null && funcType.parameters.Length > 0)
            {
                paramText = string.Join(", ", funcType.parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
            }
            var displayName = $"{funcType.name}({paramText})";

            var nameLabel = new Label(displayName);
            nameLabel.AddToClassList("lov-row__function");
            row.Add(nameLabel);

            if (funcType.isStatic)
            {
                var badge = new Label("[S]");
                badge.AddToClassList("lov-badge");
                badge.AddToClassList("lov-badge--static");
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
                invokeButton.AddToClassList("lov-invoke-button");
                row.Add(invokeButton);
            }

            return row;
        }

        private string _GetObjectPropertyValueText(LiveObjectHandle obj, LivePropertyType propType)
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

        private void _UpdateObjectPropertyValues(LiveObjectHandle obj)
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
                    nameLabel.EnableInClassList("lov-row__name--dirty", obj.IsPropertyDirty(propName));
                }
            }
        }
    }
}
