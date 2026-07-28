// Copyright (c) You-Ri, 2026

using System.Linq;
using NUnit.Framework;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Editor.Tests
{
    /// <summary>
    /// Regression guard for static [LiveClass] registration. The full attribute
    /// scan (_RegisterAllTypesFromAttributes) once ran from the static cctor before
    /// game assemblies were loaded, dropping static classes — which have no lazy
    /// registration path — from LiveClass.all for the rest of the domain's life.
    /// The scan is now re-run from _RegisterStaticLiveObjects (AfterAssembliesLoaded /
    /// InitializeOnLoadMethod), so static classes are reliably present.
    /// </summary>
    public class StaticLiveClassRegistrationTests
    {
        [Test]
        public void Reset_RegistersStaticLiveClasses_InAll()
        {
            LiveClass.Reset();

            var staticTypeNames = LiveClass.all.Values
                .Where(t => t.isStatic)
                .Select(t => t.typeName)
                .ToList();

            // LiveSceneManager / Screen は LiveStudio の代表的な static [LiveClass]。
            CollectionAssert.Contains(staticTypeNames, "LiveSceneManager",
                $"Static classes registered: [{string.Join(", ", staticTypeNames)}]");
        }

        [Test]
        public void Reset_RegistersStaticLiveObjectHandle_InRegistry()
        {
            LiveClass.Reset();

            var handle = LiveObjectRegistry.FindById("LiveSceneManager");
            Assert.IsTrue(handle.HasValue, "LiveSceneManager static handle should be registered.");
            Assert.IsTrue(handle.Value.isValid, "LiveSceneManager static handle should be valid.");
        }
    }
}
