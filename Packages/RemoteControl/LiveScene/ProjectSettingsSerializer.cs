// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;

using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl;

namespace Lilium.RemoteControl.LiveScene
{
    /// <summary>
    /// Serializes/deserializes <see cref="PersistScope.Project"/>-scoped members into per-class
    /// project settings JSON. Unlike the live scene file (one file holding every object), project
    /// settings are grouped by owning class so each class persists into its own
    /// <c>{ClassName}.settings.json</c>.
    ///
    /// The on-disk shape mirrors the live scene file (<c>{ format, formatVersion, objects:[] }</c>)
    /// but every entry belongs to a single class and carries only Project-scoped members.
    ///
    /// Besides the top-level registered objects, this also walks the <c>[ExposedClass]</c> components
    /// of each exposed GameObject. Such components (e.g. ScreenController = "Screen") are not
    /// registered as standalone handles; in the live scene they are saved as inline-reference pending
    /// entries via <see cref="LiveSceneSerializer"/>. Here they are re-linked to their owning
    /// GameObject by <c>@parent</c> (the GameObject's stable id) so they can be resolved on apply.
    /// </summary>
    public static class ProjectSettingsSerializer
    {
        public const string FormatIdentifier = "jp.lilium.remotecontrol.projectsettings";
        public const int CurrentFormatVersion = 1;

        // Oldest version this reader accepts (see FormatHeader for the shared policy).
        public const int MinSupportedVersion = 1;

        /// <summary>
        /// Builds one JSON document per class for every object (and exposed component) that owns at
        /// least one Project-scoped member. The returned dictionary is keyed by class typeName
        /// (the file base name), value is the JSON document content.
        /// A full snapshot (not a delta) is taken so each settings file is self-contained and
        /// independent of the per-object default baseline.
        /// </summary>
        public static Dictionary<string, string> BuildPerClassJson(IReadOnlyList<ExposedObjectHandle> objects, IExposedObjectResolver resolver)
        {
            var entriesByClass = new Dictionary<string, JArray>(StringComparer.Ordinal);
            if (objects == null) return new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var obj in objects)
            {
                if (obj == null) continue;
                // Container 自身は live.json と同様に書き出さない。
                if (obj.target is ExposedObjectContainer) continue;

                // 1. オブジェクト自身の Project メンバー (id を持つもののみ — 復元時に @id で再解決する)。
                if (obj.hasId && _HasProjectScopedMember(obj))
                {
                    var jObj = JObject.Parse(ExposedPropertySerializer.ToJson(
                        obj, resolver, isDirtyOnly: false, forPersistence: true, scopeFilter: PersistScope.Project));
                    if (ExposedPropertySerializer.HasNonMetaProperties(jObj))
                    {
                        _AddEntry(entriesByClass, obj.targetTypeName, jObj);
                    }
                }

                // 2. 公開 GameObject の [ExposedClass] component を辿る (ScreenController="Screen" など)。
                _CollectComponentEntries(obj, resolver, entriesByClass);
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in entriesByClass)
            {
                var root = new JObject();
                FormatHeader.Write(root, FormatIdentifier, CurrentFormatVersion);
                root["objects"] = kv.Value;
                result[kv.Key] = root.ToString(Formatting.Indented);
            }
            return result;
        }

        /// <summary>
        /// Applies a single <c>{ClassName}.settings.json</c> document to the live objects.
        /// Top-level entries are resolved by <c>@id</c>; component entries by <c>@parent</c>
        /// (the owning GameObject's id) + <c>@type</c> + <c>@componentIndex</c>. Project-scoped values
        /// are written through. Idempotent: re-applying the same document re-sets the same values.
        /// </summary>
        public static void ApplyJson(string json, IExposedObjectResolver resolver)
        {
            if (string.IsNullOrEmpty(json)) return;

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException ex)
            {
                Debug.LogWarning($"[RemoteControl] Failed to parse project settings: {ex.Message}");
                return;
            }

            if (!FormatHeader.TryReadVersion(root, "Project settings", CurrentFormatVersion, MinSupportedVersion, out _)) return;

            if (!(root["objects"] is JArray jArray)) return;

