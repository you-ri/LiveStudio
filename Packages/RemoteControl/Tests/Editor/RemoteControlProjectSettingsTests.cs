// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Project-wide live class assets: the declarations named by
    /// <see cref="RemoteControlProjectSettings"/> apply without a RemoteControlContainer.
    /// </summary>
    public class RemoteControlProjectSettingsTests
    {
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

        private LiveClassAsset _CreateAsset(System.Type type, string memberPath)
        {
            var asset = ScriptableObject.CreateInstance<LiveClassAsset>();
            _assets.Add(asset);
            asset.GetOrAddTypeDefinition(type).members.Add(new LiveClassAssetMember { path = memberPath });
            return asset;
        }

        /// <summary>
        /// Fills the settings' asset list the same way the Project Settings page does, so the
        /// private field stays private (a null entry stands for an empty inspector row).
        /// </summary>
        private RemoteControlProjectSettings _CreateSettings(params LiveClassAsset[] assets)
        {
            var settings = ScriptableObject.CreateInstance<RemoteControlProjectSettings>();
            _assets.Add(settings);

            using var so = new SerializedObject(settings);
            var list = so.FindProperty("_liveClassAssets");
            list.arraySize = assets.Length;
            for (int i = 0; i < assets.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = assets[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return settings;
        }

        [TearDown]
        public void TearDown()
        {
            // Drop the registrations before the assets go away, or the types stay declared for
            // the rest of the session and leak into other tests.
            foreach (var asset in _assets)
            {
                if (asset is LiveClassAsset liveClassAsset) LiveClassAssetSystem.UnregisterTypes(liveClassAsset);
            }
            foreach (var asset in _assets)
            {
                if (asset != null) Object.DestroyImmediate(asset);
            }
            _assets.Clear();
        }

        [Test]
        public void Apply_RegistersTypesFromEveryAsset()
        {
            var settings = _CreateSettings(
                _CreateAsset(typeof(BoxCollider), "isTrigger"),
                _CreateAsset(typeof(SphereCollider), "radius"));

            settings.Apply();

            var box = LiveClass.Find(typeof(BoxCollider));
            Assert.That(box, Is.Not.Null);
            Assert.That(box.FindProperty("isTrigger"), Is.Not.Null);

            var sphere = LiveClass.Find(typeof(SphereCollider));
            Assert.That(sphere, Is.Not.Null);
            Assert.That(sphere.FindProperty("radius"), Is.Not.Null);
        }

        [Test]
        public void Apply_WithEmptySlot_WarnsAndKeepsApplyingTheRest()
        {
            var settings = _CreateSettings(null, _CreateAsset(typeof(CapsuleCollider), "height"));

            settings.Apply();

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                "[RemoteControl] Live class asset slot 0 of the project settings is empty.");

            var capsule = LiveClass.Find(typeof(CapsuleCollider));
            Assert.That(capsule, Is.Not.Null);
            Assert.That(capsule.FindProperty("height"), Is.Not.Null);
        }

        [Test]
        public void Instance_FallsBackToThePackageDefault()
        {
            var instance = RemoteControlProjectSettings.Instance;

            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.liveClassAssets, Is.Not.Null);
        }
    }
}
