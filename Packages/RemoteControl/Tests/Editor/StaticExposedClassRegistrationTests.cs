// Copyright (c) You-Ri, 2026

using System.Linq;
using NUnit.Framework;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Editor.Tests
{
    /// <summary>
    /// Regression guard for static [ExposedClass] registration. The full attribute
    /// scan (_RegisterAllTypesFromAttributes) once ran from the static cctor before
    /// game assemblies were loaded, dropping static classes — which have no lazy
    /// registration path — from ExposedClass.all for the rest of the domain's life.
    /// The scan is now re-run from _RegisterStaticExposedObjects (AfterAssembliesLoaded /
    /// InitializeOnLoadMethod), so static classes are reliably present.
    /// </summary>
    public class StaticExposedClassRegistrationTests
    {
        [Test]
        public void Reset_RegistersStaticExposedClasses_InAll()
        {
            ExposedClass.Reset();

            var staticTypeNames = ExposedClass.all.Values
                .Where(t => t.isStatic)
                .Select(t => t.typeName)
                .ToList();

            // ProjectManager / Screen は LiveStudio の代表的な static [ExposedClass]。
            CollectionAssert.Contains(staticTypeNames, "ProjectManager",
                $"Static classes registered: [{string.Join(", ", staticTypeNames)}]");
        }

        [Test]
        public void Reset_RegistersStaticExposedObjectHandle_InRegistry()
        {
            ExposedClass.Reset();

            var handle = ExposedObjectRegistry.FindById("ProjectManager");
            Assert.IsTrue(handle.HasValue, "ProjectManager static handle should be registered.");
            Assert.IsTrue(handle.Value.isValid, "ProjectManager static handle should be valid.");
        }
    }
}
