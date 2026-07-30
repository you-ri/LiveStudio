// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// One exposed member (property/field or method) of a <see cref="LiveBindingPreset"/> type
    /// definition. Carries the UI metadata that attribute-based exposure would normally provide
    /// via [Control]/[Help]/[LiveProperty], so arbitrary compiled types can be exposed without
    /// any code changes.
    /// </summary>
    [Serializable]
    public class LiveBindingMember
    {
        [Tooltip("Member (property/field/method) name on the target type")]
        public string path;

        [Tooltip("True when the member is a method exposed as a RemoteApp button")]
        public bool isFunction;

        [Tooltip("Optional display label shown in RemoteApp instead of the member name")]
        public string label;

        [Tooltip("Optional help text shown in RemoteApp")]
        public string help;

        [Tooltip("Include this member in live-scene save/restore")]
        public bool persistable = true;

        [Tooltip("Render a numeric member as a slider")]
        public bool useSlider;

        public float sliderMin = 0f;

        public float sliderMax = 1f;

        [Tooltip("Slider step. 0 auto-derives (range / 20)")]
        public float sliderStep = 0f;

        internal LivePropertyDefine ToPropertyDefine()
        {
            return new LivePropertyDefine
            {
                name = path,
                path = path,
                isPersistable = persistable,
                persistScope = PersistScope.Scene,
                control = useSlider ? new SliderAttribute(sliderMin, sliderMax, sliderStep) : null,
                label = string.IsNullOrEmpty(label) ? null : label,
                help = string.IsNullOrEmpty(help) ? null : help,
            };
        }

        internal LiveFunctionDefine ToFunctionDefine()
        {
            return new LiveFunctionDefine
            {
                name = path,
                path = path,
                label = string.IsNullOrEmpty(label) ? null : label,
                help = string.IsNullOrEmpty(help) ? null : help,
            };
        }

        /// <summary>
        /// Contribution to the per-type registration signature. Any change in the returned string
        /// forces the type's LiveClass to be rebuilt (see <see cref="LiveBindingSystem"/>).
        /// </summary>
        internal void AppendSignature(System.Text.StringBuilder sb)
        {
            sb.Append(isFunction ? 'f' : 'p').Append(':').Append(path)
                .Append('|').Append(label).Append('|').Append(help)
                .Append('|').Append(persistable ? '1' : '0');
            if (useSlider)
            {
                sb.Append("|s").Append(sliderMin).Append(',').Append(sliderMax).Append(',').Append(sliderStep);
            }
            sb.Append(';');
        }
    }

    /// <summary>
    /// Runtime ILiveObject wrapper for one resolved <see cref="LiveBindingPreset.InstanceBinding"/>:
    /// a scene object exposed without attributes. Created by a <c>LiveBindingResolver</c> after it
    /// resolves the binding key through its <see cref="IExposedPropertyTable"/>; the id is the
    /// binding key GUID, so persisted values stay stable across scenes.
    /// Not serialized anywhere — the preset asset and the resolver's reference table are the
    /// persistent state.
    /// </summary>
    public class LiveBinding : LiveUnityObjectBase
    {
        private readonly string _id;

        private readonly UnityEngine.Object _reference;

        private string _fallbackName;

        public override string name
        {
            get
            {
                if (_reference is Component component) return component.gameObject.name;
                return _reference != null ? _reference.name : _fallbackName;
            }
            set
            {
                if (_reference is Component component)
                {
                    component.gameObject.name = value;
                }
                else if (_reference != null)
                {
                    _reference.name = value;
                }
                _fallbackName = value;
            }
        }

        public override string id => _id;

        public override Type referenceType => typeof(UnityEngine.Object);

        public override UnityEngine.Object reference => _reference;

        /// <summary>The object whose members are exposed.</summary>
        public UnityEngine.Object target => _reference;

        public LiveBinding(string id, UnityEngine.Object reference)
        {
            _id = id;
            _reference = reference;
            if (reference != null)
            {
                _Register();
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            _Register();
        }

        public override void OnDisable()
        {
            base.OnDisable();
            _Unregister();
        }

        public override bool ResolveReferences(IExposedPropertyTable resolver)
        {
            _Register();
            return _reference != null;
        }

        private void _Register()
        {
            if (_reference == null) return;
            LiveBindingSystem.Attach(this);
        }

        private void _Unregister()
        {
            LiveBindingSystem.Detach(this);
            _liveObject?.Unregister();
            _liveObject = null;
        }

        /// <summary>
        /// (Re)creates the registry handle against the given LiveClass. Called by
        /// <see cref="LiveBindingSystem"/> — handles capture the LiveClass, so a rebuilt type
        /// definition invalidates them and they must be recreated.
        /// </summary>
        internal void RefreshHandle(LiveClass liveClass)
        {
            if (_reference == null || liveClass == null)
            {
                _liveObject?.Unregister();
                _liveObject = null;
                return;
            }
            if (_liveObject.HasValue && ReferenceEquals(_liveObject.Value.targetType, liveClass)) return;

            _liveObject?.Unregister();
            _liveObject = LiveObjectRegistry.GetOrCreate(_id, liveClass, _reference);
        }
    }
}
