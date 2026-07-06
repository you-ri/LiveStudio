// Copyright (c) You-Ri, 2026

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Shared tracking-visibility gate for avatar components that toggle their meshes
    /// with the motion source (StandardAvatar / VRCAvatar / VRM0Avatar). Visibility uses
    /// asymmetric conditions:
    /// the avatar is hidden the moment BOTH body (MediaPipe) and face (ARKit) tracking are
    /// lost, and shown again on the rising edge of face (ARKit) tracking — the moment face
    /// tracking becomes valid. The body signal may flicker frame-to-frame (its validity
    /// beats against the receive phase), but the face signal is stable, so requiring both
    /// to drop before hiding keeps the visible state solid while tracked, and the face
    /// rising edge shows the avatar the instant tracking is (re)acquired. Renderers are
    /// looked up from the avatar root on each toggle.
    /// </summary>
    public struct AvatarVisibilityGate
    {
        Transform _root;
        bool _isVisible;
        bool _prevFaceValid;

        /// <summary>True while the avatar meshes are currently shown.</summary>
        public bool isTracking => _isVisible;

        /// <summary>Binds the avatar root used to find renderers and clears the gate state.</summary>
        public void Initialize(Transform root)
        {
            _root = root;
            _isVisible = false;
            _prevFaceValid = false;
        }

        /// <summary>
        /// Advances the gate for the frame. Hides the meshes the instant BOTH
        /// <paramref name="bodyValid"/> and <paramref name="faceValid"/> are lost, and shows
        /// them on the rising edge of <paramref name="faceValid"/>. Returns true while the
        /// avatar is visible (the caller should then run its pose/expression update). Because
        /// visibility is baselined to hidden in <see cref="Initialize"/>, a face signal that
        /// is already valid on the first frame still counts as a rising edge and shows it.
        /// </summary>
        public bool Update(bool bodyValid, bool faceValid)
        {
            if (_isVisible)
            {
                // Hide the moment both body (MediaPipe) and face (ARKit) tracking are lost.
                if (!bodyValid && !faceValid)
                {
                    SetVisible(false);
                    _isVisible = false;
                }
            }
            else
            {
                // Show the moment face (ARKit) tracking becomes valid.
                if (!_prevFaceValid && faceValid)
                {
                    SetVisible(true);
                    _isVisible = true;
                }
            }

            _prevFaceValid = faceValid;
            return _isVisible;
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
