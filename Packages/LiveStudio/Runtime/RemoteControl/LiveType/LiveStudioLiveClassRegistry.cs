// Copyright (c) You-Ri, 2026

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Applies the live class asset shipped with this package. It declares the built-in Unity
    /// components LiveStudio exposes without giving them a proxy class of their own (Light, ...),
    /// so a <see cref="LiveGameObject"/> holding one lists it among its components and its values
    /// are saved with the live scene.
    ///
    /// Registration is global rather than per <c>RemoteControlContainer</c>: these declarations
    /// ship with the package and apply to every scene, and an unregistered type is skipped by the
    /// serializer — wiring them per scene would silently drop those values in any scene that
    /// forgot the reference. Assets that travel with a set bundle keep using the container list.
    ///
    /// The shipped asset is read-only for users of the package; types of their own go in their
    /// own live class asset, which a container applies as before.
    /// </summary>
    public static class LiveStudioLiveClassRegistry
    {
        /// <summary><c>Resources.Load</c> path (no extension) of the asset shipped here.</summary>
        public const string kResourcesName = "LiveStudioLiveClasses";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        static void _Init()
        {
            // LiveClassAssetSystem clears its bookkeeping at SubsystemRegistration (which runs
            // before this), so the declarations have to be applied again every play session.
            var asset = Resources.Load<LiveClassAsset>(kResourcesName);
            if (asset == null)
            {
                Debug.LogError($"[Studio] Live class asset '{kResourcesName}' is missing from Resources.");
                return;
            }

            // Permanent: these ship with the package and apply to every scene, so no container
            // that happens to also list the asset may unregister them when it disables.
            LiveClassAssetSystem.RegisterTypesPermanent(asset);
        }
    }
}
