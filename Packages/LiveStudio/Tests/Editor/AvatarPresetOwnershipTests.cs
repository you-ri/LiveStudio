// Copyright (c) You-Ri, 2026
using System.Collections.Generic;

using NUnit.Framework;
using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio.Tests
{
    /// <summary>
    /// Which controller an avatar's settings file belongs to, when there is more than one controller.
    /// <para>
    /// A set scene loaded additively can bring a second <see cref="AvatarController"/> (see its
    /// OnDisable). Which avatar is out, though, is answered once by the asset layer, and every avatar
    /// load is routed to a single controller — so that answer is about that one controller. Before this
    /// was said, both controllers resolved the same answer and wrote the same file, each overwriting
    /// the other's settings; with no avatar selected, both wrote <c>default.preset.json</c>.
    /// </para>
    /// <para>
    /// The controllers are registered here by hand, as their OnEnable does at runtime: a MonoBehaviour
    /// added while not playing never receives it.
    /// </para>
    /// </summary>
    public class AvatarPresetOwnershipTests
    {
        private const string kAvatarServiceId = "current";

        private readonly List<AvatarController> _registered = new List<AvatarController>();

        private AvatarController _AddController(string name)
        {
            var controller = new GameObject(name, typeof(AvatarController)).GetComponent<AvatarController>();
            SelectableService<IAvatarService>.Register(kAvatarServiceId, controller);
            _registered.Add(controller);
            return controller;
        }

        private void _RemoveController(AvatarController controller)
        {
            SelectableService<IAvatarService>.Unregister(kAvatarServiceId, controller);
            _registered.Remove(controller);
            if (controller != null) Object.DestroyImmediate(controller.gameObject);
        }

        [SetUp]
        public void SetUp()
        {
            Assert.IsNull(SelectableService<IAvatarService>.Select(kAvatarServiceId),
                "another test left an avatar controller registered");
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _registered.Count - 1; i >= 0; i--)
            {
                var controller = _registered[i];
                SelectableService<IAvatarService>.Unregister(kAvatarServiceId, controller);
                if (controller != null) Object.DestroyImmediate(controller.gameObject);
            }
            _registered.Clear();
        }

        [Test]
        public void TheSecondControllerInAScene_KeepsNoSettingsFileOfItsOwn()
        {
            var first = _AddController("avatar-controller");
            var second = _AddController("avatar-controller-from-a-set");

            // Where AvatarService.Load sends an avatar, and so who the asset layer's answer is about.
            Assert.AreSame(first, SelectableService<IAvatarService>.Select(kAvatarServiceId));

            Assert.IsTrue(first.keepsAvatarPreset);
            Assert.IsFalse(second.keepsAvatarPreset, "would write the file the first one owns");
        }

        [Test]
        public void WhenTheControllerThatOwnedThemGoesAway_TheRemainingOneTakesThemOver()
        {
            var first = _AddController("avatar-controller");
            var second = _AddController("avatar-controller-from-a-set");
            Assert.IsFalse(second.keepsAvatarPreset, "precondition: the first one owns them");

            _RemoveController(first);

            // Avatar loads are routed to whoever is left, so the settings file follows them there.
            Assert.IsTrue(second.keepsAvatarPreset);
        }
    }
}
