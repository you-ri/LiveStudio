// Copyright (c) You-Ri, 2026

using NUnit.Framework;

using UnityEngine;

using Newtonsoft.Json.Linq;

using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Pins the polymorphic-grouping contract that lets a client filter by base type without enumerating
    /// concrete <c>@type</c>s: <see cref="LiveClass.baseTypeNames"/> (the user-defined base-class chain),
    /// <see cref="LiveClass.IsSubclassOf(string)"/> (strict, mirroring <c>Type.IsSubclassOf</c>), and the
    /// conditional <c>baseTypes</c> key emitted by <see cref="LiveTypeInfoSerializer"/> for
    /// <c>/live/types</c>.
    /// </summary>
    [TestFixture]
    public class LiveClassBaseTypesTests
    {
        // A user-defined hierarchy Leaf -> Mid -> Root -> object. None carry [LiveClass] so each test
        // controls registration explicitly and exercises the plain-C#-name fallback for unregistered bases.
        public class RootStub { }
        public class MidStub : RootStub { }
        public class LeafStub : MidStub { }
        public class SiblingStub : RootStub { }

        // Base is a Unity engine type, so the chain must be empty (framework types are stripped).
        public class MonoStub : MonoBehaviour { }

        // Carries [LiveClass] but is only registered at assembly load; SetUp clears it, so it stands in
        // for "an [LiveClass] ancestor not currently registered" — the case where the old Find-based
        // lookup would have registered it as a side effect of reading baseTypeNames.
        [LiveClass("AttributedMid")]
        public class AttributedMidStub : RootStub { }
        public class AttributedLeafStub : AttributedMidStub { }

        static readonly LivePropertyDefine[] NoProps = new LivePropertyDefine[0];

        [SetUp]
        public void Setup() => LiveClass.Clear();

        [TearDown]
        public void TearDown() => LiveClass.Clear();

        [Test]
        public void BaseTypeNames_WalksUnregisteredAncestors_AsCSharpNames_StoppingAtObject()
        {
            LiveClass.Register<LeafStub>("LeafStub", NoProps);

            CollectionAssert.AreEqual(
                new[] { "MidStub", "RootStub" },
                LiveClass.Get<LeafStub>().baseTypeNames);
        }

        [Test]
        public void BaseTypeNames_UsesLiveTypeName_ForRegisteredAncestor()
        {
            LiveClass.Register<RootStub>("RootRenamed", NoProps);
            LiveClass.Register<LeafStub>("LeafStub", NoProps);

            // MidStub is unregistered -> its C# name; RootStub is registered -> its exposed typeName.
            CollectionAssert.AreEqual(
                new[] { "MidStub", "RootRenamed" },
                LiveClass.Get<LeafStub>().baseTypeNames);
        }

        [Test]
        public void BaseTypeNames_IsEmpty_WhenBaseIsFrameworkType()
        {
            LiveClass.Register<MonoStub>("MonoStub", NoProps);

            Assert.IsEmpty(LiveClass.Get<MonoStub>().baseTypeNames);
        }

        [Test]
        public void IsSubclassOf_IsStrict_AndMatchesAncestorsOnly()
        {
            LiveClass.Register<LeafStub>("LeafStub", NoProps);
            var leaf = LiveClass.Get<LeafStub>();

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
            LiveClass.Register<AttributedLeafStub>("AttributedLeafStub", NoProps);
            Assert.IsFalse(LiveClass.all.ContainsKey(typeof(AttributedMidStub)),
                "precondition: the attributed ancestor is not registered after SetUp cleared it");

            // Reading the chain must not register the [LiveClass] ancestor into `all` — a mutation there
            // would be a collection-modified hazard while HandleGetTypes enumerates `all.Values`.
            var _ = LiveClass.Get<AttributedLeafStub>().baseTypeNames;

            Assert.IsFalse(LiveClass.all.ContainsKey(typeof(AttributedMidStub)),
                "baseTypeNames must use a non-registering lookup");
        }

        [Test]
        public void Serializer_EmitsBaseTypes_WhenAncestorsExist_OmitsForFrameworkBase()
        {
            LiveClass.Register<LeafStub>("LeafStub", NoProps);
            LiveClass.Register<MonoStub>("MonoStub", NoProps);

            var leaf = JObject.Parse(LiveTypeInfoSerializer.ToJson(LiveClass.Get<LeafStub>()));
            CollectionAssert.AreEqual(
                new[] { "MidStub", "RootStub" },
                leaf["baseTypes"].ToObject<string[]>());

            var mono = JObject.Parse(LiveTypeInfoSerializer.ToJson(LiveClass.Get<MonoStub>()));
            Assert.IsNull(mono["baseTypes"], "a type with no user-defined ancestor must omit baseTypes");
        }
    }
}
