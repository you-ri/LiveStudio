// Copyright (c) You-Ri, 2026

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Shared tracking-visibility gate for avatar components that toggle their meshes
    /// with the motion source (StandardAvatar / VRCAvatar / VRM0Avatar). Tracks the
    /// on/off transition, hiding the renderers on tracking loss and showing them on
    /// recovery — flipping visibility only at the transition, not every frame.
    /// Behaviour matches the per-avatar implementations it replaces: renderers are
    /// looked up from the avatar root on each toggle.
    /// </summary>
    public struct AvatarVisibilityGate
    {
        Transform _root;
        bool _isTracking;

        /// <summary>True while the motion source is providing valid frames.</summary>
        public bool isTracking => _isTracking;

        /// <summary>Binds the avatar root used to find renderers and clears the tracking state.</summary>
        public void Initialize(Transform root)
        {
            _root = root;
            _isTracking = false;
        }

        /// <summary>
        /// Advances the gate for the frame. Returns true while tracking (the caller should
        /// then run its pose/expression update); returns false on an invalid frame after
        /// hiding the meshes. Mesh visibility flips only on the tracking transition.
        /// </summary>
        public bool Update(bool isValid)
        {
            if (!isValid)
            {
                if (_isTracking) SetVisible(false);
                _isTracking = false;
                return false;
            }

            if (!_isTracking) SetVisible(true);
            _isTracking = true;
            return true;
        }

        /// <summary>Enables/disables every renderer under the avatar root.</summary>
        public void SetVisible(bool visible)
        {
            if (_root == null) return;
            var renderers = _root.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = visible;
            }
        }
    }
}
