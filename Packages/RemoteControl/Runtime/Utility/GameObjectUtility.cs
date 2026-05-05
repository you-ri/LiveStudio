// Copyright (c) You-Ri, 2026
// Merged GameObjectUtility (consolidated from Lilium.Virgo.GameObjectUtility into Lilium.RemoteControl).

using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Utility for GameObject and Component operations that need to switch behavior between
    /// edit mode and play mode (PrefabUtility vs Object.Instantiate, DestroyImmediate vs Destroy, etc.).
    /// </summary>
    public static class GameObjectUtility
    {
        /// <summary>
        /// Set GameObject active state only when it differs from the current state, avoiding redundant change events.
        /// </summary>
        public static void SetActive(GameObject go, bool active)
        {
            if (go == null) return;
            if (go.activeSelf != active)
            {
                go.SetActive(active);
            }
        }

        /// <summary>
        /// Return the existing direct child with the given name under <paramref name="parent"/>,
        /// or create a new empty GameObject if none exists.
        /// </summary>
        public static GameObject CreateOrGetGameObject(string name, Transform parent)
        {
            if (parent == null) return null;
            var t = parent.Find(name);
            if (t == null)
            {
                var go = new GameObject(name);
                go.transform.parent = parent;
                return go;
            }
            return t.gameObject;
        }

        /// <summary>
        /// Same as <see cref="CreateOrGetGameObject(string, Transform)"/> but attaches the given component types when creating.
        /// </summary>
        public static GameObject CreateOrGetGameObject(string name, Transform parent, params System.Type[] components)
        {
            if (parent == null) return null;
            var t = parent.Find(name);
            if (t == null)
            {
                var go = new GameObject(name, components);
                go.transform.parent = parent;
                return go;
            }
            return t.gameObject;
        }

        /// <summary>
        /// Find a direct child of <paramref name="parent"/> with the given name. Returns null if not found.
        /// </summary>
        public static GameObject FindChildByName(GameObject parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
                return null;
            return FindChildByName(parent.transform, name);
        }

        /// <summary>
        /// Find a direct child of <paramref name="parent"/> with the given name. Returns null if not found.
        /// </summary>
        public static GameObject FindChildByName(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
                return null;
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child.gameObject;
            }
            return null;
        }

        /// <summary>
        /// Find a descendant of <paramref name="parent"/> with the given name (depth-first, breadth-preferred).
        /// </summary>
        public static GameObject FindChildByNameRecursive(GameObject parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
                return null;
            return FindChildByNameRecursive(parent.transform, name);
        }

        /// <summary>
        /// Find a descendant of <paramref name="parent"/> with the given name (depth-first, breadth-preferred).
        /// </summary>
        public static GameObject FindChildByNameRecursive(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
                return null;

            // Search direct children first to keep the result stable when multiple matches exist.
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child.gameObject;
            }

            foreach (Transform child in parent)
            {
                var result = FindChildByNameRecursive(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Find all direct children of <paramref name="parent"/> with the given name.
        /// </summary>
        public static GameObject[] FindChildrenByName(GameObject parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
                return new GameObject[0];
            return FindChildrenByName(parent.transform, name);
        }

        /// <summary>
        /// Find all direct children of <paramref name="parent"/> with the given name.
        /// </summary>
        public static GameObject[] FindChildrenByName(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
                return new GameObject[0];

            var results = new List<GameObject>();
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    results.Add(child.gameObject);
            }
            return results.ToArray();
        }

        /// <summary>
        /// Build a slash-separated relative path from <paramref name="root"/> to <paramref name="target"/>.
        /// Returns the target's name when target is not a descendant of root.
        /// </summary>
        public static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || root == target)
                return string.Empty;

            var path = target.name;
            var current = target.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        /// <summary>
        /// Instantiate a prefab keeping the prefab connection in editor non-play mode, registering an undo step.
        /// In play mode or standalone runtime this falls back to plain <see cref="Object.Instantiate(Object, Transform)"/>.
        /// </summary>
        public static GameObject CreateInstanceFromPrefab(GameObject prefab, Transform parent = null)
        {
            if (prefab == null) return null;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance != null)
                {
                    if (parent != null)
                        instance.transform.SetParent(parent);
                    Undo.RegisterCreatedObjectUndo(instance, "Create Instance From Prefab");
                }
                return instance;
            }
