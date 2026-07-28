using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [Serializable]
    [LiveClass(Category = "Light", Icon = "lightbulb")]
    [FormerlyNamedAs("ExposedLight")]
    [MovedFrom(false, null, null, "ExposedLight")]
    public class LiveLight : LiveUnityObjectProxy<LiveLight, Light>
    {
        [LiveField, Hide]
        [FormerlyNamedAs("enabled")]
        private bool _enabled = true;

        [LiveProperty]
        public bool enabled
        {
            // Read live from the Light so external changes (Inspector / scripts / operations that
            // touch the Light directly) are reflected by RemoteApp. The shadow field _enabled is
            // kept only for serialization and as a fallback when _reference is null; it is refreshed
            // from the live state in OnBeforeLiveSerialize. Matches LiveGameObject.active.
            get => _reference != null ? _reference.enabled : _enabled;
            set
            {
                _enabled = value;
                if (_reference != null) _reference.enabled = value;
            }
        }

        [LiveField, Hide]
        [FormerlyNamedAs("color")]
        private Color _color = Color.white;

        [LiveProperty]
        public Color color
        {
            get => _reference != null ? _reference.color : _color;
            set
            {
                _color = value;
                if (_reference != null) _reference.color = value;
            }
        }

        [LiveField, Hide]
        [FormerlyNamedAs("intensity")]
        private float _intensity = 1f;

        [LiveProperty, Slider(0, 10, 0.1f)]
        public float intensity
        {
            get => _reference != null ? _reference.intensity : _intensity;
            set
            {
                _intensity = value;
                if (_reference != null) _reference.intensity = value;
            }
        }

        [LiveField, Hide]
        [FormerlyNamedAs("shadow")]
        private bool _shadow = true;

        [LiveProperty]
        public bool shadow
        {
            get => _reference != null ? _reference.shadows != LightShadows.None : _shadow;
            set
            {
                _shadow = value;
                if (_reference != null) _reference.shadows = value ? LightShadows.Soft : LightShadows.None;
            }
        }

        [LiveField, Hide]
        [FormerlyNamedAs("transform")]
        private TransformValue _transform = TransformValue.identity;

        [LiveProperty]
        public TransformValue transform
        {
            get => _reference != null ? TransformValue.FromTransform(_reference.transform) : _transform;
            set
            {
                _transform = value;
                if (_reference != null) value.ApplyTo(_reference.transform);
            }
        }

        [SerializeField, LiveField]
        TransformRef _parent = new TransformRef();

        public TransformRef parent => _parent;

        [NonSerialized]
        Transform _attachedTransform;

        public LiveLight() : base(null) { }

        public LiveLight(Light light) : base(light)
        {
        }


        public override void Update()
        {
        }


        public override void OnEnable()
        {
            GameObjectUtility.RegisterHierarchyChanged(_OnHierarchyChanged);
            TransformStructureService.onStructureChanged += _OnStructureChanged;

            base.OnEnable();

            _enabled = _reference != null ? _reference.enabled : true;
            _color = _reference != null ? _reference.color : Color.white;
            _intensity = _reference != null ? _reference.intensity : 1f;
            _shadow = _reference != null ? _reference.shadows != LightShadows.None : true;

            _parent.SetSelf(this);

            if (_reference != null
                && _parent.isEmpty
                && _reference.transform.parent != null)
            {
                _parent.InitFromTransform(_reference.transform.parent);
            }

            _parent.onChanged += _OnParentChanged;
            _UpdateAttachment();
            _ApplyLightSettings();
        }

        void _ApplyLightSettings()
        {
            if (_reference == null) return;
            _reference.enabled = _enabled;
            _reference.color = _color;
            _reference.intensity = _intensity;
            _reference.shadows = _shadow ? LightShadows.Soft : LightShadows.None;
            // _transform.ApplyTo は OnEnable 経由で呼ぶと、シーン配置時に Inspector で
            // 設定された rotation を identity で上書きしてしまう。
            // JSON ロード時のみ OnAfterLiveDeserialize で適用する。
        }

        public override void OnBeforeLiveSerialize()
        {
            base.OnBeforeLiveSerialize();
            // Getters now read live, so refresh every shadow field from the Light before saving.
            // Otherwise scene.json would persist the stale OnEnable snapshot (a latent bug).
            if (_reference != null)
            {
                _enabled = _reference.enabled;
                _color = _reference.color;
                _intensity = _reference.intensity;
                _shadow = _reference.shadows != LightShadows.None;
                _transform = TransformValue.FromTransform(_reference.transform);
            }
        }

        public override void OnAfterLiveDeserialize()
        {
            base.OnAfterLiveDeserialize();
            _ApplyLightSettings();
            if (_reference != null) _transform.ApplyTo(_reference.transform);
        }

        public override void OnDisable()
        {
            base.OnDisable();

            Lilium.RemoteControl.GameObjectUtility.UnregisterHierarchyChanged(_OnHierarchyChanged);
            _parent.onChanged -= _OnParentChanged;
            TransformStructureService.onStructureChanged -= _OnStructureChanged;
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

        /// <summary>
        /// owner GameObject の内部 hierarchy 変化通知。ownerName 一致時のみ再 attach する。
        /// </summary>
        void _OnStructureChanged(GameObject owner)
        {
            if (owner == null) return;
            if (_parent.ownerName != owner.name) return;
            _UpdateAttachment();
        }

        void _UpdateAttachment()
        {
            if (_reference == null) return;
            TransformAttachment.Attach(_parent, _reference.transform, ref _attachedTransform);
        }
    }
}