// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// Finds UnityEngine.Object references that live only in memory (no asset path) under a
    /// GameObject hierarchy and persists them as sub-assets of a single container asset.
    /// <para>
    /// After Modular Avatar / NDMF manual bake, generated Mesh / Material / AnimationClip /
    /// AnimatorController objects are sometimes left in memory. Saving such a hierarchy as a
    /// prefab loses those references because Unity does not serialize non-persistent objects.
    /// </para>
    /// <para>
    /// <see cref="AssetDatabase.AddObjectToAsset(Object, Object)"/> persists the existing
    /// instance in place, so references from components keep pointing at the same object and
    /// no remapping is required.
    /// </para>
    /// </summary>
    internal static class InMemoryAssetCollector
    {
        /// <summary>
        /// Persists every in-memory asset reachable from <paramref name="root"/> into
        /// <paramref name="container"/> as a sub-asset. Returns the number of objects persisted.
        /// </summary>
        public static int CollectAndPersist(GameObject root, Object container)
        {
            if (root == null || container == null) return 0;

            var visited = new HashSet<long>();
            var queue = new Queue<Object>();

            // 1. シーン階層の全コンポーネントから whitelist 型の在メモリ参照を収集する。
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue; // Missing Script
                _ScanReferences(component, r =>
                {
                    if (_ShouldCollectFromComponent(r) && visited.Add(_GetId(r)))
                    {
                        queue.Enqueue(r);
                    }
                });
            }

            // 2. 収集したオブジェクトを永続化し、その内部参照（Material→Texture、
            //    AnimatorController→State/BlendTree 等）も同様にたどって永続化する。
            int persisted = 0;
            while (queue.Count > 0)
            {
                var obj = queue.Dequeue();
                if (obj == null || EditorUtility.IsPersistent(obj)) continue;

                // DontSave 系が立っているとサブアセットとして保存されない。
                obj.hideFlags &= ~(HideFlags.DontSave | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild);
                AssetDatabase.AddObjectToAsset(obj, container);
                persisted++;

                if (_ShouldRecurseInto(obj))
                {
                    _ScanReferences(obj, r =>
                    {
                        if (_ShouldCollectNested(r) && visited.Add(_GetId(r)))
                        {
                            queue.Enqueue(r);
                        }
                    });
                }
            }

            return persisted;
        }

        private static void _ScanReferences(Object owner, System.Action<Object> onReference)
        {
            using var so = new SerializedObject(owner);
            var it = so.GetIterator();
            bool enterChildren = true;
            while (it.Next(enterChildren))
            {
                enterChildren = true;
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                var r = it.objectReferenceValue;
                if (r != null) onReference(r);
            }
        }

        // コンポーネントから直接拾うのは、ベイクで生成され得る代表的なアセット型に限定する。
        private static bool _ShouldCollectFromComponent(Object o)
        {
            if (!_IsCollectableInstance(o)) return false;
            return o is Mesh
                || o is Material
                || o is Texture
                || o is AnimationClip
                || o is RuntimeAnimatorController
                || o is AvatarMask
                || o is Avatar;
        }

        // 永続化対象の内部参照は型を限定しない。AnimatorController の State / Transition /
        // BlendTree 等は whitelist 外だが、親アセットと一緒に保存しないと参照が失われる。
        private static bool _ShouldCollectNested(Object o)
        {
            if (!_IsCollectableInstance(o)) return false;
            return !(o is MonoScript) && !(o is Shader);
        }

        private static bool _IsCollectableInstance(Object o)
        {
            if (o == null) return false;
            if (EditorUtility.IsPersistent(o)) return false;
            // GameObject / Component はシーン階層の参照なのでアセット化しない。
            return !(o is GameObject) && !(o is Component);
        }

        // 末端アセット（外部への参照を持たない型）は再走査コストを避けるため潜らない。
        private static bool _ShouldRecurseInto(Object o)
        {
            return !(o is Mesh)
                && !(o is Texture)
                && !(o is AnimationClip)
                && !(o is AudioClip)
                && !(o is Avatar)
                && !(o is AvatarMask);
        }

        // Unity 6.3 (6000.3) replaced GetInstanceID with GetEntityId, and Unity 6.5 (6000.5)
        // promoted both the old API and the EntityId<->int implicit conversions to Obsolete errors.
        // Only identity within this call matters here, so any stable 64-bit form will do.
        private static long _GetId(Object o)
        {
#if UNITY_6000_5_OR_NEWER
            return unchecked((long)EntityId.ToULong(o.GetEntityId()));
#elif UNITY_6000_3_OR_NEWER
            return o.GetEntityId();
#else
            return o.GetInstanceID();
#endif
        }
    }
}
