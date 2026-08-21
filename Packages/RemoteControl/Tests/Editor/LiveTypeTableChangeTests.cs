// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

using Lilium.RemoteControl.LiveScene;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// A remote app fetches /live/types once per connection, so every later change to the type
    /// table has to reach it through the change feed. Without that, a type registered after the
    /// client connected (lazy resolution, a live class asset arriving with a scene or bundle, a
    /// scan that ran before an assembly was loaded) is invisible to it for the rest of the
    /// session and its properties render with no type information at all.
    /// </summary>
    public class LiveTypeTableChangeTests
    {
        private readonly List<string> _buffer = new List<string>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();
        private readonly List<GameObject> _gameObjects = new List<GameObject>();

        private class TypeTableProbe
        {
            public float number;
        }

        private enum TypeTableProbeEnum
        {
            First,
            Second,
        }

        [SetUp]
        public void SetUp()
        {
            LiveChangeLog.Clear();
            _buffer.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            if (LiveClass.TryGet(typeof(TypeTableProbe), out var probe)) LiveClass.Unregister(probe);

            foreach (var go in _gameObjects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _gameObjects.Clear();

            foreach (var asset in _assets)
            {
                if (asset != null) Object.DestroyImmediate(asset);
            }
            _assets.Clear();

            LiveChangeLog.Clear();
        }

        /// <summary>Ids recorded since the given revision.</summary>
        private List<string> _ChangesSince(long since)
        {
            LiveChangeLog.GetChangesSince(since, _buffer);
            return _buffer;
        }

        [Test]
        public void Register_RecordsTypesChange()
        {
            var since = LiveChangeLog.revision;

            LiveClass.Register(typeof(TypeTableProbe), "TypeTableProbe",
                new[] { new LivePropertyDefine { name = "number", path = "number" } });

            Assert.That(_ChangesSince(since), Contains.Item(LiveChangeLog.kTypesId),
                "A client that already fetched /live/types has to be told the table grew");
        }

        [Test]
        public void Unregister_RecordsTypesChange()
        {
            LiveClass.Register(typeof(TypeTableProbe), "TypeTableProbe", new LivePropertyDefine[0]);
            Assert.That(LiveClass.TryGet(typeof(TypeTableProbe), out var liveClass), Is.True);

            var since = LiveChangeLog.revision;
            LiveClass.Unregister(liveClass);

            Assert.That(_ChangesSince(since), Contains.Item(LiveChangeLog.kTypesId));
        }

        [Test]
        public void EnumRegisterAndUnregister_RecordTypesChange()
        {
            // /live/enums is fetched together with /live/types, so both share the pseudo id.
            var since = LiveChangeLog.revision;
            LiveEnum.Register<TypeTableProbeEnum>();
            Assert.That(_ChangesSince(since), Contains.Item(LiveChangeLog.kTypesId));

            var liveEnum = LiveEnum.Get<TypeTableProbeEnum>();
            Assert.That(liveEnum, Is.Not.Null);

            LiveChangeLog.Clear();
            since = LiveChangeLog.revision;
            LiveEnum.Unregister(liveEnum);

            Assert.That(_ChangesSince(since), Contains.Item(LiveChangeLog.kTypesId));
        }

        [Test]
        public void AssetTypeDefinition_RecordsTypesChangeOnApplyAndOnRemoval()
        {
            var preset = ScriptableObject.CreateInstance<LiveClassAsset>();
            _assets.Add(preset);
            var definition = preset.GetOrAddTypeDefinition(typeof(Light));
            definition.members.Add(new LiveClassAssetMember { path = "intensity" });

            var go = new GameObject("TypeTableChangeContainer");
            _gameObjects.Add(go);
            var container = go.AddComponent<RemoteControlContainer>();
            container.assets.Add(preset);

            var since = LiveChangeLog.revision;
            container.Reload();
            Assert.That(LiveClass.Has(typeof(Light)), Is.True);
            Assert.That(_ChangesSince(since), Contains.Item(LiveChangeLog.kTypesId),
                "A type that arrives with a scene or bundle has to reach connected clients");

            // Dropping the definition and reloading takes the registration away again; the client
            // has to hear about that too, or it keeps offering a type the server no longer has.
            LiveChangeLog.Clear();
            since = LiveChangeLog.revision;
            preset.typeDefinitions.Remove(definition);
            container.Reload();

            Assert.That(LiveClass.Has(typeof(Light)), Is.False);
            Assert.That(_ChangesSince(since), Contains.Item(LiveChangeLog.kTypesId));
        }

        [Test]
        public void AssetReapply_WithUnchangedDefinition_RecordsNothing()
        {
            var preset = ScriptableObject.CreateInstance<LiveClassAsset>();
            _assets.Add(preset);
            preset.GetOrAddTypeDefinition(typeof(Light)).members.Add(
                new LiveClassAssetMember { path = "intensity" });

            var go = new GameObject("TypeTableChangeIdempotent");
            _gameObjects.Add(go);
            var container = go.AddComponent<RemoteControlContainer>();
            container.assets.Add(preset);
            container.Reload();

            // Re-applying an unchanged definition is skipped by the signature check, so nothing
            // reaches the change feed. Otherwise "@types" would be pinned on every reload and
            // every client would refetch the whole table for nothing.
            LiveChangeLog.Clear();
            var since = LiveChangeLog.revision;
            LiveClassAssetSystem.RegisterTypes(preset);

            Assert.That(_ChangesSince(since), Does.Not.Contain(LiveChangeLog.kTypesId));
        }
    }
}
