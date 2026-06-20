// Copyright (c) You-Ri, 2026

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A named position marker placed in the scene. Acts as a tag identifying a "stage mark"
    /// (a blocking mark / spike mark) that the avatar can be warped to from the remote app.
    ///
    /// Marks register themselves with <see cref="StageMarkRegistry"/> on enable and unregister
    /// on disable, so marks brought in or removed by additive set bundles (<c>*.set.lsb</c>)
    /// are tracked automatically without hooking SceneManager events.
    /// </summary>
    [DisallowMultipleComponent]
    public class StageMark : MonoBehaviour
    {
        /// <summary>Display name for this mark, shown in the remote app. Uses the GameObject name.</summary>
        public string label => gameObject.name;

        /// <summary>World position the avatar is moved to when this mark is selected.</summary>
        public Vector3 position => transform.position;

        /// <summary>World rotation the avatar is oriented to when this mark is selected.</summary>
        public Quaternion rotation => transform.rotation;

        private void OnEnable()
        {
            StageMarkRegistry.Register(this);
        }

        private void OnDisable()
        {
            StageMarkRegistry.Unregister(this);
        }
    }
}