            foreach (var jEntry in jArray)
            {
                if (!(jEntry is JObject jObject)) continue;

                // Component entry (@parent + @type + @componentIndex) vs top-level entry (@id).
                var parentId = jObject["@parent"]?.Value<string>();
                if (!string.IsNullOrEmpty(parentId))
                    _ApplyComponentEntry(jObject, parentId, resolver);
                else
                    _ApplyRootEntry(jObject, resolver);
            }
        }

        // Applies a top-level entry resolved by @id. Skips (with a warning) when the id is missing from
        // the live scene — e.g. the owning object is not present this session.
        private static void _ApplyRootEntry(JObject jObject, IExposedObjectResolver resolver)
        {
            var id = jObject["@id"]?.Value<string>();
            if (string.IsNullOrEmpty(id)) return;
            var handle = resolver.FindById(id);
            if (handle == null)
            {
                Debug.LogWarning($"[RemoteControl] Project settings: object '{id}' not found; entry skipped.");
                return;
            }
            ExposedPropertySerializer.FromJson(jObject.ToString(), handle.Value, resolver, captureDefaults: false);
        }

        // Walks the [ExposedClass] components of the GameObject behind an exposed object proxy and
        // emits a Project-scoped settings entry for each component that owns Project members.
        // Entries are linked to the owning GameObject by @parent so they can be re-resolved on apply.
        private static void _CollectComponentEntries(ExposedObjectHandle obj, IExposedObjectResolver resolver, Dictionary<string, JArray> entriesByClass)
        {
            if (!obj.hasId) return; // 親 id が無いと apply 時に component を再解決できない。
            var go = LiveSceneObjectUtil.GetGameObject(obj);
            if (go == null) return;

            var comps = go.GetComponents<Component>();
            var typeIndex = new Dictionary<Type, int>();
            foreach (var comp in comps)
            {
                if (comp == null) continue;
                var compType = comp.GetType();
                if (!ExposedClass.TryGet(compType, out var compClass)) continue;

                int idx = typeIndex.TryGetValue(compType, out var n) ? n : 0;
                typeIndex[compType] = idx + 1;

                // 既に独立ハンドルとして登録済みの component は top-level 経路で処理されるので二重出力を避ける。
                if (resolver.FindByTarget(comp) != null) continue;

                var ch = ExposedObjectHandle.CreateUnregistered(compClass, comp);
                if (!_HasProjectScopedMember(ch)) continue;

                var cObj = JObject.Parse(ExposedPropertySerializer.ToJson(
                    ch, resolver, isDirtyOnly: false, forPersistence: true, scopeFilter: PersistScope.Project));
                if (!ExposedPropertySerializer.HasNonMetaProperties(cObj)) continue;

                cObj.Remove("@id");
                cObj.Remove("@source");
                cObj["@parent"] = obj.id;
                cObj["@componentIndex"] = idx;
                _AddEntry(entriesByClass, compClass.typeName, cObj);
            }
        }

        private static void _ApplyComponentEntry(JObject jObject, string parentId, IExposedObjectResolver resolver)
        {
            var parent = resolver.FindById(parentId);
            if (parent == null)
            {
                Debug.LogWarning($"[RemoteControl] Project settings: parent '{parentId}' not found; component entry skipped.");
                return;
            }
            var go = LiveSceneObjectUtil.GetGameObject(parent.Value);
            if (go == null) return;

            var typeName = jObject["@type"]?.Value<string>();
            if (string.IsNullOrEmpty(typeName)) return;
            var compClass = ExposedClass.Find(typeName);
            if (compClass == null || compClass.type == null)
            {
                Debug.LogWarning($"[RemoteControl] Project settings: unknown component type '{typeName}' (deleted/renamed class?); entry skipped.");
                return;
            }

            int wantIdx = jObject["@componentIndex"]?.Value<int>() ?? 0;
            var matches = go.GetComponents(compClass.type);
            if (wantIdx < 0 || wantIdx >= matches.Length)
            {
                Debug.LogWarning($"[RemoteControl] Project settings: component index {wantIdx} out of range for '{typeName}' on '{go.name}' ({matches.Length} found); entry skipped.");
                return;
            }

            var ch = ExposedObjectHandle.CreateUnregistered(compClass, matches[wantIdx]);
            ExposedPropertySerializer.FromJson(jObject.ToString(), ch, resolver, captureDefaults: false);
        }

        private static void _AddEntry(Dictionary<string, JArray> entriesByClass, string className, JObject entry)
        {
            if (string.IsNullOrEmpty(className)) return;
            if (!entriesByClass.TryGetValue(className, out var arr))
            {
                arr = new JArray();
                entriesByClass[className] = arr;
            }
            arr.Add(entry);
        }


        private static bool _HasProjectScopedMember(ExposedObjectHandle obj)
        {
            var props = obj.propertyTypes;
            if (props == null) return false;
            for (int i = 0; i < props.Length; i++)
            {
                var pt = props[i];
                if (pt != null && pt.isPersistable && pt.persistScope == PersistScope.Project) return true;
            }
            return false;
        }
    }
}
