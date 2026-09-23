// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl.LiveScene;

namespace Lilium.LiveStudio.Tests
{
    /// <summary>
    /// What a live scene written before the asset catalog left it still means.
    /// <para>
    /// Until then the catalog shadow (<c>_persistedAssets</c>) was where the show was read back from:
    /// which avatar was out, which stage was standing, which props were on it. Those answers now live
    /// on the objects that own them, and an entry naming a member that is gone is dropped in silence
    /// — so the failure this guards is not an exception but a scene that opens looking empty.
    /// </para>
    /// <para>
    /// Pure JSON in, JSON out: the rewrite runs before anything is applied, so nothing here needs a
    /// scene, a registry or an asset on disk. The fixtures are shaped after real files.
    /// </para>
    /// </summary>
    public class LegacyAssetCatalogMigrationTests
    {
        private const string kAvatarObjectId = "1bd72042-8b83-4a6d-b39c-c662487db3f0";
        private const string kPropObjectId = "20b19321-3bc5-4b59-99e3-15ff071c88be";

        // A file holding nothing but the catalog, which is what the older builds wrote most of the time.
        private static JObject _SceneWithCatalog(params JObject[] assets)
        {
            var persisted = new JArray();
            foreach (var asset in assets) persisted.Add(asset);

            return new JObject
            {
                ["objects"] = new JArray
                {
                    new JObject
                    {
                        ["@source"] = "a7d3f1e2-9c4b-4e85-b6a1-2f8c5d3e7b91",
                        ["@type"] = "ExternalAssetManager",
                        ["_persistedAssets"] = persisted,
                    },
                },
            };
        }

        private static JObject _Avatar(string name, bool enabled = true) => new JObject
        {
            ["@type"] = "AvatarAsset",
            ["name"] = name,
            ["path"] = "Avatars/" + name + ".vrm",
            ["enabled"] = enabled,
            ["objectId"] = kAvatarObjectId,
        };

        private static JObject _Set(string name, bool isActive) => new JObject
        {
            ["@type"] = "SetBundleAsset",
            ["name"] = name,
            ["path"] = "Sets/" + name + ".set.lsb",
            ["enabled"] = true,
            ["isActive"] = isActive,
        };

        private static JObject _Prop(string name, string path) => new JObject
        {
            ["@type"] = "PropAsset",
            ["name"] = name,
            ["path"] = path,
            ["enabled"] = true,
            ["objectId"] = kPropObjectId,
        };

        private static JObject _FindByType(JObject root, string typeName)
        {
            foreach (var entry in (JArray)root["objects"])
            {
                if (entry is JObject o && o["@type"]?.Value<string>() == typeName) return o;
            }
            return null;
        }

        [Test]
        public void TheAvatarTheCatalogHeld_BecomesTheSourcesSelection()
        {
            var scene = _SceneWithCatalog(_Avatar("CHR_SUG"));

            LegacyAssetCatalogMigration.Apply(scene);

            var source = _FindByType(scene, "ExternalAvatarSource");
            Assert.IsNotNull(source, "The avatar the catalog had loaded should now be the source's selection.");
            Assert.AreEqual("CHR_SUG", source["selectedAvatar"]?.Value<string>());
            Assert.AreEqual(kAvatarObjectId + ".components[ExternalAvatarSource]",
                source["@source"]?.Value<string>(),
                "The entry belongs on the GameObject the catalog moored the loaded avatar to.");
        }

        [Test]
        public void TheSetThatWasStanding_BecomesTheActiveStage()
        {
            var scene = _SceneWithCatalog(_Set("SampleRoom", isActive: true));

            LegacyAssetCatalogMigration.Apply(scene);

            var stage = _FindByType(scene, "StageManager");
            Assert.IsNotNull(stage);
            Assert.AreEqual("SampleRoom", stage["activeSet"]?.Value<string>());

            var loaded = stage["loadedSets"] as JArray;
            Assert.AreEqual(1, loaded?.Count, "A standing set is also a loaded one.");
            Assert.AreEqual("SampleRoom", loaded[0]["name"]?.Value<string>());
        }

        [Test]
        public void ASetThatWasLoadedButNotStanding_LeavesNoStageStanding()
        {
            // The older build read the bootstrap scene as active when no set claimed it, which is
            // what an absent activeSet says now. Nothing is inferred from a lone loaded set.
            var scene = _SceneWithCatalog(_Set("SampleRoom", isActive: false));

            LegacyAssetCatalogMigration.Apply(scene);

            var stage = _FindByType(scene, "StageManager");
            Assert.IsNull(stage["activeSet"], "No set was standing, so none should be made to stand.");
            Assert.AreEqual(1, (stage["loadedSets"] as JArray)?.Count);
        }

        [Test]
        public void APropTheCatalogHeld_ComesBackAsAnInstanceUnderTheIdItsSettingsAreAddressedTo()
        {
            var scene = _SceneWithCatalog(_Prop("Straw", "Props/Straw.prop.lsb"));

            // The prop's placement is already in the file, moored to the catalog entry's objectId.
            var settings = new JObject
            {
                ["@source"] = kPropObjectId + ".components[Item]",
                ["@type"] = "Item",
                ["attachment"] = new JObject { ["@type"] = "PropAttachment" },
            };
            ((JArray)scene["objects"]).Add(settings);

            LegacyAssetCatalogMigration.Apply(scene);

            JObject instance = null;
            foreach (var entry in (JArray)scene["objects"])
            {
                if (entry is JObject o && o["@prefab"] != null) instance = o;
            }

            Assert.IsNotNull(instance, "A prop that was out should come back as a scene instance.");
            Assert.AreEqual("Props/Straw.prop.lsb", instance["@prefab"]?.Value<string>(),
                "The project-relative path is the prop's @prefab key.");
            Assert.AreEqual(kPropObjectId, instance["@id"]?.Value<string>(),
                "Standing it up under the old objectId is what re-attaches the settings already in the file.");
            Assert.IsNotNull(_FindByType(scene, "Item"), "The settings entry should be left where it is.");
        }

