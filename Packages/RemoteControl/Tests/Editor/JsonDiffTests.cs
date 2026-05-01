// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.Tests
{
    public class JsonDiffTests
    {
        [Test]
        public void JsonDiff_IdenticalPrimitives_ReturnsNull()
        {
            var a = new JValue(42);
            var b = new JValue(42);
            Assert.IsNull(ExposedPropertySerializer.JsonDiff(a, b));
        }

        [Test]
        public void JsonDiff_DifferentPrimitives_ReturnsCurrent()
        {
            var a = new JValue(1);
            var b = new JValue(2);
            var diff = ExposedPropertySerializer.JsonDiff(a, b);
            Assert.IsNotNull(diff);
            Assert.AreEqual(2, diff.Value<int>());
        }

        [Test]
        public void JsonDiff_IdenticalStrings_ReturnsNull()
        {
            var a = new JValue("hello");
            var b = new JValue("hello");
            Assert.IsNull(ExposedPropertySerializer.JsonDiff(a, b));
        }

        [Test]
        public void JsonDiff_DifferentStrings_ReturnsCurrent()
        {
            var a = new JValue("hello");
            var b = new JValue("world");
            var diff = ExposedPropertySerializer.JsonDiff(a, b);
            Assert.IsNotNull(diff);
            Assert.AreEqual("world", diff.Value<string>());
        }

        [Test]
        public void JsonDiff_IdenticalObjects_ReturnsNull()
        {
            var a = JObject.Parse(@"{""x"":1, ""y"":2}");
            var b = JObject.Parse(@"{""x"":1, ""y"":2}");
            Assert.IsNull(ExposedPropertySerializer.JsonDiff(a, b));
        }

        [Test]
        public void JsonDiff_ObjectWithChangedProperty_ReturnsOnlyChanged()
        {
            var a = JObject.Parse(@"{""x"":1, ""y"":2}");
            var b = JObject.Parse(@"{""x"":1, ""y"":5}");
            var diff = ExposedPropertySerializer.JsonDiff(a, b) as JObject;
            Assert.IsNotNull(diff);
            Assert.IsNull(diff["x"], "Unchanged property should not appear");
            Assert.AreEqual(5, diff["y"].Value<int>());
        }

        [Test]
        public void JsonDiff_ObjectWithAddedProperty_ReturnsAdded()
        {
            var a = JObject.Parse(@"{""x"":1}");
            var b = JObject.Parse(@"{""x"":1, ""y"":2}");
            var diff = ExposedPropertySerializer.JsonDiff(a, b) as JObject;
            Assert.IsNotNull(diff);
            Assert.AreEqual(2, diff["y"].Value<int>());
        }

        [Test]
        public void JsonDiff_ObjectMetadataPreserved()
        {
            var a = JObject.Parse(@"{""@type"":""Foo"", ""x"":1}");
            var b = JObject.Parse(@"{""@type"":""Foo"", ""x"":2}");
            var diff = ExposedPropertySerializer.JsonDiff(a, b) as JObject;
            Assert.IsNotNull(diff);
            Assert.AreEqual("Foo", diff["@type"].Value<string>(), "@type metadata should be preserved");
            Assert.AreEqual(2, diff["x"].Value<int>());
        }

        [Test]
        public void JsonDiff_ObjectOnlyMetadataDiffers_ReturnsNull()
        {
            // メタデータのみが異なる場合、非メタプロパティに差分がないのでnull
            var a = JObject.Parse(@"{""@type"":""Foo"", ""x"":1}");
            var b = JObject.Parse(@"{""@type"":""Bar"", ""x"":1}");
            Assert.IsNull(ExposedPropertySerializer.JsonDiff(a, b));
        }

        [Test]
        public void JsonDiff_IdenticalPrimitiveArrays_ReturnsNull()
        {
            var a = JArray.Parse("[1, 2, 3]");
            var b = JArray.Parse("[1, 2, 3]");
            Assert.IsNull(ExposedPropertySerializer.JsonDiff(a, b));
        }

        [Test]
        public void JsonDiff_DifferentPrimitiveArrays_ReturnsCurrentArray()
        {
            var a = JArray.Parse("[1, 2, 3]");
            var b = JArray.Parse("[1, 2, 4]");
            var diff = ExposedPropertySerializer.JsonDiff(a, b) as JArray;
            Assert.IsNotNull(diff);
            Assert.AreEqual(3, diff.Count);
            Assert.AreEqual(4, diff[2].Value<int>());
        }

        [Test]
        public void JsonDiff_ObjectArray_UnchangedElement_EmptyStub()
        {
            var a = JArray.Parse(@"[{""id"":1, ""name"":""A""}, {""id"":2, ""name"":""B""}]");
            var b = JArray.Parse(@"[{""id"":1, ""name"":""A""}, {""id"":2, ""name"":""Changed""}]");
            var diff = ExposedPropertySerializer.JsonDiff(a, b) as JArray;
            Assert.IsNotNull(diff);
            Assert.AreEqual(2, diff.Count);

            // 最初の要素は未変更 → 空stub
            var first = diff[0] as JObject;
            Assert.IsNotNull(first);
            Assert.AreEqual(0, first.Count);

            // 2番目は変更あり
            var second = diff[1] as JObject;
            Assert.IsNotNull(second);
            Assert.AreEqual("Changed", second["name"].Value<string>());
            Assert.IsNull(second["id"], "Unchanged property should not appear in diff");
        }

        [Test]
        public void JsonDiff_ObjectArray_TrailingUnchanged_Omitted()
        {
            var a = JArray.Parse(@"[{""id"":1, ""name"":""A""}, {""id"":2, ""name"":""B""}, {""id"":3, ""name"":""C""}]");
            var b = JArray.Parse(@"[{""id"":1, ""name"":""Modified""}, {""id"":2, ""name"":""B""}, {""id"":3, ""name"":""C""}]");
            var diff = ExposedPropertySerializer.JsonDiff(a, b) as JArray;
            Assert.IsNotNull(diff);
            // 末尾の未変更要素は省略されるが、デルタ形式の識別のため空マーカーが1つ追加される
            Assert.AreEqual(2, diff.Count, "Should have changed element + one empty delta marker");
            Assert.IsTrue(ExposedPropertySerializer.IsArrayDeltaFormat(diff), "Result should be detectable as delta format");
        }

        [Test]
        public void JsonDiff_ObjectArray_NewElements_HaveOpNew()
        {
            var a = JArray.Parse(@"[{""id"":1, ""name"":""First""}]");
            var b = JArray.Parse(@"[{""id"":1, ""name"":""First""}, {""id"":10, ""name"":""New""}]");
            var diff = ExposedPropertySerializer.JsonDiff(a, b) as JArray;
            Assert.IsNotNull(diff);

            // 新規要素に@op: "new"がある
            bool hasNew = false;
            foreach (var elem in diff)
            {
                if (elem is JObject o && o["@op"]?.ToString() == "new")
                {
                    hasNew = true;
                    Assert.AreEqual(10, o["id"].Value<int>());
                    Assert.AreEqual("New", o["name"].Value<string>());
                }
            }
            Assert.IsTrue(hasNew, "New element should have @op: new");
        }

        [Test]
        public void JsonDiff_ObjectArray_AppendOnly_OmitsUnchangedStubs()
        {
            // 既存要素が未変更で末尾に新規要素が1つ追加されるケース。
            // 既存要素の位置マーカー（空{}）は不要なので出力に含めない。
            var a = JArray.Parse(@"[{""id"":1, ""name"":""A""}, {""id"":2, ""name"":""B""}]");
            var b = JArray.Parse(@"[{""id"":1, ""name"":""A""}, {""id"":2, ""name"":""B""}, {""id"":3, ""name"":""C""}]");
            var diff = ExposedPropertySerializer.JsonDiff(a, b) as JArray;

            Assert.IsNotNull(diff);
            Assert.AreEqual(1, diff.Count, "Only the appended element should remain — leading empty stubs should be omitted");
            var newElem = diff[0] as JObject;
            Assert.IsNotNull(newElem);
            Assert.AreEqual("new", newElem["@op"]?.Value<string>());
            Assert.AreEqual(3, newElem["id"].Value<int>());
            Assert.AreEqual("C", newElem["name"].Value<string>());
        }

        [Test]
        public void JsonDiff_ObjectArray_AppendOnly_ForPersistence_OmitsUnchangedStubs()
        {
            // forPersistence=true でも append-only ケースでは leading empty stubs を省略する。
            var a = JArray.Parse(@"[{""id"":1, ""name"":""A""}, {""id"":2, ""name"":""B""}]");
            var b = JArray.Parse(@"[{""id"":1, ""name"":""A""}, {""id"":2, ""name"":""B""}, {""id"":3, ""name"":""C""}]");
            var diff = ExposedPropertySerializer.JsonDiff(a, b, forPersistence: true) as JArray;

            Assert.IsNotNull(diff);
            Assert.AreEqual(1, diff.Count, "forPersistence append-only should not include empty stubs");
            var newElem = diff[0] as JObject;
            Assert.IsNotNull(newElem);
            Assert.AreEqual("new", newElem["@op"]?.Value<string>());
        }

        [Test]
        public void JsonDiff_ObjectArray_ModifyAndAppend_KeepsLeadingStubsUpToLastModified()
        {
            // 中間要素を変更 + 末尾に新規追加: 変更位置の位置マーカーは必要、
            // その後の未変更要素の位置マーカーは不要。
            var a = JArray.Parse(@"[{""id"":1, ""name"":""A""}, {""id"":2, ""name"":""B""}, {""id"":3, ""name"":""C""}]");
            var b = JArray.Parse(@"[{""id"":1, ""name"":""A""}, {""id"":2, ""name"":""Modified""}, {""id"":3, ""name"":""C""}, {""id"":4, ""name"":""D""}]");
            var diff = ExposedPropertySerializer.JsonDiff(a, b, forPersistence: true) as JArray;

            Assert.IsNotNull(diff);
            // 期待出力: [{}, {name:"Modified"}, {@op:new, id:4, name:"D"}]
            Assert.AreEqual(3, diff.Count, "Expected [leading empty stub, modified diff, new element] — no trailing empty stub");

            Assert.IsTrue(diff[0] is JObject o0 && o0.Count == 0, "Index 0 should be empty stub");
            Assert.IsTrue(diff[1] is JObject o1 && o1["name"]?.Value<string>() == "Modified", "Index 1 should be modified diff");
            Assert.IsTrue(diff[2] is JObject o2 && o2["@op"]?.Value<string>() == "new" && o2["id"]?.Value<int>() == 4, "Index 2 should be new element");
        }

        [Test]
        public void JsonDiff_ObjectArray_AppendOnly_RoundTrip()
        {
            // 省略版のデルタからロードしても結果が正しく復元できることを確認。
            var a = JArray.Parse(@"[{""id"":1, ""name"":""A""}, {""id"":2, ""name"":""B""}]");
            var b = JArray.Parse(@"[{""id"":1, ""name"":""A""}, {""id"":2, ""name"":""B""}, {""id"":3, ""name"":""C""}]");
            var diff = ExposedPropertySerializer.JsonDiff(a, b, forPersistence: true) as JArray;

            Assert.IsNotNull(diff);
            Assert.IsTrue(ExposedPropertySerializer.IsArrayDeltaFormat(diff), "Result should be detectable as delta format");
        }

        [Test]
        public void JsonDiff_NestedObjectDiff_ReturnsNestedChangesOnly()
        {
            var a = JObject.Parse(@"{""nested"":{""x"":1, ""y"":2}, ""other"":""same""}");
            var b = JObject.Parse(@"{""nested"":{""x"":1, ""y"":5}, ""other"":""same""}");
            var diff = ExposedPropertySerializer.JsonDiff(a, b) as JObject;
            Assert.IsNotNull(diff);
            Assert.IsNull(diff["other"], "Unchanged property should not appear");
            var nestedDiff = diff["nested"] as JObject;
            Assert.IsNotNull(nestedDiff);
            Assert.AreEqual(5, nestedDiff["y"].Value<int>());
            Assert.IsNull(nestedDiff["x"], "Unchanged nested property should not appear");
        }

        [Test]
        public void JsonDiff_NullDefault_ReturnsCurrentClone()
        {
            var b = new JValue(42);
            var diff = ExposedPropertySerializer.JsonDiff(null, b);
            Assert.IsNotNull(diff);
            Assert.AreEqual(42, diff.Value<int>());
        }

        [Test]
        public void JsonDiff_NullCurrent_ReturnsJNull()
        {
            var a = new JValue(42);
            var diff = ExposedPropertySerializer.JsonDiff(a, null);
            Assert.IsNotNull(diff);
            Assert.AreEqual(JTokenType.Null, diff.Type);
        }

        [Test]
        public void JsonDiff_BothNull_ReturnsNull()
        {
            Assert.IsNull(ExposedPropertySerializer.JsonDiff(null, null));
        }
    }
}
