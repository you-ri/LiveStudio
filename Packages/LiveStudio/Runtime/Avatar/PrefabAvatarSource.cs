// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [DefaultExecutionOrder(250)]
    [ExposedClass("PrefabAvatarSource", Category = "Avatar", Icon = "deployed_code")]
    public class PrefabAvatarSource : MonoBehaviour, IAvatarSource
    {
        public const string kNoneSelection = "None";

        public event Action<GameObject> onAvatarReady;

        [SerializeField]
        GameObject[] _avatarPrefabs = Array.Empty<GameObject>();

        [SerializeField]
        [ExposedField(label = "AVATAR_SELECTEDPREFAB"), StringSelector(nameof(avatarPrefabNames))]
        [ExposedHelp("AVATAR_SELECTEDPREFAB_HELP")]
        string _selectedAvatarPrefab = kNoneSelection;

        public string selectedAvatarPrefab => _selectedAvatarPrefab;

        [ExposedProperty, Hide]
        public string[] avatarPrefabNames
        {
            get
            {
                var names = new List<string> { kNoneSelection };
                if (_avatarPrefabs != null)
                {
                    names.AddRange(_avatarPrefabs
                        .Where(p => p != null)
                        .Select(p => p.name)
                        .Distinct());
                }
                return names.ToArray();
            }
        }

        bool _initialized;

        void OnEnable()
        {
            ExposedClass.Get<PrefabAvatarSource>().onPropertyChanged += OnPropertyChanged;
        }

        void OnDisable()
        {
            ExposedClass.Get<PrefabAvatarSource>().onPropertyChanged -= OnPropertyChanged;
        }

        void Start()
        {
            _initialized = true;
            _ApplySelectedAvatarPrefab();
        }

        void _ApplySelectedAvatarPrefab()
        {
            if (string.IsNullOrEmpty(_selectedAvatarPrefab) || _selectedAvatarPrefab == kNoneSelection)
            {
                return;
            }
            if (_avatarPrefabs == null) return;

            var prefab = _avatarPrefabs.FirstOrDefault(p => p != null && p.name == _selectedAvatarPrefab);
            if (prefab == null)
            {
                Debug.LogWarning($"[LiveStudio] Avatar prefab '{_selectedAvatarPrefab}' not found in _avatarPrefabs.");
                return;
            }

            var newTarget = GameObjectUtility.InstantiatePrefabWithUndo(prefab, this.transform);
            onAvatarReady?.Invoke(newTarget);
        }

        void OnPropertyChanged(ExposedProperty property, object oldValue)
        {
            // Skip until Start() runs. The scene-JSON load fires this during
            // RemoteControlBehaviour.Start (well before our own Start), and
            // Start would then spawn a second instance.
            if (!_initialized) return;
            if (property.PathContains(nameof(_selectedAvatarPrefab)))
            {
                _ApplySelectedAvatarPrefab();
            }
        }
    }
}
