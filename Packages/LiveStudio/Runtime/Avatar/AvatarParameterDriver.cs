// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// How a driven parameter is modified on state enter. Values match VRChat's
    /// VRC_AvatarParameterDriver.ChangeType so converted data maps one-to-one.
    /// </summary>
    public enum AvatarParameterChangeType
    {
        Set = 0,
        Add = 1,
        Random = 2,
        Copy = 3,
    }

    /// <summary>
    /// A single parameter drive operation. Field shape mirrors VRChat's
    /// VRC_AvatarParameterDriver.Parameter (functional schema only; no SDK code).
    /// </summary>
    [Serializable]
    public class AvatarParameterDriverParameter
    {
        /// <summary>Destination animator parameter name.</summary>
        public string name;

        /// <summary>Source animator parameter name (used by <see cref="AvatarParameterChangeType.Copy"/>).</summary>
        public string source;

        public AvatarParameterChangeType type = AvatarParameterChangeType.Set;

        /// <summary>Value used by Set / Add.</summary>
        public float value;

        /// <summary>Lower bound for Random (Float / Int).</summary>
        public float valueMin;

        /// <summary>Upper bound for Random (Float / Int).</summary>
        public float valueMax;

        /// <summary>Probability [0,1] of becoming true / firing for Bool / Trigger Random.</summary>
        public float chance = 1f;

        /// <summary>Source range min for Copy when <see cref="convertRange"/> is true.</summary>
        public float sourceMin;

        /// <summary>Source range max for Copy when <see cref="convertRange"/> is true.</summary>
        public float sourceMax;

        /// <summary>Destination range min for Copy when <see cref="convertRange"/> is true.</summary>
        public float destMin;

        /// <summary>Destination range max for Copy when <see cref="convertRange"/> is true.</summary>
        public float destMax;

        /// <summary>When true, Copy remaps the source value from [sourceMin,sourceMax] to [destMin,destMax].</summary>
        public bool convertRange;
    }

    /// <summary>
    /// Non-VRChat replacement for VRChat's VRCAvatarParameterDriver. Attached to
    /// AnimatorController states; drives animator parameters on state enter
    /// (Set / Add / Random / Copy), matching VRChat's apply timing.
    /// Clean-room implementation: references only the public, functional API shape
    /// and contains no VRChat SDK code.
    /// </summary>
    public class AvatarParameterDriver : StateMachineBehaviour
    {
        public List<AvatarParameterDriverParameter> parameters = new List<AvatarParameterDriverParameter>();

        /// <summary>
        /// Kept for VRChat data fidelity. This package has no networking concept,
        /// so the driver always applies regardless of this flag.
        /// </summary>
        public bool localOnly;

        public string debugString;

        // Cached parameter-name -> type map. Built lazily per Animator to avoid
        // per-state-enter allocations. Animators rarely change parameter sets at
        // runtime, so a single cache keyed by the last seen Animator is enough.
        private Animator _cachedAnimator;
        private Dictionary<string, AnimatorControllerParameterType> _typeByName;

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            Apply(animator);
        }

        /// <summary>
        /// Applies every configured parameter operation against <paramref name="animator"/>.
        /// Public so behaviour can be unit tested without an AnimatorController playback.
        /// </summary>
        public void Apply(Animator animator)
        {
            if (animator == null || parameters == null) return;

            EnsureTypeCache(animator);

            for (int i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];
                if (p == null || string.IsNullOrEmpty(p.name)) continue;

                if (!_typeByName.TryGetValue(p.name, out var destType))
                {
                    Debug.LogWarning($"[Studio] AvatarParameterDriver: parameter '{p.name}' not found on Animator; skipped.");
                    continue;
                }

                switch (p.type)
                {
                    case AvatarParameterChangeType.Set:
                        _Write(animator, p.name, destType, p.value);
                        break;
                    case AvatarParameterChangeType.Add:
                        _Write(animator, p.name, destType, _Read(animator, p.name, destType) + p.value);
                        break;
                    case AvatarParameterChangeType.Random:
                        _ApplyRandom(animator, p, destType);
                        break;
                    case AvatarParameterChangeType.Copy:
                        _ApplyCopy(animator, p, destType);
                        break;
                }
            }
        }

        private void _ApplyRandom(Animator animator, AvatarParameterDriverParameter p, AnimatorControllerParameterType destType)
        {
            if (destType == AnimatorControllerParameterType.Bool ||
                destType == AnimatorControllerParameterType.Trigger)
            {
                bool on = UnityEngine.Random.value < p.chance;
                _Write(animator, p.name, destType, on ? 1f : 0f);
            }
            else
            {
                _Write(animator, p.name, destType, UnityEngine.Random.Range(p.valueMin, p.valueMax));
            }
        }

        private void _ApplyCopy(Animator animator, AvatarParameterDriverParameter p, AnimatorControllerParameterType destType)
        {
            if (string.IsNullOrEmpty(p.source) ||
                !_typeByName.TryGetValue(p.source, out var srcType))
            {
                Debug.LogWarning($"[Studio] AvatarParameterDriver: copy source '{p.source}' not found on Animator; skipped.");
                return;
            }

            float v = _Read(animator, p.source, srcType);
            if (p.convertRange)
            {
                float denom = p.sourceMax - p.sourceMin;
                float t = Mathf.Approximately(denom, 0f) ? 0f : (v - p.sourceMin) / denom;
                v = Mathf.LerpUnclamped(p.destMin, p.destMax, t);
            }
            _Write(animator, p.name, destType, v);
        }

        private static float _Read(Animator animator, string name, AnimatorControllerParameterType type)
        {
            switch (type)
            {
                case AnimatorControllerParameterType.Float:
                    return animator.GetFloat(name);
                case AnimatorControllerParameterType.Int:
                    return animator.GetInteger(name);
                case AnimatorControllerParameterType.Bool:
                case AnimatorControllerParameterType.Trigger:
                    return animator.GetBool(name) ? 1f : 0f;
                default:
                    return 0f;
            }
        }

        private static void _Write(Animator animator, string name, AnimatorControllerParameterType type, float value)
        {
            switch (type)
            {
                case AnimatorControllerParameterType.Float:
                    animator.SetFloat(name, value);
                    break;
                case AnimatorControllerParameterType.Int:
                    animator.SetInteger(name, Mathf.RoundToInt(value));
                    break;
                case AnimatorControllerParameterType.Bool:
                    animator.SetBool(name, value != 0f);
                    break;
                case AnimatorControllerParameterType.Trigger:
                    if (value != 0f) animator.SetTrigger(name);
                    else animator.ResetTrigger(name);
                    break;
            }
        }

        private void _BuildTypeCache(Animator animator)
        {
            if (_typeByName == null) _typeByName = new Dictionary<string, AnimatorControllerParameterType>();
            else _typeByName.Clear();

            var ps = animator.parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                _typeByName[ps[i].name] = ps[i].type;
            }
            _cachedAnimator = animator;
        }

        private void EnsureTypeCache(Animator animator)
        {
            if (_cachedAnimator != animator || _typeByName == null)
            {
                _BuildTypeCache(animator);
            }
        }
    }
}