#endif
            return Object.Instantiate(prefab, parent);
        }

        /// <summary>
        /// Return an existing direct child named <paramref name="name"/> if present; otherwise instantiate
        /// <paramref name="prefab"/> via <see cref="CreateInstanceFromPrefab"/> and rename it.
        /// </summary>
        public static GameObject GetOrCreateInstanceFromPrefab(string name, GameObject prefab, Transform parent = null)
        {
            Debug.Assert(prefab != null, "Prefab must not be null");
            if (parent == null) return null;

            var existing = parent.Find(name);
            if (existing != null)
            {
                return existing.gameObject;
            }

            var instanced = CreateInstanceFromPrefab(prefab, parent);
            if (instanced == null)
            {
                Debug.LogError($"[RemoteControl] Failed to instantiate prefab: {prefab.name}");
                return null;
            }

            instanced.name = name;
            instanced.transform.SetParent(parent);
            return instanced;
        }

        /// <summary>
        /// Remove the given component if present (calls <see cref="Destroy(Object)"/>); the component
        /// is intentionally not re-added — call sites are expected to recreate it explicitly when needed.
        /// </summary>
        public static void ReplaceComponent<T>(GameObject instance) where T : Component
        {
            if (instance == null) return;
            T component = instance.GetComponent<T>();
            if (component != null)
            {
                Destroy(component);
            }
        }

        /// <summary>
        /// Prefabからインスタンスを生成する。
        /// エディターではPrefabUtilityを使用し、ランタイムではObject.Instantiateを使用する。
        /// </summary>
        public static GameObject InstantiatePrefab(GameObject prefab, Transform parent = null)
        {
            if (prefab == null) return null;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance != null && parent != null)
                {
                    instance.transform.SetParent(parent);
                }
                return instance;
            }
#endif
            return Object.Instantiate(prefab, parent);
        }

        /// <summary>
        /// Prefabからインスタンスを生成し、Undo操作に登録する。
        /// エディター非再生中はPrefabUtility + Undo.RegisterCreatedObjectUndoを使用する。
        /// </summary>
        public static GameObject InstantiatePrefabWithUndo(GameObject prefab, Transform parent = null)
        {
            if (prefab == null) return null;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance != null)
                {
                    if (parent != null)
                        instance.transform.SetParent(parent);
                    Undo.RegisterCreatedObjectUndo(instance, "Create " + prefab.name);
                }
                return instance;
            }
#endif
            return Object.Instantiate(prefab, parent);
        }

        /// <summary>
        /// Undo用にオブジェクトの状態を記録する（エディター非再生中のみ）。
        /// </summary>
        public static void RecordObjectUndo(Object obj, string name)
        {
#if UNITY_EDITOR
            if (obj != null && !Application.isPlaying)
            {
                Undo.RecordObject(obj, name);
            }
#endif
        }

        /// <summary>
        /// 現在のUndoグループ名を設定する（エディター非再生中のみ）。
        /// </summary>
        public static void SetCurrentUndoGroup(string name)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.SetCurrentGroupName(name);
            }
#endif
        }

        /// <summary>
        /// Objectを削除する。
        /// エディター非再生中はDestroyImmediate、ランタイムではDestroyを使用する。
        /// </summary>
        public static void Destroy(Object obj)
        {
            if (obj == null) return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(obj);
                return;
            }
#endif
            Object.Destroy(obj);
        }

        /// <summary>
        /// Objectを削除する。エディターではUndo対応で削除する。
        /// </summary>
        public static void DestroyWithUndo(Object obj)
        {
            if (obj == null) return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.DestroyObjectImmediate(obj);
                return;
            }
