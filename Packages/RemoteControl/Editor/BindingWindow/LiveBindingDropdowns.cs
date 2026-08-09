// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>
    /// Searchable class picker (UE Remote Control style dropdown with a search field):
    /// preset-defined types first, then every pickable Component / ScriptableObject type
    /// grouped by namespace.
    /// </summary>
    internal class LiveBindingTypeDropdown : AdvancedDropdown
    {
        private readonly List<Type> _presetTypes;
        private readonly Func<IEnumerable<Type>> _enumerateTypes;
        private readonly Action<Type> _onSelected;
        private readonly List<Type> _typesById = new List<Type>();

        public LiveBindingTypeDropdown(AdvancedDropdownState state, List<Type> presetTypes,
            Func<IEnumerable<Type>> enumerateTypes, Action<Type> onSelected) : base(state)
        {
            _presetTypes = presetTypes;
            _enumerateTypes = enumerateTypes;
            _onSelected = onSelected;
            minimumSize = new Vector2(320f, 420f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            _typesById.Clear();
            var root = new AdvancedDropdownItem("Class");

            if (_presetTypes.Count > 0)
            {
                var presetGroup = new AdvancedDropdownItem("In Preset");
                foreach (var type in _presetTypes)
                {
                    presetGroup.AddChild(_MakeItem(type));
                }
                root.AddChild(presetGroup);
            }

            var groups = new SortedDictionary<string, List<Type>>(StringComparer.Ordinal);
            foreach (var type in _enumerateTypes())
            {
                string ns = string.IsNullOrEmpty(type.Namespace) ? "(no namespace)" : type.Namespace;
                if (!groups.TryGetValue(ns, out var list))
                {
                    groups[ns] = list = new List<Type>();
                }
                list.Add(type);
            }
            foreach (var pair in groups)
            {
                var folder = new AdvancedDropdownItem(pair.Key);
                pair.Value.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                foreach (var type in pair.Value)
                {
                    folder.AddChild(_MakeItem(type));
                }
                root.AddChild(folder);
            }
            return root;
        }

        private AdvancedDropdownItem _MakeItem(Type type)
        {
            // Leaf ids index into _typesById; group items are never selected (they navigate).
            var item = new AdvancedDropdownItem(type.Name) { id = _typesById.Count };
            _typesById.Add(type);
            return item;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item.id >= 0 && item.id < _typesById.Count)
            {
                _onSelected(_typesById[item.id]);
            }
        }
    }
}
