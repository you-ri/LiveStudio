// Copyright (c) You-Ri, 2026

using System.Collections.Generic;

using UnityEngine;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl.LiveScene;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Reads the asset catalog an older build wrote into the live scene (<c>_persistedAssets</c>) and
    /// says what it said in the terms this build uses.
    ///
    /// Until the catalog was taken out of the scene it carried the show: which avatar was out, which
    /// stage was standing and which props were on it were all read back off entries in that shadow.
    /// The catalog is a setting of the machine -- which files this disk happens to have -- so it no
    /// longer reaches a saved scene, and the show says those three things itself:
    /// <see cref="ExternalAvatarSource.selectedAvatar"/>, <see cref="StageManager.activeSet"/> /
    /// <see cref="StageManager.loadedSets"/>, and a prop as the scene instance it is. An unmigrated
    /// file still parses -- the shadow simply finds no owner -- so without this, a scene saved before
    /// the change opens stripped of its avatar, its stage and its props with nothing said.
    ///
    /// <para>
    /// The old entries hold everything the new ones need, so this is a rename and not a guess: each
    /// carries the <c>name</c> the new members address assets by, a set says whether it was the one
    /// standing, and a prop carries the project-relative path that is its <c>@prefab</c> key.
    /// </para>
    /// <para>
    /// The prop case is the one worth knowing. The old shadow moored a prop's saved settings to an
    /// <c>objectId</c>, and the file still holds those settings as top-level entries addressed
    /// <c>{objectId}.components[Item]</c>. Giving the migrated instance that same id as its
    /// <c>@id</c> re-attaches them where they were, so a prop comes back on its socket at the offset
    /// it was placed at rather than at defaults -- no table mapping old ids to new is needed.
    /// </para>
    /// <para>
    /// ⚠ A prop needs the entry to carry <c>path</c>, which they began doing in 0.23.5. An avatar and
    /// a set do not: the members that hold them address an asset by name, which every generation of
    /// the shadow wrote, so an older file still comes back with its avatar and its stage and loses
    /// only its props. Resolving a prop from a name instead would mean waiting for the project crawl
    /// to finish, which a rewrite running before the scene is applied cannot do.
    /// </para>
    /// <para>
    /// ⚠ A scene already re-saved by a build without the catalog cannot be recovered by anything:
    /// the shadow is gone from the file, and all that is left of a prop is an orphaned settings
    /// entry addressed to an id nothing carries any more.
    /// </para>
    /// </summary>
    public static class LegacyAssetCatalogMigration
    {
        /// <summary>Key this rewrite is registered under, so installing twice replaces it.</summary>
        public const string kMigrationId = "LiveStudio.LegacyAssetCatalog";

        // The member the catalog used to be saved under. Gone from ExternalAssetManager, so this
        // name now exists only here.
        private const string kPersistedAssets = "_persistedAssets";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        private static void _Install() => LiveSceneMigrations.Register(kMigrationId, Apply);

        /// <summary>
        /// Rewrites one parsed live scene in place. Does nothing to a file with no catalog in it, and
        /// leaves any member the file already states alone -- a scene written while both shapes were
        /// saved says it twice, and the one this build wrote is the newer answer.
        /// </summary>
        public static void Apply(JObject root)
        {
            if (!(root?["objects"] is JArray objects)) return;

            var manager = _FindByType(objects, "ExternalAssetManager");
            if (!(manager?[kPersistedAssets] is JArray persisted)) return;

            string avatarName = null;
            string avatarObjectId = null;
            string activeSet = null;
            var loadedSets = new List<string>();
            var props = new List<KeyValuePair<string, string>>();   // @prefab key -> @id

            foreach (var entry in persisted)
            {
                if (!(entry is JObject asset)) continue;

                // Only what was out. A disabled entry was a file the project had and nothing more,
                // which is the catalog's business and no longer the scene's.
                if (asset["enabled"]?.Value<bool>() != true) continue;

                var name = asset["name"]?.Value<string>();
                switch (asset["@type"]?.Value<string>())
                {
                    case "AvatarAsset":
                        avatarName = name;
                        avatarObjectId = asset["objectId"]?.Value<string>();
                        break;

                    // SceneBundleAsset is what a set bundle was called before it was a set.
                    case "SetBundleAsset":
                    case "SceneBundleAsset":
                    case "BuiltinSetAsset":
                        if (string.IsNullOrEmpty(name)) break;
                        loadedSets.Add(name);
                        // Absent means no set was standing and the bootstrap scene was, which is
                        // what an empty activeSet says now. Nothing to infer from a lone loaded set.
                        if (asset["isActive"]?.Value<bool>() == true) activeSet = name;
                        break;

                    case "PropAsset":
                    case "BuiltinPropAsset":
                    {
                        var key = _PropInstanceKey(asset);
                        var objectId = asset["objectId"]?.Value<string>();
                        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(objectId)) break;
                        props.Add(new KeyValuePair<string, string>(key, objectId));
                        break;
                    }
                }
            }

            _ApplyAvatar(objects, avatarName, avatarObjectId);
            _ApplyStage(objects, activeSet, loadedSets);
            _ApplyProps(objects, props);

            manager.Remove(kPersistedAssets);

            // A manager entry that held the catalog and nothing else has nothing left to say.
            if (manager.Count <= 2 && manager["@source"] != null && manager["@type"] != null)
                manager.Remove();
        }

        // The @prefab key that re-instantiates this prop, matching IInstantiableProp.instanceKey: the
        // catalog GUID for a built-in prop, the project-relative path for one that is a file.
        private static string _PropInstanceKey(JObject asset)
        {
            var guid = asset["guid"]?.Value<string>();
            if (!string.IsNullOrEmpty(guid)) return guid;
            return asset["path"]?.Value<string>();
        }

        private static void _ApplyAvatar(JArray objects, string avatarName, string avatarObjectId)
        {
            if (string.IsNullOrEmpty(avatarName)) return;

            var source = _FindByType(objects, "ExternalAvatarSource");
            if (source == null)
            {
                // The catalog moored the loaded avatar to the wrapper GameObject the source sits on,
                // so its objectId is the address the component entry is written under.
                if (string.IsNullOrEmpty(avatarObjectId)) return;
                source = _AddEntry(objects, "ExternalAvatarSource",
                    avatarObjectId + ".components[ExternalAvatarSource]");
            }

            if (source["selectedAvatar"] == null) source["selectedAvatar"] = avatarName;
        }

        private static void _ApplyStage(JArray objects, string activeSet, List<string> loadedSets)
        {
            if (string.IsNullOrEmpty(activeSet) && loadedSets.Count == 0) return;

            var stage = _FindByType(objects, "StageManager")
                        ?? _AddEntry(objects, "StageManager", StageManager.kId);

            if (activeSet != null && stage["activeSet"] == null) stage["activeSet"] = activeSet;

            if (loadedSets.Count == 0 || stage["loadedSets"] != null) return;

            var jLoaded = new JArray();
            foreach (var name in loadedSets)
            {
                // @op marks the element as one the restore stands up, the same way a saved
                // loadedSets element does; the element being there is what says the set is loaded.
                jLoaded.Add(new JObject
                {
                    ["@type"] = "LoadedSet",
                    ["name"] = name,
                    ["@op"] = "new",
                });
            }
            stage["loadedSets"] = jLoaded;
        }

        private static void _ApplyProps(JArray objects, List<KeyValuePair<string, string>> props)
        {
            foreach (var prop in props)
            {
                // Already stood up -- a file written while both shapes were saved carries the
                // instance as well, and standing a second one up would double the prop.
                if (_FindById(objects, prop.Value) != null) continue;

                // A *.prop.lsb prop is a socket-driven prop (its root carries an IProp component),
                // which PropObjectFactory wraps as a plain GameObject: its pose is written every
                // frame from the attachment, so it exposes no transform of its own to edit.
                objects.Add(new JObject
                {
                    ["@prefab"] = prop.Key,
                    ["@type"] = "GameObject",
                    ["@id"] = prop.Value,
                });
            }
        }

        private static JObject _FindByType(JArray objects, string typeName)
        {
            foreach (var entry in objects)
            {
                if (entry is JObject o && o["@type"]?.Value<string>() == typeName) return o;
            }
            return null;
        }

        private static JObject _FindById(JArray objects, string id)
        {
            foreach (var entry in objects)
            {
                if (!(entry is JObject o)) continue;
                if (o["@id"]?.Value<string>() == id || o["@source"]?.Value<string>() == id) return o;
            }
            return null;
        }

        private static JObject _AddEntry(JArray objects, string typeName, string source)
        {
            var entry = new JObject
            {
                ["@source"] = source,
                ["@type"] = typeName,
            };
            objects.Add(entry);
            return entry;
        }
    }
}
