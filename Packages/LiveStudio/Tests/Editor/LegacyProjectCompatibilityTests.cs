// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace Lilium.LiveStudio.Tests
{
    /// <summary>
    /// Every sample project this app has shipped, and what its scene still puts on stage.
    ///
    /// <para>
    /// The unit tests beside this one pin the migration rule by rule, on fixtures written to exercise
    /// one rule each. This pins the promise those rules exist for: a project somebody downloaded with
    /// an older release opens with its avatar, its stage and its props, and keeps opening with them.
    /// The files here are the real <c>*.live.json</c> out of the shipped archives, reformatted (LF,
    /// two-space) so a diff between two releases reads as the change it is and nothing else.
    /// </para>
    /// <para>
    /// **Adding a release** is two steps: <c>python Tests/Editor/LegacyProjects~/update.py [archive]</c>
    /// to take the scenes out of the shipped archive, then a row below saying what that release had on
    /// stage. The row is what covers a release — a file on its own says nothing. Only the scenes are
    /// kept: the avatars and bundles beside them come to tens of megabytes and nothing here reads them,
    /// since a migration is text in and text out. To run a release against its real assets, unpack the
    /// archive itself and open it in the app.
    /// </para>
    /// <para>
    /// ⚠ Two of these are honest records of a shipped mistake rather than of correct content, and are
    /// left as they are: <c>SampleProject@0.24.1</c> names an avatar (<c>VRoid_V110_Male_v1.1.3</c>)
    /// that is not in its own archive, and the props in <c>SampleProject@0.25.x</c> are saved with no
    /// stage under them. Both still have to survive the migration; whether they were worth shipping is
    /// a different question from whether they still open.
    /// </para>
    /// </summary>
    public class LegacyProjectCompatibilityTests
    {
        /// <summary>What one shipped release's scene is expected to put on stage.</summary>
        public class Shipped
        {
            public string scene;            // path under LegacyProjects~
            public string avatar;           // ExternalAvatarSource.selectedAvatar, null for none
            public string activeSet;        // StageManager.activeSet, null when nothing was standing
            public string[] loadedSets;
            public string[] props;          // @prefab keys, in catalog order

            public override string ToString() => scene;
        }

        private static readonly Shipped[] kShipped =
        {
            // The oldest archive still around. Pre-rename throughout: ActionManager, ExposedLight, and
            // an expression config addressed by component index rather than by type.
            new Shipped
            {
                scene = "SampleProject@0.24.1-exp.1/Start.live.json",
                avatar = "VRoid_V110_Male_v1.1.3",
                activeSet = "SampleRoomSet",
                loadedSets = new[] { "SampleRoomSet" },
                props = new string[0],
            },

            // Two props out and no stage under them. The chair carries saved settings; the mic does not.
            new Shipped
            {
                scene = "SampleProject@0.25.2-exp.1/Start.live.json",
                avatar = "7989649190987387872",
                activeSet = null,
                loadedSets = new string[0],
                props = new[] { "Props/SampleChair.prop.lsb", "Props/SampleHandMic.prop.lsb" },
            },
            new Shipped
            {
                scene = "SampleProject@0.25.3-exp.1/Start.live.json",
                avatar = "7989649190987387872",
                activeSet = null,
                loadedSets = new string[0],
                props = new[] { "Props/SampleChair.prop.lsb", "Props/SampleHandMic.prop.lsb" },
            },
            new Shipped
            {
                scene = "LiveProjectSample@0.25.3-exp.1/Start.live.json",
                avatar = "7989649190987387872",
                activeSet = null,
                loadedSets = new string[0],
                props = new[] { "Props/SampleChair.prop.lsb", "Props/SampleHandMic.prop.lsb" },
            },

            // Written while both shapes were saved: the scene already answers for the avatar and the
            // stage, and only the prop is left for the catalog to answer.
            new Shipped
            {
                scene = "LiveProjectSample@0.26.0-exp.1/Start.live.json",
                avatar = "7989649190987387872",
                activeSet = "SampleRoom",
                loadedSets = new[] { "SampleRoom" },
                props = new[] { "Props/SampleHandMic.prop.lsb" },
            },
            // A set loaded without being the one standing, which is the bootstrap scene staying up.
            new Shipped
            {
                scene = "LiveProjectSample@0.26.0-exp.1/Untitled.live.json",
                avatar = "7989649190987387872",
                activeSet = null,
                loadedSets = new[] { "SampleRoom" },
                props = new string[0],
            },
        };

        private static string _FixtureRoot
            => Path.GetFullPath("Packages/jp.lilium.livestudio/Tests/Editor/LegacyProjects~");

        private static JObject _Read(Shipped shipped)
        {
            var path = Path.Combine(_FixtureRoot, shipped.scene);
            Assert.IsTrue(File.Exists(path), $"Fixture missing: {path}");
            return JObject.Parse(File.ReadAllText(path));
        }

        private static JObject _Migrated(Shipped shipped)
        {
            var root = _Read(shipped);
            LegacyAssetCatalogMigration.Apply(root);
            return root;
        }

        private static JObject _FindByType(JObject root, string typeName)
        {
            foreach (var entry in (JArray)root["objects"])
            {
                if (entry is JObject o && o["@type"]?.Value<string>() == typeName) return o;
            }
            return null;
        }

        private static List<JObject> _Instances(JObject root)
        {
            var result = new List<JObject>();
            foreach (var entry in (JArray)root["objects"])
            {
                if (entry is JObject o && o["@prefab"] != null) result.Add(o);
            }
            return result;
        }

        [TestCaseSource(nameof(kShipped))]
        public void TheAvatarItShippedWith_IsStillTheOneOut(Shipped shipped)
        {
            var source = _FindByType(_Migrated(shipped), "ExternalAvatarSource");
            Assert.AreEqual(shipped.avatar, source?["selectedAvatar"]?.Value<string>());
        }

        [TestCaseSource(nameof(kShipped))]
        public void TheStageItShippedWith_IsStillStanding(Shipped shipped)
        {
            var stage = _FindByType(_Migrated(shipped), "StageManager");

            Assert.AreEqual(shipped.activeSet, stage?["activeSet"]?.Value<string>());

            var loaded = new List<string>();
            foreach (var element in (stage?["loadedSets"] as JArray) ?? new JArray())
                loaded.Add(element["name"]?.Value<string>());
            CollectionAssert.AreEqual(shipped.loadedSets, loaded);
        }

        [TestCaseSource(nameof(kShipped))]
        public void ThePropsItShippedWith_AreStillOut(Shipped shipped)
        {
            var keys = new List<string>();
            foreach (var instance in _Instances(_Migrated(shipped)))
                keys.Add(instance["@prefab"]?.Value<string>());

            CollectionAssert.AreEqual(shipped.props, keys);
        }

        [TestCaseSource(nameof(kShipped))]
        public void APropsSavedSettings_FindTheInstanceThatComesBack(Shipped shipped)
        {
            // The mooring the whole prop rule turns on. Settings written against a catalog entry are
            // addressed `{objectId}.components[...]`, and the instance is stood up under that same id,
            // so the entries already in the file land back on it. Break the link and the prop is still
            // out — at its defaults, off its socket — which none of the assertions above would notice.
            var original = _Read(shipped);
            var migrated = _Read(shipped);
            LegacyAssetCatalogMigration.Apply(migrated);

            foreach (var objectId in _PropObjectIds(original))
            {
                var instance = _FindById(migrated, objectId);
                Assert.IsNotNull(instance,
                    $"The prop moored to '{objectId}' has no instance to attach its settings to.");
                Assert.IsNotNull(instance["@prefab"],
                    $"'{objectId}' came back as something other than a prefab instance.");

                CollectionAssert.AreEquivalent(
                    _SettingsAddressedTo(original, objectId),
                    _SettingsAddressedTo(migrated, objectId),
                    $"The settings addressed to '{objectId}' should be left exactly where they are.");
            }
        }

        // The objectId of every prop the catalog had out, which is what its saved settings are filed under.
        private static List<string> _PropObjectIds(JObject root)
        {
            var result = new List<string>();
            var manager = _FindByType(root, "ExternalAssetManager");
            foreach (var entry in (manager?["_persistedAssets"] as JArray) ?? new JArray())
            {
                if (!(entry is JObject asset)) continue;
                if (asset["enabled"]?.Value<bool>() != true) continue;

                var type = asset["@type"]?.Value<string>();
                if (type != "PropAsset" && type != "BuiltinPropAsset") continue;

                var objectId = asset["objectId"]?.Value<string>();
                if (!string.IsNullOrEmpty(objectId)) result.Add(objectId);
            }
            return result;
        }

        private static List<string> _SettingsAddressedTo(JObject root, string objectId)
        {
            var result = new List<string>();
            foreach (var entry in (JArray)root["objects"])
            {
                var source = (entry as JObject)?["@source"]?.Value<string>();
                if (source != null && source.StartsWith(objectId + ".")) result.Add(source);
            }
            return result;
        }

        private static JObject _FindById(JObject root, string id)
        {
            foreach (var entry in (JArray)root["objects"])
            {
                if (entry is JObject o && o["@id"]?.Value<string>() == id) return o;
            }
            return null;
        }

        [TestCaseSource(nameof(kShipped))]
        public void TheCatalogItShippedWith_DoesNotSurviveTheRead(Shipped shipped)
        {
            foreach (var entry in (JArray)_Migrated(shipped)["objects"])
            {
                Assert.IsNull((entry as JObject)?["_persistedAssets"],
                    "The catalog is a setting of the machine and has no business in a scene.");
            }
        }
    }
}
