using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [Serializable]
    [ExposedClass(Category = "Light", Icon = "lightbulb")]
    public class ExposedLight : ExposedUnityObjectProxy<ExposedLight, Light>
    {
        [ExposedField, Hide]
        [FormerlyExposedAs("enabled")]
        private bool _enabled = true;

        [ExposedProperty]
        public bool enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (_reference != null) _reference.enabled = value;
            }
        }

        [ExposedField, Hide]
        [FormerlyExposedAs("color")]
        private Color _color = Color.white;

        [ExposedProperty]
        public Color color
        {
            get => _color;
            set
            {
                _color = value;
                if (_reference != null) _reference.color = value;
            }
        }

        [ExposedField, Hide]
        [FormerlyExposedAs("intensity")]
        private float _intensity = 1f;

        [ExposedProperty, Slider(0, 10, 0.1f)]
        public float intensity
        {
            get => _intensity;
            set
            {
                _intensity = value;
                if (_reference != null) _reference.intensity = value;
            }
        }

        [ExposedField, Hide]
        [FormerlyExposedAs("shadow")]
        private bool _shadow = true;

        [ExposedProperty]
        public bool shadow
        {
            get => _shadow;
            set
            {
                _shadow = value;
                if (_reference != null) _reference.shadows = value ? LightShadows.Hard : LightShadows.None;
            }
        }

        [ExposedField, Hide]
        [FormerlyExposedAs("transform")]
        private TransformValue _transform = TransformValue.identity;

        [ExposedProperty]
        public TransformValue transform
        {
            get => _transform;
            set
            {
                _transform = value;
                if (_reference != null) value.ApplyTo(_reference.transform);
            }
        }

        [SerializeField, ExposedField]
        TransformRef _parent = new TransformRef();

        public TransformRef parent => _parent;

        [NonSerialized]
        Transform _attachedTransform;

        public ExposedLight() : base(null) { }

        public ExposedLight(Light light) : base(light)
        {
        }


        public override void Update()
        {
        }


        public override void OnEnable()
        {
            base.OnEnable();

            _parent.SetSelf(this);

            if (_reference != null
                && _parent.isEmpty
                && _reference.transform.parent != null)
            {
                _parent.InitFromTransform(_reference.transform.parent);
            }

            _parent.onChanged += _OnParentChanged;
            Lilium.RemoteControl.GameObjectUtility.RegisterHierarchyChanged(_OnHierarchyChanged);
            SelectableService<IAvatarService>.onRegistered += _OnAvatarRegistered;
            SelectableService<IAvatarService>.onUnregistered += _OnAvatarUnregistered;
            _UpdateAttachment();
            _ApplyLightSettings();
        }

        void _ApplyLightSettings()
        {
            if (_reference == null) return;
            _reference.enabled = _enabled;
            _reference.color = _color;
            _reference.intensity = _intensity;
            _reference.shadows = _shadow ? LightShadows.Hard : LightShadows.None;
            // _transform.ApplyTo は OnEnable 経由で呼ぶと、シーン配置時に Inspector で
            // 設定された rotation を identity で上書きしてしまう。
            // JSON ロード時のみ OnAfterExposedDeserialize で適用する。
        }

        public override void OnBeforeExposedSerialize()
        {
            base.OnBeforeExposedSerialize();
            if (_reference != null) _transform = TransformValue.FromTransform(_reference.transform);
        }

        public override void OnAfterExposedDeserialize()
        {
            base.OnAfterExposedDeserialize();
            _ApplyLightSettings();
            if (_reference != null) _transform.ApplyTo(_reference.transform);
        }

        public override void OnDisable()
        {
            base.OnDisable();

            Lilium.RemoteControl.GameObjectUtility.UnregisterHierarchyChanged(_OnHierarchyChanged);
            _parent.onChanged -= _OnParentChanged;
            SelectableService<IAvatarService>.onRegistered -= _OnAvatarRegistered;
            SelectableService<IAvatarService>.onUnregistered -= _OnAvatarUnregistered;
        }

        void _OnParentChanged() => _UpdateAttachment();

        /// <summary>
        /// Unity hierarchy の変更通知を受け、実際の Transform.parent と TransformRef の保持する
        /// desired state にズレがある場合は TransformRef を silent に同期する。
        /// </summary>
        void _OnHierarchyChanged()
        {
            if (_reference == null) return;
            var actualParent = _reference.transform.parent;
            if (actualParent == _attachedTransform) return;
            _parent.InitFromTransform(actualParent, silent: true);
            // 次回の Attach で同じ親への余分な SetParent を避けるためキャッシュも同期する。
            _attachedTransform = actualParent;
        }

        void _OnAvatarRegistered(string id, IAvatarService avatar)
        {
            avatar.onAvatarChanged += _OnAvatarChanged;
            _UpdateAttachment();
        }

        void _OnAvatarUnregistered(string id, IAvatarService avatar)
        {
            avatar.onAvatarChanged -= _OnAvatarChanged;
            TransformAttachment.Detach(_reference != null ? _reference.transform : null, ref _attachedTransform);
        }

        void _OnAvatarChanged() => _UpdateAttachment();

        void _UpdateAttachment()
        {
            if (_reference == null) return;
            TransformAttachment.Attach(_parent, _reference.transform, ref _attachedTransform);
        }
    }
}