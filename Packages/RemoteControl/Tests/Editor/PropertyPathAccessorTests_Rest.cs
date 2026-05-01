using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Reflection;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// PropertyPathParser tests using DotBracket format (internal format).
    /// Note: Slash format is only handled at REST API handler level via PropertyPath.FromSlash().
    /// </summary>
    [TestFixture]
    public class PropertyPathParserRestApiTests
    {
        #region Test Classes

        public class TestRootObject
        {
            public string name { get; set; }
            public int value { get; set; }
            public TestChild child { get; set; }
            public TestChild[] children { get; set; }
            public List<TestChild> childrenList { get; set; }
            public Vector3 position { get; set; }
        }

        public class TestChild
        {
            public string childName { get; set; }
            public TestGrandChild grandChild { get; set; }
            public Vector3 position { get; set; }
            public int[] numbers { get; set; }
        }

        public class TestGrandChild
        {
            public float data { get; set; }
            public string message { get; set; }
        }

        #endregion

        private TestRootObject _testObject;

        [SetUp]
        public void SetUp()
        {
            _testObject = new TestRootObject
            {
                name = "RootObject",
                value = 42,
                position = new Vector3(1f, 2f, 3f),
                child = new TestChild
                {
                    childName = "FirstChild",
                    position = new Vector3(4f, 5f, 6f),
                    grandChild = new TestGrandChild
                    {
                        data = 3.14f,
                        message = "Hello"
                    },
                    numbers = new int[] { 10, 20, 30 }
                },
                children = new TestChild[]
                {
                    new TestChild
                    {
                        childName = "Child0",
                        position = new Vector3(7f, 8f, 9f),
                        grandChild = new TestGrandChild { data = 1.0f, message = "Zero" }
                    },
                    new TestChild
                    {
                        childName = "Child1",
                        position = new Vector3(10f, 11f, 12f),
                        grandChild = new TestGrandChild { data = 2.0f, message = "One" }
                    },
                    new TestChild
                    {
                        childName = "Child2",
                        position = new Vector3(13f, 14f, 15f),
                        grandChild = new TestGrandChild { data = 3.0f, message = "Two" }
                    }
                },
                childrenList = new List<TestChild>
                {
                    new TestChild
                    {
                        childName = "ListChild0",
                        grandChild = new TestGrandChild { data = 10.0f, message = "List0" }
                    },
                    new TestChild
                    {
                        childName = "ListChild1",
                        grandChild = new TestGrandChild { data = 20.0f, message = "List1" }
                    }
                }
            };
        }

        #region DotBracket Format Tests (Internal Format)

        [Test]
        public void ParsePropertyPath_DotBracketFormat_Simple()
        {
            // DotBracket形式: "child.grandChild.data"
            var path = "child.grandChild.data";
            var expected = new[] { "child", "grandChild", "data" };
            int i = 0;
            foreach (var segment in PropertyPathParser.Parse(path))
            {
                Assert.IsFalse(segment.isError);
                Assert.IsFalse(segment.isIndexed);
                Assert.AreEqual(expected[i], segment.name.ToString());
                i++;
            }
            Assert.AreEqual(3, i);
        }

        [Test]
        public void ParsePropertyPath_DotBracketFormat_WithIndex()
        {
            // DotBracket形式: "children[0]"
            var path = "children[0]";
            int count = 0;
            foreach (var segment in PropertyPathParser.Parse(path))
            {
                Assert.IsFalse(segment.isError);
                if (count == 0)
                {
                    Assert.IsFalse(segment.isIndexed);
                    Assert.AreEqual("children", segment.name.ToString());
                }
                else
                {
                    Assert.IsTrue(segment.isIndexed);
                    Assert.AreEqual(0, segment.index);
                }
                count++;
            }
            Assert.AreEqual(2, count);
        }

        [Test]
        public void ParsePropertyPath_DotBracketFormat_ComplexPath()
        {
            // DotBracket形式: "children[1].grandChild.data"
            var path = "children[1].grandChild.data";
            int count = 0;
            foreach (var segment in PropertyPathParser.Parse(path))
            {
                Assert.IsFalse(segment.isError);
                switch (count)
                {
                    case 0:
                        Assert.IsFalse(segment.isIndexed);
                        Assert.AreEqual("children", segment.name.ToString());
                        break;
                    case 1:
                        Assert.IsTrue(segment.isIndexed);
                        Assert.AreEqual(1, segment.index);
                        break;
                    case 2:
                        Assert.IsFalse(segment.isIndexed);
                        Assert.AreEqual("grandChild", segment.name.ToString());
                        break;
                    case 3:
                        Assert.IsFalse(segment.isIndexed);
                        Assert.AreEqual("data", segment.name.ToString());
                        break;
                }
                count++;
            }
            Assert.AreEqual(4, count);
        }

        [Test]
        public void GetValue_DotBracketFormat_SimpleProperty()
        {
            // DotBracket形式でテスト
            var value = MemberAccessSystem.GetValue(_testObject, "child.childName");
            Assert.AreEqual("FirstChild", value);

            value = MemberAccessSystem.GetValue(_testObject, "child.grandChild.message");
            Assert.AreEqual("Hello", value);
        }

        [Test]
        public void GetValue_DotBracketFormat_ArrayAccess()
        {
            // DotBracket形式でテスト
            var value = MemberAccessSystem.GetValue(_testObject, "children[0].childName");
            Assert.AreEqual("Child0", value);

            value = MemberAccessSystem.GetValue(_testObject, "children[1].grandChild.data");
            Assert.AreEqual(2.0f, value);

            value = MemberAccessSystem.GetValue(_testObject, "children[2].grandChild.message");
            Assert.AreEqual("Two", value);
        }

        [Test]
        public void GetValue_DotBracketFormat_NestedArray()
        {
            // DotBracket形式でテスト
            var value = MemberAccessSystem.GetValue(_testObject, "child.numbers[0]");
            Assert.AreEqual(10, value);

            value = MemberAccessSystem.GetValue(_testObject, "child.numbers[2]");
            Assert.AreEqual(30, value);
        }

        [Test]
        public void SetValue_DotBracketFormat_SimpleProperty()
        {
            // DotBracket形式でテスト
            MemberAccessSystem.SetValue(_testObject, "child.childName", "DotBracketUpdated");
            Assert.AreEqual("DotBracketUpdated", _testObject.child.childName);
        }

        [Test]
        public void SetValue_DotBracketFormat_ArrayAccess()
        {
            // DotBracket形式でテスト
            MemberAccessSystem.SetValue(_testObject, "children[0].childName", "DotChild0");
            Assert.AreEqual("DotChild0", _testObject.children[0].childName);

            MemberAccessSystem.SetValue(_testObject, "children[1].grandChild.data", 123.45f);
            Assert.AreEqual(123.45f, _testObject.children[1].grandChild.data);
        }

        [Test]
        public void SetValue_DotBracketFormat_NestedArray()
        {
            // DotBracket形式でテスト
            MemberAccessSystem.SetValue(_testObject, "child.numbers[1]", 888);
            Assert.AreEqual(888, _testObject.child.numbers[1]);
        }

        #endregion

        #region Slash to DotBracket Conversion Tests

        [Test]
        public void PropertyPath_FromSlash_ThenGetValue_Works()
        {
            // Slash形式をDotBracket形式に変換してからGetValue
            var slashPath = "children/1/grandChild/data";
            var dotBracketPath = PropertyPath.FromSlash(slashPath);

            var value = MemberAccessSystem.GetValue(_testObject, dotBracketPath.Value);
            Assert.AreEqual(2.0f, value);
        }

        [Test]
        public void PropertyPath_FromSlash_ThenSetValue_Works()
        {
            // Slash形式をDotBracket形式に変換してからSetValue
            var slashPath = "children/0/childName";
            var dotBracketPath = PropertyPath.FromSlash(slashPath);

            MemberAccessSystem.SetValue(_testObject, dotBracketPath.Value, "ConvertedChild0");
            Assert.AreEqual("ConvertedChild0", _testObject.children[0].childName);
        }

        [Test]
        public void PropertyPath_SlashAndDotBracket_ProduceSameResult()
        {
            // DotBracket形式で直接取得
            var dotValue = MemberAccessSystem.GetValue(_testObject, "children[1].grandChild.data");

            // Slash形式を変換してから取得
            var slashPath = "children/1/grandChild/data";
            var convertedPath = PropertyPath.FromSlash(slashPath);
            var convertedValue = MemberAccessSystem.GetValue(_testObject, convertedPath.Value);

            Assert.AreEqual(dotValue, convertedValue);
            Assert.AreEqual(2.0f, dotValue);
        }

        #endregion
    }
}