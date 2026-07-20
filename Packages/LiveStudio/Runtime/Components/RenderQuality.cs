// Copyright (c) You-Ri, 2026

using System;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Exposes Unity's render quality level (<see cref="QualitySettings"/>) to the remote app as the
    /// settings page's quality section.
    ///
    /// Whether the section appears is decided by registration, not by code: the settings
    /// <c>NavigatePage</c> lists this object's id, and <c>NavigateObjectSelector</c> silently skips ids
    /// that resolve to nothing. So an app that wants remote quality control puts an instance in a
    /// <see cref="Lilium.RemoteControl.LiveScene.RemoteControlContainer"/> (or the host
    /// <c>RemoteControlBehaviour</c>) object list, and one that does not simply leaves it out.
    ///
    /// The level is a project-wide setting rather than a per-scene one, so it persists into
    /// <c>{projectPath}/Settings/RenderQuality.settings.json</c> via <see cref="PersistScope.Project"/>.
    /// </summary>
    [Serializable]
    [ExposedClass(Icon = "high_quality", HideInScene = true)]
    public class RenderQuality : IExposedObject, IExposedDeserializeCallback
    {
        // Readable and stable so the settings NavigatePage can reference this object by name, the way
        // it referenced the static LiveSceneManager class this used to live on.
        const string kId = "RenderQuality";

        public string name { get; set; } = "Render Quality";

        public ExposedObjectHandle? exposedObject => ExposedObjectRegistry.FindByTarget(this);

        public string id => kId;

        /// <summary>The quality level names of the current project, in <c>QualitySettings</c> order.</summary>
        [ExposedProperty, Hide]
        public string[] qualityNames => QualitySettings.names;

        [ExposedField(persistScope = PersistScope.Project), Hide]
        private string _quality;

        [Section("high_quality", "SECTION_QUALITY_TITLE", "SECTION_QUALITY_SUBTITLE")]
        [ExposedProperty]
        [StringSelector(nameof(qualityNames))]
        public string quality
        {
            get => _quality;
            set
            {
                _quality = value;
                SetQuality(value);
            }
        }

        public void OnEnable()
        {
            // Sync the shadow field with QualitySettings before the container captures defaults, so the
            // initial getter value reflects the active quality level and the persisted baseline matches
            // reality for dirty detection.
            _quality = QualitySettings.names[QualitySettings.GetQualityLevel()];

            ExposedObjectRegistry.Create<RenderQuality>(this, kId);
        }

        public void OnDisable()
        {
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

        /// <summary>
        /// Re-applies <see cref="_quality"/> to <see cref="QualitySettings"/>. The deserializer writes the
        /// shadow field by raw reflection and bypasses the property setter, so the setter's side effect has
        /// to be redone here. Idempotent, which matters because this also fires on remote writes.
        /// </summary>
        public void OnAfterExposedDeserialize()
        {
            if (!string.IsNullOrEmpty(_quality)) SetQuality(_quality);
        }

        /// <summary>Activates the quality level with the given name. Logs an error when it is unknown.</summary>
        public void SetQuality(string levelName)
        {
            var names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == levelName)
                {
                    QualitySettings.SetQualityLevel(i, true);
                    return;
                }
            }

            Debug.LogError($"[LiveStudio] Quality level '{levelName}' not found. Available: {string.Join(", ", names)}");
        }
    }
}
