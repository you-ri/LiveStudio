// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Drives this GameObject's transform to follow a bone resolved from a <see cref="TransformRef"/>
    /// (a target owner name + a bone name or hierarchy path), so the object stays pinned to the bone even
    /// after the avatar model is swapped.
    ///
    /// Intended as a stable LookAt / Follow anchor for a Cinemachine camera authored in a *separate* scene
    /// (e.g. a stage / set bundle that is outside the LiveStudio camera pipeline, so it cannot receive a
    /// <see cref="LookAtCameraController"/>). The camera's LookAt is assigned to this object as an ordinary
    /// intra-scene reference, while this object bridges across the swappable avatar: a direct reference from
    /// the camera to the avatar bone cannot survive the swap (the bone Transform is destroyed) and cannot be
    /// authored across scenes, whereas this component re-resolves the bone on avatar reload and copies its
    /// pose every frame.
    ///
    /// The resolved bone is cached and only re-resolved when the reference changes
    /// (<see cref="TransformRef.onChanged"/>) or when the owning hierarchy is rebuilt
    /// (<see cref="TransformStructureService.onStructureChanged"/>, which fires on avatar swap), so the
    /// per-frame follow is allocation-free. This mirrors <see cref="LookAtCameraController"/>'s resolution
    /// strategy; the difference is that this component moves itself instead of assigning a camera target.
    /// </summary>
    [DefaultExecutionOrder(20)] // after the avatar (VRCFTAvatar order 10) has posed its bones this frame
    [ExposedClass("BoneFollower", Category = "Camera", Icon = "my_location")]
    public class BoneFollower : MonoBehaviour
    {
        [ExposedProperty("name"), Hide]
        public string displayName => this.name;

        // Bone reference (owner name + bone name / path). Defaults to the main avatar's head, matching
        // LookAtCameraController, since a head anchor is the common look-at case.
        [SerializeField, ExposedField]
        TransformRef _target = new TransformRef("Main Avatar", "S_Head", TransformRef.SearchType.Name);

        public TransformRef target => _target;

        // Copy the bone rotation as well as its position. Off by default: a look-at target only needs
        // position, but a follow anchor that frames by orientation can opt in.
        [SerializeField, ExposedField]
        bool _followRotation = false;

        // Cached resolved bone. The follow runs every frame, so the allocating Resolve() must not run per
        // frame; re-resolved only when the reference changes or the cached bone is destroyed (avatar swap).
        [NonSerialized] Transform _resolved;

        // Set when the reference or hierarchy changed, forcing a re-resolve on the next follow.
        [NonSerialized] bool _dirty = true;

        void OnEnable()
        {
            _target.onChanged += _OnTargetChanged;
            TransformStructureService.onStructureChanged += _OnStructureChanged;
            _dirty = true;
        }

        void OnDisable()
        {
            _target.onChanged -= _OnTargetChanged;
            TransformStructureService.onStructureChanged -= _OnStructureChanged;
        }

        void _OnTargetChanged() => _dirty = true;

        /// <summary>
        /// Re-resolve only when the changed owner is the one this reference points at (e.g. the avatar model
        /// was swapped). Mirrors <see cref="LookAtCameraController._OnStructureChanged"/>.
        /// </summary>
        void _OnStructureChanged(GameObject owner)
        {
            if (owner == null) return;
            if (_target.ownerName != owner.name) return;
            _dirty = true;
        }

        // LateUpdate so the bone has already been posed by animation this frame, and before the
        // CinemachineBrain samples its targets.
        void LateUpdate()
        {
            var bone = _EnsureBone();
            if (bone == null) return;

            if (_followRotation)
            {
                transform.SetPositionAndRotation(bone.position, bone.rotation);
            }
            else
            {
                transform.position = bone.position;
            }
        }

        // Resolves and caches the target bone. Allocates only when the reference changed or the cached bone
        // was destroyed (avatar swap); returns null (and keeps retrying) until the owner is registered.
        Transform _EnsureBone()
        {
            // Unity-null when the previous avatar's bone was destroyed (swap) → re-resolve.
            if (_dirty || _resolved == null)
            {
                _resolved = _target.Resolve();
                // Keep retrying while unresolved; clear the dirty flag only once a bone is actually found.
                _dirty = _resolved == null;
            }
            return _resolved;
        }
    }
}
