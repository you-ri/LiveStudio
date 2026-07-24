// Copyright (c) You-Ri, 2026

using NUnit.Framework;

using UnityEngine;

using Newtonsoft.Json.Linq;

using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Pins the polymorphic-grouping contract that lets a client filter by base type without enumerating
    /// concrete <c>@type</c>s: <see cref="ExposedClass.baseTypeNames"/> (the user-defined base-class chain),
    /// <see cref="ExposedClass.IsSubclassOf(string)"/> (strict, mirroring <c>Type.IsSubclassOf</c>), and the
    /// conditional <c>baseTypes</c> key emitted by <see cref="ExposedTypeInfoSerializer"/> for
    /// <c>/exposed/types</c>.
    /// </summary>
    [TestFixture]
    public class ExposedClassBaseTypesTests
    {
        // A user-defined hierarchy Leaf -> Mid -> Root -> object. None carry [ExposedClass] so each test
        // controls registration explicitly and exercises the plain-C#-name fallback for unregistered bases.
        public class RootStub { }
        public class MidStub : RootStub { }
        public class LeafStub : MidStub { }
        public class SiblingStub : RootStub { }

        // Base is a Unity engine type, so the chain must be empty (framework types are stripped).
        public class MonoStub : MonoBehaviour { }

        // Carries [ExposedClass] but is only registered at assembly load; SetUp clears it, so it stands in
        // for "an [ExposedClass] ancestor not currently registered" — the case where the old Find-based
        // lookup would have registered it as a side effect of reading baseTypeNames.
        [ExposedClass("AttributedMid")]
        public class AttributedMidStub : RootStub { }
        public class AttributedLeafStub : AttributedMidStub { }

        static readonly ExposedPropertyDefine[] NoProps = new ExposedPropertyDefine[0];

        [SetUp]
        public void Setup() => ExposedClass.Clear();

        [TearDown]
        public void TearDown() => ExposedClass.Clear();

        [Test]
        public void BaseTypeNames_WalksUnregisteredAncestors_AsCSharpNames_StoppingAtObject()
        {
            ExposedClass.Register<LeafStub>("LeafStub", NoProps);

            CollectionAssert.AreEqual(
                new[] { "MidStub", "RootStub" },
                ExposedClass.Get<LeafStub>().baseTypeNames);
        }

        [Test]
        public void BaseTypeNames_UsesExposedTypeName_ForRegisteredAncestor()
        {
            ExposedClass.Register<RootStub>("RootRenamed", NoProps);
            ExposedClass.Register<LeafStub>("LeafStub", NoProps);

            // MidStub is unregistered -> its C# name; RootStub is registered -> its exposed typeName.
            CollectionAssert.AreEqual(
                new[] { "MidStub", "RootRenamed" },
                ExposedClass.Get<LeafStub>().baseTypeNames);
        }

        [Test]
        public void BaseTypeNames_IsEmpty_WhenBaseIsFrameworkType()
        {
            ExposedClass.Register<MonoStub>("MonoStub", NoProps);

            Assert.IsEmpty(ExposedClass.Get<MonoStub>().baseTypeNames);
        }

        [Test]
        public void IsSubclassOf_IsStrict_AndMatchesAncestorsOnly()
        {
            ExposedClass.Register<LeafStub>("LeafStub", NoProps);
            var leaf = ExposedClass.Get<LeafStub>();

            Assert.IsTrue(leaf.IsSubclassOf("MidStub"));
            Assert.IsTrue(leaf.IsSubclassOf("RootStub"));
            Assert.IsFalse(leaf.IsSubclassOf("LeafStub"), "strict: a type is not a subclass of itself");
            Assert.IsFalse(leaf.IsSubclassOf("SiblingStub"));
            Assert.IsFalse(leaf.IsSubclassOf("object"), "framework ancestors are never grouping keys");
            Assert.IsFalse(leaf.IsSubclassOf(""));
            Assert.IsFalse(leaf.IsSubclassOf(null));
        }

        [Test]
        public void BaseTypeNames_DoesNotRegisterAncestors_AsSideEffect()
        {
            ExposedClass.Register<AttributedLeafStub>("AttributedLeafStub", NoProps);
            Assert.IsFalse(ExposedClass.all.ContainsKey(typeof(AttributedMidStub)),
                "precondition: the attributed ancestor is not registered after SetUp cleared it");

            // Reading the chain must not register the [ExposedClass] ancestor into `all` — a mutation there
            // would be a collection-modified hazard while HandleGetTypes enumerates `all.Values`.
            var _ = ExposedClass.Get<AttributedLeafStub>().baseTypeNames;

            Assert.IsFalse(ExposedClass.all.ContainsKey(typeof(AttributedMidStub)),
                "baseTypeNames must use a non-registering lookup");
        }

        [Test]
        public void Serializer_EmitsBaseTypes_WhenAncestorsExist_OmitsForFrameworkBase()
        {
            ExposedClass.Register<LeafStub>("LeafStub", NoProps);
            ExposedClass.Register<MonoStub>("MonoStub", NoProps);

            var leaf = JObject.Parse(ExposedTypeInfoSerializer.ToJson(ExposedClass.Get<LeafStub>()));
            CollectionAssert.AreEqual(
                new[] { "MidStub", "RootStub" },
                leaf["baseTypes"].ToObject<string[]>());

            var mono = JObject.Parse(ExposedTypeInfoSerializer.ToJson(ExposedClass.Get<MonoStub>()));
            Assert.IsNull(mono["baseTypes"], "a type with no user-defined ancestor must omit baseTypes");
        }
    }
}
