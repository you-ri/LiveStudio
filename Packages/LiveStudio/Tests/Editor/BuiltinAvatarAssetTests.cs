// Copyright (c) You-Ri, 2026

using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

using Lilium.LiveStudio;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// Pure-logic tests for the built-in avatar wiring: the <c>BuiltinAvatar</c> catalog kind coexisting
    /// with the built-in prop kind (both <c>GameObject</c>), the <see cref="BuiltinAvatarAsset"/> flags and
    /// its shared <see cref="AvatarAssetBase"/>, and the accept filter rejecting non-avatar prefabs.
    ///
    /// The positive accept path (a humanoid Animator plus an IAvatar / VRM 1.0 instance) needs a real
    /// humanoid <c>Avatar</c>, which cannot be constructed cheaply here; it is left to the Play-mode /
    /// manual E2E. These tests cover the classification and rejection logic that is unit-testable.
    /// </summary>
    public class BuiltinAvatarAssetTests
    {
        private readonly List<Object> _created = new List<Object>();

        private GameObject NewGameObject(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
        }

        [Test]
        public void BuiltinAvatarKind_IsRegistered_WithoutShadowingTheBuiltinPropKind()
        {
            var avatar = BuiltinAssetTypeRegistry.Find("BuiltinAvatar");
            Assert.IsNotNull(avatar, "the BuiltinAvatar descriptor should be registered");
            Assert.AreEqual("BuiltinAvatar", avatar.typeName);
            Assert.AreEqual(typeof(GameObject), avatar.assetType);
            Assert.IsFalse(avatar.isReference, "a loadable avatar is not a reference-only kind");

            // Two GameObject kinds coexist: the prop kind must still resolve under the default "GameObject"
            // key rather than being shadowed by the avatar kind.
            var prop = BuiltinAssetTypeRegistry.Find("GameObject");
            Assert.IsNotNull(prop);
            Assert.IsInstanceOf<BuiltinPropAsset>(
                prop.create(new BuiltinAssetCatalog.Entry { guid = "p" }));
        }

        [Test]
        public void BuiltinAvatarDescriptor_Create_YieldsBuiltinAvatarAssetCarryingTheGuid()
        {
            var asset = BuiltinAssetTypeRegistry.Find("BuiltinAvatar")
                .create(new BuiltinAssetCatalog.Entry { guid = "abc123" });

            Assert.IsInstanceOf<BuiltinAvatarAsset>(asset);
            Assert.AreEqual("abc123", ((BuiltinAvatarAsset)asset).guid);
        }

        [Test]
        public void BuiltinAvatarAsset_HasExpectedAssetFlags()
        {
            var asset = new BuiltinAvatarAsset { guid = "g" };

            Assert.IsTrue(asset.isExclusive, "avatars are a single-selection (radio) group");
            Assert.IsTrue(asset.isBuiltin);
            Assert.IsTrue(asset.isPersistable, "the selected built-in avatar is remembered across restarts");
            Assert.AreEqual("g", asset.persistentId, "the GUID is the persisted identity");
            Assert.IsInstanceOf<AvatarAssetBase>(asset);
            Assert.IsInstanceOf<AssetBase>(asset);
        }

        [Test]
        public void AvatarKinds_ShareAvatarAssetBase_AndAreExclusiveUnlikeProps()
        {
            Assert.IsInstanceOf<AvatarAssetBase>(new AvatarAsset());
            Assert.IsInstanceOf<AvatarAssetBase>(new BuiltinAvatarAsset());

            Assert.IsTrue(new AvatarAsset().isExclusive);
            Assert.IsTrue(new BuiltinAvatarAsset().isExclusive);

            // The exclusivity axis avatars share is real: a prop is additive, not exclusive.
            Assert.IsFalse(new PropAsset().isExclusive);
        }

        [Test]
        public void Accept_RejectsNonAvatarPrefabs()
        {
            var accept = BuiltinAssetTypeRegistry.Find("BuiltinAvatar").accept;
            Assert.IsNotNull(accept, "the avatar kind narrows by content, so it must have an accept filter");

            Assert.IsFalse(accept(null));

            // Not a GameObject at all.
            var so = ScriptableObject.CreateInstance<ScriptableObject>();
            _created.Add(so);
            Assert.IsFalse(accept(so));

            // A GameObject with no Animator.
            Assert.IsFalse(accept(NewGameObject("empty")));

            // An Animator whose Avatar is unset is not a humanoid rig (this also guards the fake-null trap:
            // the check uses Unity's `== null`, never the C# `is Animator` pattern).
            var rigged = NewGameObject("rigged");
            rigged.AddComponent<Animator>();
            Assert.IsFalse(accept(rigged));
        }
    }
}