#endif
            Object.Destroy(obj);
        }

        /// <summary>
        /// オブジェクトをDirtyにマークする（エディターのみ）。
        /// </summary>
        public static void SetDirty(Object obj)
        {
#if UNITY_EDITOR
            if (obj != null)
            {
                EditorUtility.SetDirty(obj);
            }
#endif
        }

        /// <summary>
        /// 指定した型のコンポーネントがなければ追加する。
        /// </summary>
        public static T GetOrAddComponent<T>(GameObject go) where T : Component
        {
            if (go == null) return null;
            var component = go.GetComponent<T>();
            if (component == null)
            {
                component = go.AddComponent<T>();
            }
            return component;
        }

        /// <summary>
        /// 指定した型のコンポーネントが存在すれば削除する。
        /// immediate=true の場合は DestroyImmediate を使う。
        /// </summary>
        public static void RemoveComponent<T>(GameObject go, bool immediate = false) where T : Component
        {
            if (go == null) return;
            var component = go.GetComponent<T>();
            if (component == null) return;

            if (immediate)
            {
                Object.DestroyImmediate(component);
            }
            else
            {
                Destroy(component);
            }
        }

        // ----------------------------------------------------------------
        // Transform 親子付け / Hierarchy 変更通知
        //
        // Editor / PlayMode / Standalone の挙動差を個々の呼び出し元で扱わず、
        // ここに集約する。呼び出し元は `[ExecuteAlways]` 等を付与せず本 API を使う。
        // ----------------------------------------------------------------

        /// <summary>
        /// 指定 GameObject の親を付け替えることができるかを判定する。
        /// Prefab インスタンスの内部 (非ルート) の子はプレハブ構造保護のため付け替え不可。
        /// Play mode / Edit mode ともに Editor 内では PrefabUtility でチェックする。
        /// Standalone (非 Editor) ビルドでは常に true。
        /// </summary>
        /// <param name="reason">不可の場合、その理由メッセージ。可の場合は null。</param>
        public static bool CanReparent(GameObject go, out string reason)
        {
            reason = null;
            if (go == null) return true;
#if UNITY_EDITOR
            if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
                if (root != null && root != go)
                {
                    reason =
                        $"Cannot reparent '{go.name}': object is inside a Prefab instance (root: '{root.name}'). Unpack the prefab first or move the root.";
                    return false;
                }
            }
#endif
            return true;
        }

        /// <summary>
        /// self の親を parent に付け替える。ローカル TRS は維持する。
        /// Editor 非再生中は Undo 対応 (undoName 指定時)。Play mode / Standalone は通常の SetParent。
        /// </summary>
        public static void SetTransformParent(Transform self, Transform parent, string undoName = null)
        {
            if (self == null) return;

#if UNITY_EDITOR
            // Edit mode では Prefab インスタンス内部の子の親付け替えは禁止されている。
            // Play 終了直後に ExecuteAlways の OnEnable から Apply が走るケース等で失敗するため、
            // Edit mode では CanReparent 不可なら静かにスキップする (Play mode は Unity が許容)。
            if (!Application.isPlaying && !CanReparent(self.gameObject, out _)) return;

            if (!Application.isPlaying && !string.IsNullOrEmpty(undoName))
            {
                Undo.SetTransformParent(self, parent, undoName);
                // Undo.SetTransformParent は world position を維持する挙動のため、
                // ローカル TRS 維持のために続けて SetParent(false) を呼ぶ。
                self.SetParent(parent, false);
                return;
            }
#endif
            self.SetParent(parent, false);
        }

        /// <summary>
        /// シーンのヒエラルキー変化通知を購読する。
        /// - Editor (Edit mode / Play in Editor): EditorApplication.hierarchyChanged を橋渡し。
        /// - Standalone Runtime: 各対象 GameObject に付与された HierarchyChangeDispatcher の
        ///   OnTransformParentChanged / OnTransformChildrenChanged から発火される。
        /// </summary>
        public static void RegisterHierarchyChanged(System.Action callback)
        {
            if (callback == null) return;
            _hierarchyChangedCallbacks += callback;
#if UNITY_EDITOR
            _EnsureEditorHierarchyHook();
#endif
        }

        public static void UnregisterHierarchyChanged(System.Action callback)
        {
            if (callback == null) return;
            _hierarchyChangedCallbacks -= callback;
        }

        /// <summary>
        /// 登録された hierarchy 変更 callback を発火する。内部利用。
        /// HierarchyChangeDispatcher から呼ばれる。Editor hook 内でも使われる。
        /// </summary>
        internal static void InvokeHierarchyChanged()
        {
            _hierarchyChangedCallbacks?.Invoke();
        }

        static System.Action _hierarchyChangedCallbacks;

#if UNITY_EDITOR
        static bool _editorHierarchyHookInstalled;

        static void _EnsureEditorHierarchyHook()
        {
            if (_editorHierarchyHookInstalled) return;
            _editorHierarchyHookInstalled = true;
            EditorApplication.hierarchyChanged += _OnEditorHierarchyChanged;
        }

        static void _OnEditorHierarchyChanged()
        {
            if (EditorApplication.isCompiling) return;
            if (EditorApplication.isUpdating) return;
            // プレハブ編集モードは誤検知が多いのでスキップ。
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null) return;
            InvokeHierarchyChanged();
        }
#endif
    }
}