        [Test]
        public void APropWithoutAPath_IsNotGuessedAt()
        {
            // Before 0.23.5 an entry named its asset and nothing else. Resolving a name needs the
            // project crawl, which has not run when this rewrite does.
            var nameOnly = _Prop("Straw", "Props/Straw.prop.lsb");
            nameOnly.Remove("path");
            var scene = _SceneWithCatalog(nameOnly);

            LegacyAssetCatalogMigration.Apply(scene);

            foreach (var entry in (JArray)scene["objects"])
            {
                Assert.IsNull((entry as JObject)?["@prefab"], "Nothing should be stood up from a name alone.");
            }
        }

        [Test]
        public void AnEntryThatWasNotOut_IsLeftOut()
        {
            var scene = _SceneWithCatalog(_Avatar("CHR_SUG", enabled: false));

            LegacyAssetCatalogMigration.Apply(scene);

            Assert.IsNull(_FindByType(scene, "ExternalAvatarSource"),
                "A disabled entry was a file the project had, not something on stage.");
        }

        [Test]
        public void WhatTheFileAlreadyStates_Wins()
        {
            // A build wrote both shapes for a while. The one it wrote itself is the newer answer.
            var scene = _SceneWithCatalog(_Avatar("FromCatalog"), _Set("FromCatalog", isActive: true));
            ((JArray)scene["objects"]).Add(new JObject
            {
                ["@source"] = kAvatarObjectId + ".components[ExternalAvatarSource]",
                ["@type"] = "ExternalAvatarSource",
                ["selectedAvatar"] = "FromTheScene",
            });
            ((JArray)scene["objects"]).Add(new JObject
            {
                ["@source"] = StageManager.kId,
                ["@type"] = "StageManager",
                ["activeSet"] = "FromTheScene",
            });

            LegacyAssetCatalogMigration.Apply(scene);

            Assert.AreEqual("FromTheScene", _FindByType(scene, "ExternalAvatarSource")["selectedAvatar"]?.Value<string>());
            Assert.AreEqual("FromTheScene", _FindByType(scene, "StageManager")["activeSet"]?.Value<string>());
        }

        [Test]
        public void TheCatalogGoesAway_AndAnEntryLeftHoldingNothingGoesWithIt()
        {
            var scene = _SceneWithCatalog(_Avatar("CHR_SUG"));

            LegacyAssetCatalogMigration.Apply(scene);

            Assert.IsNull(_FindByType(scene, "ExternalAssetManager"),
                "The manager entry held the catalog and nothing else, so it has nothing left to say.");
        }

        [Test]
        public void AManagerEntryThatSaysSomethingElse_Stays()
        {
            var scene = _SceneWithCatalog(_Avatar("CHR_SUG"));
            _FindByType(scene, "ExternalAssetManager")["name"] = "Assets";

            LegacyAssetCatalogMigration.Apply(scene);

            var manager = _FindByType(scene, "ExternalAssetManager");
            Assert.IsNotNull(manager);
            Assert.IsNull(manager["_persistedAssets"], "Only the catalog should be taken out of it.");
        }

        [Test]
        public void RunningTwice_ChangesNothingTheSecondTime()
        {
            var scene = _SceneWithCatalog(_Avatar("CHR_SUG"), _Set("SampleRoom", true), _Prop("Straw", "Props/Straw.prop.lsb"));

            LegacyAssetCatalogMigration.Apply(scene);
            var once = scene.ToString();
            LegacyAssetCatalogMigration.Apply(scene);

            Assert.AreEqual(once, scene.ToString(), "A migrated file has no catalog left to read.");
        }

        [Test]
        public void AFileWithNoCatalogInIt_IsLeftAlone()
        {
            var scene = new JObject
            {
                ["objects"] = new JArray
                {
                    new JObject { ["@source"] = StageManager.kId, ["@type"] = "StageManager", ["activeSet"] = "SampleRoom" },
                },
            };
            var before = scene.ToString();

            LegacyAssetCatalogMigration.Apply(scene);

            Assert.AreEqual(before, scene.ToString());
        }

        [Test]
        public void ARewriteRegisteredTwiceUnderOneId_RunsOnce()
        {
            // Both [RuntimeInitializeOnLoadMethod] and [InitializeOnLoadMethod] install it in the
            // editor, so the second install has to replace the first rather than stack on it.
            const string id = "Test.CountingMigration";
            int runs = 0;
            try
            {
                LiveSceneMigrations.Register(id, _ => runs++);
                LiveSceneMigrations.Register(id, _ => runs++);
                LiveSceneMigrations.Apply(new JObject());

                Assert.AreEqual(1, runs);
            }
            finally
            {
                LiveSceneMigrations.Unregister(id);
            }
        }
    }
}
