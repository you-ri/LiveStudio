// Copyright (c) You-Ri, 2026
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Verifies that <see cref="RemoteControlContainer"/> self-registers in its static registry and
    /// raises register/unregister events as it enables/disables.
    /// </summary>
    [TestFixture]
    public class RemoteControlContainerRegistryTests
    {
        [Test]
        public void Enable_AddsToAll_Disable_Removes()
        {
            var go = new GameObject("rcc");
            try
            {
                var container = go.AddComponent<RemoteControlContainer>(); // ExecuteAlways -> OnEnable fires
                Assert.IsTrue(RemoteControlContainer.all.Contains(container), "Container should register on enable.");

                container.enabled = false; // OnDisable
                Assert.IsFalse(RemoteControlContainer.all.Contains(container), "Container should unregister on disable.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Enable_RaisesOnRegistered()
        {
            RemoteControlContainer registered = null;
            System.Action<RemoteControlContainer> handler = c => registered = c;
            RemoteControlContainer.onRegistered += handler;

            var go = new GameObject("rcc");
            try
            {
                var container = go.AddComponent<RemoteControlContainer>();
                Assert.AreSame(container, registered, "onRegistered should fire with the enabled container.");
            }
            finally
            {
                RemoteControlContainer.onRegistered -= handler;
                Object.DestroyImmediate(go);
            }
        }
    }
}
