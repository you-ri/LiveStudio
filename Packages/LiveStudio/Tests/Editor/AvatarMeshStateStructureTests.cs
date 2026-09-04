// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using UnityEngine;

using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Generated;

namespace Lilium.LiveStudio.Tests
{
    /// <summary>
    /// The mesh overrides of a real avatar, in both lanes.
    ///
    /// The synthetic fixtures cover the walk; this covers the type the feature exists for. An
    /// override is an element of a collection on a MonoBehaviour exposed by its type name, which is
    /// a different way into the walk than a registered object, and the two have to agree: the state
    /// lane carries `visible` under a composed address, and the inventory has to name the same
    /// element so a replay can stand it back up.
    /// </summary>
    public class AvatarMeshStateStructureTests
    {
        private GameObject _go;
        private AvatarController _avatar;

        [SetUp]
        public void SetUp()
        {
            LiveObjectRegistry.ClearAll();

            _go = new GameObject("Avatar");
            _avatar = _go.AddComponent<AvatarController>();

            LiveObjectRoster.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);

            LiveObjectRegistry.ClearAll();
            LiveObjectRoster.Refresh();
        }

        private void _SetOverrides(params string[] names)
        {
            var overrides = new MeshState[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                overrides[i] = new MeshState { name = names[i], visible = false };
            }

            var field = typeof(AvatarController).GetField("meshStateOverrides",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.IsNotNull(field, "meshStateOverrides is gone or renamed");
            field.SetValue(_avatar, overrides);
        }

        [Test]
        public void AMeshOverride_IsCarriedByTheStateLane()
        {
            _SetOverrides("Armature/Body");

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            var block = state.Find<Lilium_LiveStudio_MeshStateLiveStateBlock>();
            Assert.IsNotNull(block, "no mesh override reached the state lane");
            Assert.AreEqual(1, block.count);
        }

        [Test]
        public void AnAvatarWithNoOverrides_StillListsItsCollections()
        {
            // What the viewer draws under a selected object. With nothing configured the collections
            // still have to appear, or "recorded and empty" reads as "the walk never got here" --
            // which is the difference the window exists to show.
            var symbols = FrameGate.symbols;
            using var structure = new StructureBlock();
            LiveStructureSystem.CaptureInto(structure, symbols);

            var ownerId = FrameSymbolTable.kNone;
            var members = new List<string>();

            for (int i = 0; i < structure.count; i++)
            {
                var entry = structure[i];
                if (!entry.isCollection) continue;

                var member = symbols.Resolve(entry.memberId);
                if (member != "meshStateOverrides" && member != "animationParameterOverrides") continue;

                ownerId = entry.parentId;
                members.Add(member);
            }

            CollectionAssert.Contains(members, "meshStateOverrides",
                "an empty collection is nowhere in the inventory");
            CollectionAssert.Contains(members, "animationParameterOverrides");

            // The viewer matches rows by the owner the state lane selected, so the two ids have to
            // be the same symbol -- not merely the same-looking string.
            Assert.AreEqual(symbols.Intern("Avatar"), ownerId,
                "the collection is filed under an owner the state lane never names");
        }

        [Test]
        public void AMeshOverride_IsInTheInventory()
        {
            _SetOverrides("Armature/Body");

            var symbols = FrameGate.symbols;
            using var structure = new StructureBlock();
            LiveStructureSystem.CaptureInto(structure, symbols);

            var found = false;
            for (int i = 0; i < structure.count; i++)
            {
                var entry = structure[i];
                if (!entry.isElement) continue;
                if (symbols.Resolve(entry.memberId) != "meshStateOverrides") continue;

                Assert.AreEqual("Armature/Body", symbols.Resolve(entry.keyId),
                    "the key is the mesh path, slashes and all");
                found = true;
            }

            Assert.IsTrue(found, "the mesh override is nowhere in the inventory");
        }

        [Test]
        public void TheTwoLanes_AgreeOnTheAddress()
        {
            // The whole point of sharing the walk. A value carried under one address and an
            // inventory entry naming another is the shape of every bug this area has had.
            _SetOverrides("Armature/Body", "Armature/Hair");

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            var symbols = FrameGate.symbols;
            using var structure = new StructureBlock();
            LiveStructureSystem.CaptureInto(structure, symbols);

            var block = state.Find<Lilium_LiveStudio_MeshStateLiveStateBlock>();
            Assert.IsNotNull(block, "no mesh override reached the state lane");

            var checkedAny = false;
            for (int i = 0; i < structure.count; i++)
            {
                var entry = structure[i];
                if (!entry.isElement) continue;
                if (symbols.Resolve(entry.memberId) != "meshStateOverrides") continue;

                Assert.GreaterOrEqual(block.IndexOfOwner(entry.id), 0,
                    $"the inventory names '{symbols.Resolve(entry.id)}' and the state lane does not");
                checkedAny = true;
            }

            Assert.IsTrue(checkedAny, "no mesh override was in the inventory to check");
        }
    }
}
