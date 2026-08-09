// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Runtime ILiveObject wrapper for one resolved <see cref="LiveClassAsset.InstanceBinding"/>:
    /// a scene object exposed without attributes. Created by a <c>LiveClassBinding</c> after it
    /// resolves the binding key through its <see cref="IExposedPropertyTable"/>; the id is the
    /// binding key GUID, so persisted values stay stable across scenes.
    /// Not serialized anywhere — the asset and the resolver's reference table are the
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
            LiveClassAssetSystem.Attach(this);
        }

        private void _Unregister()
        {
            LiveClassAssetSystem.Detach(this);
            _liveObject?.Unregister();
            _liveObject = null;
        }

        /// <summary>
        /// (Re)creates the registry handle against the given LiveClass. Called by
        /// <see cref="LiveClassAssetSystem"/> — handles capture the LiveClass, so a rebuilt type
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
