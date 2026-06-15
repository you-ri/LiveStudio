// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Places the current avatar at a selected <see cref="StageMark"/> from the remote app.
    ///
    /// The remote app shows <see cref="place"/> as a dropdown whose candidates come from
    /// <see cref="availablePlaces"/>: always a built-in <c>Zero</c> origin entry, plus every
    /// registered <see cref="StageMark"/>. Selecting an entry warps the avatar's anchor
    /// (position and rotation) to that mark.
    /// </summary>
    [Serializable]
    [ExposedClass(Category = "Avatar", Icon = "place")]
    public class AvatarStageController : IExposedObject
    {
        const string kId = "5f0c2a1e-8d34-4b27-9a6c-1e7b3f9d4c80";

        // Built-in entry that always exists, even with no marks in the scene. Warps to origin.
        const string kZeroPlace = "Zero";

        public string name { get; set; } = "Avatar Placement";

        public ExposedObjectHandle? exposedObject => ExposedObjectRegistry.FindByTarget(this);

        public string id => kId;

        private string _place = kZeroPlace;

        /// <summary>
        /// Currently selected place. Setting it warps the avatar to the matching mark
        /// (or to the origin for <see cref="kZeroPlace"/>).
        /// </summary>
        [ExposedProperty]
        [StringSelector(nameof(availablePlaces))]
        public string place
        {
            get => _place;
            set
            {
                _place = value;
                _WarpTo(value);
            }
        }

        /// <summary>
        /// Dropdown candidates: the built-in <see cref="kZeroPlace"/> followed by the label of
        /// every registered <see cref="StageMark"/>. Evaluated on each read, so it tracks marks
        /// added/removed by scene bundles automatically.
        /// </summary>
        [ExposedProperty, Hide]
        public string[] availablePlaces
        {
            get
            {
                var names = new List<string> { kZeroPlace };
                foreach (var mark in StageMarkRegistry.marks)
                {
                    if (mark == null) continue;
                    var label = mark.label;
                    if (!string.IsNullOrEmpty(label)) names.Add(label);
                }
                return names.Distinct().ToArray();
            }
        }

        public void OnEnable()
        {
            ExposedObjectRegistry.Create<AvatarStageController>(this, kId);
            StageMarkRegistry.onChanged += _OnMarksChanged;
        }

        public void OnDisable()
        {
            StageMarkRegistry.onChanged -= _OnMarksChanged;
            ExposedObjectRegistry.FindByTarget(this)?.Unregister();
        }

        public void OnDispose()
        {
            OnDisable();
        }

        public void Update()
        {
        }

        public void Reset()
        {
        }

        // The candidate set changed; push the updated list to connected clients so the dropdown
        // refreshes without a manual refetch.
        private void _OnMarksChanged()
        {
            ExposedPropertyBroadcast.BroadcastProperty(this, nameof(availablePlaces));
        }

        private void _WarpTo(string placeName)
        {
            Vector3 targetPosition;
            Quaternion targetRotation;

            if (string.IsNullOrEmpty(placeName) || placeName == kZeroPlace)
            {
                targetPosition = Vector3.zero;
                targetRotation = Quaternion.identity;
            }
            else
            {
                var mark = _FindMark(placeName);
                if (mark == null)
                {
                    Debug.LogWarning($"[LiveStudio] Stage mark not found: {placeName}");
                    return;
                }
                targetPosition = mark.position;
                targetRotation = mark.rotation;
            }

            // The avatar's world placement is driven by its anchor, which AvatarController sets to
            // its own transform. The animator root is rewritten every frame relative to that anchor,
            // so moving the anchor (not the animator GameObject) is what actually warps the avatar.
            var anchor = (SingletonService<IAvatarService>.subject as MonoBehaviour)?.transform;
            if (anchor == null)
            {
                Debug.LogWarning("[LiveStudio] No active avatar to place.");
                return;
            }

            anchor.SetPositionAndRotation(targetPosition, targetRotation);
        }

        private static StageMark _FindMark(string label)
        {
            foreach (var mark in StageMarkRegistry.marks)
            {
                if (mark != null && mark.label == label) return mark;
            }
            return null;
        }
    }
}
