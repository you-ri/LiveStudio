// Copyright (c) You-Ri, 2026

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.RemoteControl.LiveScene
{
    /// <summary>
    /// Small shared helpers for the live-scene / project-settings serializers (same assembly), so the
    /// two serializers do not each carry a private copy.
    /// </summary>
    internal static class LiveSceneObjectUtil
    {
        /// <summary>
        /// Resolves the backing <see cref="GameObject"/> for an exposed handle: the GameObject itself, a
        /// component's GameObject, or the GameObject/Component referenced by an
        /// <see cref="LiveUnityObjectBase"/> proxy. Returns null for non-GameObject targets.
        /// </summary>
        public static GameObject GetGameObject(LiveObjectHandle obj)
        {
            if (obj.target is Component comp) return comp.gameObject;
            if (obj.target is GameObject g) return g;
            if (obj.target is LiveUnityObjectBase unityObj)
            {
                if (unityObj.reference is GameObject gRef) return gRef;
                if (unityObj.reference is Component cRef) return cRef.gameObject;
            }
            return null;
        }
    }
}
