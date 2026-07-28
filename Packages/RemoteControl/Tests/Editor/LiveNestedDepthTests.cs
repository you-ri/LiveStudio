// Copyright (c) You-Ri, 2026
using System.Collections.Specialized;
using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Tests for the depth-limited serialization added for the shallow <c>/live/objects</c> REST read
    /// (the <c>nested</c> query flag). Absent (depth 1) emits nested inline composites as
    /// <c>@truncated</c> stubs so the payload stays small; <c>?nested</c> restores the legacy unbounded
    /// expansion. Registered children keep their <c>@ref</c> form, arrays never consume depth (element
    /// count / type stay visible), and persistence is forced unbounded so scene / project files stay
    /// byte-identical. FromJson must ignore the <c>@truncated</c> marker so a round-trip never wipes a
    /// child.
    /// </summary>
    [TestFixture]
    public class LiveNestedDepthTests
    {
        [LiveClass("DepthLeaf")]
        public class DepthLeaf
        {
            [LiveField] public int id;
            [LiveField] public string label;
        }

        [LiveClass("DepthChild")]
        public class DepthChild
        {
            [LiveField] public int childValue;
            [LiveField] public DepthLeaf leaf;
        }

        [LiveClass("DepthRoot")]
        public class DepthRoot
        {
            [LiveField] public int rootValue;
            [LiveField] public string rootName;
            [LiveField] public DepthChild child;
            [LiveField] public DepthChild[] children;
            [LiveField] public int[] scalars;
        }

        private TestLiveObjectResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            LiveClass.Clear();
            ClearRegistry();
            _resolver = new TestLiveObjectResolver();
            LiveClass.RegisterFromAttributes<DepthLeaf>();
            LiveClass.RegisterFromAttributes<DepthChild>();
            LiveClass.RegisterFromAttributes<DepthRoot>();
        }

        [TearDown]
        public void TearDown() => ClearRegistry();

        private static void ClearRegistry()
        {
            foreach (var obj in LiveObjectRegistry.instances.ToList())
            {
                obj.Unregister();
            }
        }

        private DepthRoot MakeRootTarget()
        {
            return new DepthRoot
            {
                rootValue = 7,
                rootName = "root",
                child = new DepthChild
                {
                    childValue = 11,
                    leaf = new DepthLeaf { id = 99, label = "inner" },
                },
                children = new[]
                {
                    new DepthChild { childValue = 1, leaf = new DepthLeaf { id = 1, label = "a" } },
                    new DepthChild { childValue = 2, leaf = new DepthLeaf { id = 2, label = "b" } },
                },
                scalars = new[] { 10, 20, 30 },
            };
        }

        private LiveObjectHandle MakeRootHandle(string id = "root-1")
        {
            return new LiveObjectHandle(id, LiveClass.Find(typeof(DepthRoot)), MakeRootTarget());
        }

        // ---- depth-1 (shallow) ----

        [Test]
        public void ShallowDepth_StubsInlineChild_KeepsScalars()
        {
            var json = LivePropertySerializer.ToJson(MakeRootHandle(), _resolver, maxDepth: 1);
            var obj = JObject.Parse(json);

            // Top scalar properties are always present.
            Assert.AreEqual(7, obj["rootValue"].Value<int>());
            Assert.AreEqual("root", obj["rootName"].Value<string>());

            // The inline composite child is stubbed, not expanded.
            var child = (JObject)obj["child"];
            Assert.AreEqual("DepthChild", child["@type"].Value<string>());
            Assert.IsTrue(child["@truncated"].Value<bool>(), "Inline child must carry @truncated at depth 1.");
            Assert.IsNull(child["childValue"], "A truncated stub must not expand its properties.");
            Assert.IsNull(child["leaf"], "A truncated stub must not expand its nested composites.");
        }

        [Test]
        public void ShallowDepth_ArrayElementsStubbed_CountPreserved()
        {
            var json = LivePropertySerializer.ToJson(MakeRootHandle(), _resolver, maxDepth: 1);
            var obj = JObject.Parse(json);

            // Arrays do not consume depth: the element count and per-element type stay visible.
            var children = (JArray)obj["children"];
            Assert.AreEqual(2, children.Count, "Array element count must survive truncation.");
            foreach (var element in children.Cast<JObject>())
            {
                Assert.AreEqual("DepthChild", element["@type"].Value<string>());
                Assert.IsTrue(element["@truncated"].Value<bool>());
                Assert.IsNull(element["childValue"]);
            }
        }

        [Test]
        public void ShallowDepth_ScalarArray_ValuesPresent()
        {
            var json = LivePropertySerializer.ToJson(MakeRootHandle(), _resolver, maxDepth: 1);
            var obj = JObject.Parse(json);

            // Scalar arrays are pure leaves — never truncated.
            var scalars = ((JArray)obj["scalars"]).Select(t => t.Value<int>()).ToArray();
            CollectionAssert.AreEqual(new[] { 10, 20, 30 }, scalars);
        }

        // ---- unbounded (default / ?nested) ----

        [Test]
        public void UnboundedDepth_ExpandsFully_NoTruncationMarker()
        {
            // Default maxDepth is int.MaxValue (unbounded); equivalent to ?nested.
            var json = LivePropertySerializer.ToJson(MakeRootHandle(), _resolver);
            var obj = JObject.Parse(json);

            var child = (JObject)obj["child"];
            Assert.IsNull(child["@truncated"], "Unbounded expansion must not emit @truncated.");
            Assert.AreEqual(11, child["childValue"].Value<int>());

            var leaf = (JObject)child["leaf"];
            Assert.AreEqual(99, leaf["id"].Value<int>());
            Assert.AreEqual("inner", leaf["label"].Value<string>());

            Assert.IsFalse(json.Contains("@truncated"), "No @truncated anywhere under unbounded depth.");
        }

        [Test]
        public void ExplicitMaxValue_ByteIdenticalToDefault()
        {
            var a = LivePropertySerializer.ToJson(MakeRootHandle("same"), _resolver);
            var b = LivePropertySerializer.ToJson(MakeRootHandle("same"), _resolver, maxDepth: int.MaxValue);
            Assert.AreEqual(a, b);
        }

        // ---- level counting ----

        [Test]
        public void Depth2_ExpandsOneLevel_StubsNext()
        {
            var json = LivePropertySerializer.ToJson(MakeRootHandle(), _resolver, maxDepth: 2);
            var obj = JObject.Parse(json);

            // child is expanded (one level below the root)...
            var child = (JObject)obj["child"];
            Assert.IsNull(child["@truncated"]);
            Assert.AreEqual(11, child["childValue"].Value<int>());

            // ...but its own nested composite is stubbed at the next level.
            var leaf = (JObject)child["leaf"];
            Assert.AreEqual("DepthLeaf", leaf["@type"].Value<string>());
            Assert.IsTrue(leaf["@truncated"].Value<bool>());
            Assert.IsNull(leaf["id"], "Depth-2 must not expand the third level.");
        }

        // ---- registered child keeps @ref regardless of depth ----

        [Test]
        public void RegisteredChild_EmitsRef_UnaffectedByDepth()
        {
            // A registered child resolves to a handle -> @ref, both shallow and unbounded.
            var childTarget = new DepthChild { childValue = 42, leaf = new DepthLeaf { id = 5, label = "reg" } };
            var childHandle = new LiveObjectHandle("child-1", LiveClass.Find(typeof(DepthChild)), childTarget);

            var rootTarget = MakeRootTarget();
            rootTarget.child = childTarget;
            var rootHandle = new LiveObjectHandle("root-ref", LiveClass.Find(typeof(DepthRoot)), rootTarget);

            var shallow = JObject.Parse(LivePropertySerializer.ToJson(rootHandle, _resolver, maxDepth: 1));
            var deep = JObject.Parse(LivePropertySerializer.ToJson(rootHandle, _resolver));

            foreach (var obj in new[] { shallow, deep })
            {
                var child = (JObject)obj["child"];
                Assert.AreEqual("child-1", child["@ref"].Value<string>(), "Registered child must serialize as @ref.");
                Assert.IsNull(child["@truncated"], "A registered @ref child is not a truncation stub.");
                Assert.IsNull(child["childValue"], "@ref must not inline the referenced value.");
            }

            // childHandle is retained by LiveObjectRegistry for the resolver to find.
            Assert.IsNotNull(childHandle.id);
        }

        // ---- persistence forced unbounded ----

        [Test]
        public void Persistence_IgnoresMaxDepth_FullyExpands()
        {
            // Defensive guard: forPersistence must never truncate, even if a finite depth is passed,
            // so scene / project files stay byte-stable.
            var json = LivePropertySerializer.ToJson(
                MakeRootHandle(), _resolver, isDirtyOnly: false, forPersistence: true,
                scopeFilter: PersistScope.Scene, maxDepth: 1);

            Assert.IsFalse(json.Contains("@truncated"), "Persistence must ignore maxDepth and fully expand.");
            var obj = JObject.Parse(json);
            Assert.AreEqual(11, ((JObject)obj["child"])["childValue"].Value<int>());
        }

        // ---- FromJson ignores @truncated ----

        [Test]
        public void FromJson_IgnoresTruncatedMarker_DoesNotWipeChild()
        {
            var shallow = LivePropertySerializer.ToJson(MakeRootHandle(), _resolver, maxDepth: 1);

            // A fresh target whose child already holds data the stub must not clobber.
            var freshTarget = new DepthRoot
            {
                child = new DepthChild { childValue = 555, leaf = new DepthLeaf { id = 7, label = "keep" } },
            };
            var freshHandle = new LiveObjectHandle(
                "fresh", LiveClass.Find(typeof(DepthRoot)), freshTarget);

            Assert.DoesNotThrow(() => LivePropertySerializer.FromJson(shallow, freshHandle, _resolver));

            // Scalars from the shallow payload applied; the truncated child kept its existing value.
            Assert.AreEqual(7, freshTarget.rootValue);
            Assert.AreEqual(555, freshTarget.child.childValue,
                "@truncated stub must not overwrite an existing child value.");
        }

        // ---- query flag parsing ----

        private static NameValueCollection Query(string key, string value)
        {
            var q = new NameValueCollection();
            q.Add(key, value);
            return q;
        }

        [Test]
        public void ResolveNestedMaxDepth_Absent_IsShallow()
        {
            Assert.AreEqual(1, LiveObjectHandler.ResolveNestedMaxDepth(new NameValueCollection()));
            Assert.AreEqual(1, LiveObjectHandler.ResolveNestedMaxDepth(null));
        }

        [Test]
        public void ResolveNestedMaxDepth_TruthyValues_AreUnbounded()
        {
            Assert.AreEqual(int.MaxValue, LiveObjectHandler.ResolveNestedMaxDepth(Query("nested", "true")));
            Assert.AreEqual(int.MaxValue, LiveObjectHandler.ResolveNestedMaxDepth(Query("nested", "1")));
            Assert.AreEqual(int.MaxValue, LiveObjectHandler.ResolveNestedMaxDepth(Query("nested", "")));
            // Bare `?nested` (no '=') lands under the null key in .NET's parser.
            Assert.AreEqual(int.MaxValue, LiveObjectHandler.ResolveNestedMaxDepth(Query(null, "nested")));
        }

        [Test]
        public void ResolveNestedMaxDepth_FalseyValue_IsShallow()
        {
            Assert.AreEqual(1, LiveObjectHandler.ResolveNestedMaxDepth(Query("nested", "false")));
            Assert.AreEqual(1, LiveObjectHandler.ResolveNestedMaxDepth(Query("nested", "0")));
        }
    }
}
