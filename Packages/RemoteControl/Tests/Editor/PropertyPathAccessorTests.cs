using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl.Reflection;

namespace Lilium.RemoteControl.Tests
{
    [TestFixture]
    public class PropertyPathParserTests
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
            public readonly int readOnlyValue = 100;

            // フィールドのテスト用
            public string nameField;
            public TestChild childField;
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
                nameField = "RootField",
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
                childField = new TestChild
                {
                    childName = "FieldChild",
                    grandChild = new TestGrandChild
                    {
                        data = 2.71f,
                        message = "Field"
                    }
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

        #region ParsePropertyPath Tests

        [Test]
        public void ParsePropertyPath_SimpleProperty_ReturnsSingleSegment()
        {
            var path = "name";
            int count = 0;
            foreach (var segment in PropertyPathParser.Parse(path))
            {
                Assert.IsFalse(segment.isError);
                Assert.IsFalse(segment.isIndexed);
                Assert.AreEqual("name", segment.name.ToString());
                count++;
            }
            Assert.AreEqual(1, count);
        }

        [Test]
        public void ParsePropertyPath_NestedProperty_ReturnsMultipleSegments()
        {
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
        public void ParsePropertyPath_ArrayAccess_ReturnsIndexedSegment()
        {
            var path = "children[1]";
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
                    Assert.AreEqual(1, segment.index);
                }
                count++;
            }
            Assert.AreEqual(2, count);
        }

        [Test]
        public void ParsePropertyPath_ComplexPath_ReturnsCorrectSegments()
        {
            var path = "children[0].grandChild.data";
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
                        Assert.AreEqual(0, segment.index);
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
        public void ParsePropertyPath_EmptyPath_ReturnsEmptyArray()
        {
            int count = 0;
            foreach (var segment in PropertyPathParser.Parse(""))
            {
                count++;
            }
            Assert.AreEqual(0, count);

            count = 0;
            foreach (var segment in PropertyPathParser.Parse(null))
            {
                count++;
            }
            Assert.AreEqual(0, count);
        }

        #endregion

        #region GetValue Tests

        [Test]
        public void GetValue_SimpleProperty_ReturnsCorrectValue()
        {
            var value = MemberAccessSystem.GetValue(_testObject, "name");
            Assert.AreEqual("RootObject", value);

            value = MemberAccessSystem.GetValue(_testObject, "value");
            Assert.AreEqual(42, value);
        }

        [Test]
        public void GetValue_SimpleField_ReturnsCorrectValue()
        {
            var value = MemberAccessSystem.GetValue(_testObject, "nameField");
            Assert.AreEqual("RootField", value);
        }

        [Test]
        public void GetValue_NestedProperty_ReturnsCorrectValue()
        {
            var value = MemberAccessSystem.GetValue(_testObject, "child.childName");
            Assert.AreEqual("FirstChild", value);

            value = MemberAccessSystem.GetValue(_testObject, "child.grandChild.data");
            Assert.AreEqual(3.14f, value);

            value = MemberAccessSystem.GetValue(_testObject, "child.grandChild.message");
            Assert.AreEqual("Hello", value);
        }

        [Test]
        public void GetValue_NestedField_ReturnsCorrectValue()
        {
            var value = MemberAccessSystem.GetValue(_testObject, "childField.childName");
            Assert.AreEqual("FieldChild", value);

            value = MemberAccessSystem.GetValue(_testObject, "childField.grandChild.data");
            Assert.AreEqual(2.71f, value);
        }

        [Test]
        public void GetValue_ArrayElement_ReturnsCorrectValue()
        {
            var value = MemberAccessSystem.GetValue(_testObject, "children[0]");
            Assert.IsNotNull(value);
            Assert.AreEqual("Child0", ((TestChild)value).childName);

            value = MemberAccessSystem.GetValue(_testObject, "children[1]");
            Assert.IsNotNull(value);
            Assert.AreEqual("Child1", ((TestChild)value).childName);
        }

        [Test]
        public void GetValue_ArrayElementProperty_ReturnsCorrectValue()
        {
            var value = MemberAccessSystem.GetValue(_testObject, "children[0].childName");
            Assert.AreEqual("Child0", value);

            value = MemberAccessSystem.GetValue(_testObject, "children[1].childName");
            Assert.AreEqual("Child1", value);

            value = MemberAccessSystem.GetValue(_testObject, "children[2].childName");
            Assert.AreEqual("Child2", value);
        }

        [Test]
        public void GetValue_ArrayElementNestedProperty_ReturnsCorrectValue()
        {
            var value = MemberAccessSystem.GetValue(_testObject, "children[0].grandChild.data");
            Assert.AreEqual(1.0f, value);

            value = MemberAccessSystem.GetValue(_testObject, "children[1].grandChild.message");
            Assert.AreEqual("One", value);

            value = MemberAccessSystem.GetValue(_testObject, "children[2].grandChild.data");
            Assert.AreEqual(3.0f, value);
        }

        [Test]
        public void GetValue_ListElement_ReturnsCorrectValue()
        {
            var value = MemberAccessSystem.GetValue(_testObject, "childrenList[0].childName");
            Assert.AreEqual("ListChild0", value);

            value = MemberAccessSystem.GetValue(_testObject, "childrenList[1].grandChild.data");
            Assert.AreEqual(20.0f, value);
        }

        [Test]
        public void GetValue_NestedArray_ReturnsCorrectValue()
        {
            var value = MemberAccessSystem.GetValue(_testObject, "child.numbers[0]");
            Assert.AreEqual(10, value);

            value = MemberAccessSystem.GetValue(_testObject, "child.numbers[2]");
            Assert.AreEqual(30, value);
        }

        [Test]
        public void GetValue_NonExistentProperty_ReturnsNull()
        {
            var value = MemberAccessSystem.GetValue(_testObject, "nonExistent");
            Assert.IsNull(value);

            value = MemberAccessSystem.GetValue(_testObject, "child.nonExistent");
            Assert.IsNull(value);
        }

        [Test]
        public void GetValue_InvalidIndex_ReturnsNull()
        {
            var value = MemberAccessSystem.GetValue(_testObject, "children[10]");
            Assert.IsNull(value);

            value = MemberAccessSystem.GetValue(_testObject, "children[-1]");
            Assert.IsNull(value);

            value = MemberAccessSystem.GetValue(_testObject, "children[10].childName");
            Assert.IsNull(value);
        }

        [Test]
        public void GetValue_NullObject_ReturnsNull()
        {
            var value = MemberAccessSystem.GetValue(null, "name");
            Assert.IsNull(value);
        }

        [Test]
        public void GetValue_NullIntermediate_ReturnsNull()
        {
            _testObject.child = null;
            var value = MemberAccessSystem.GetValue(_testObject, "child.childName");
            Assert.IsNull(value);
        }

        #endregion

        #region SetValue Tests

        [Test]
        public void SetValue_SimpleProperty_UpdatesValue()
        {
            MemberAccessSystem.SetValue(_testObject, "name", "NewName");
            Assert.AreEqual("NewName", _testObject.name);

            MemberAccessSystem.SetValue(_testObject, "value", 100);
            Assert.AreEqual(100, _testObject.value);
        }

        [Test]
        public void SetValue_SimpleField_UpdatesValue()
        {
            MemberAccessSystem.SetValue(_testObject, "nameField", "NewFieldValue");
            Assert.AreEqual("NewFieldValue", _testObject.nameField);
        }

        [Test]
        public void SetValue_NestedProperty_UpdatesValue()
        {
            MemberAccessSystem.SetValue(_testObject, "child.childName", "UpdatedChild");
            Assert.AreEqual("UpdatedChild", _testObject.child.childName);

            MemberAccessSystem.SetValue(_testObject, "child.grandChild.data", 6.28f);
            Assert.AreEqual(6.28f, _testObject.child.grandChild.data);
        }

        [Test]
        public void SetValue_ArrayElement_UpdatesValue()
        {
            MemberAccessSystem.SetValue(_testObject, "children[0].childName", "UpdatedChild0");
            Assert.AreEqual("UpdatedChild0", _testObject.children[0].childName);

            MemberAccessSystem.SetValue(_testObject, "children[1].grandChild.data", 99.9f);
            Assert.AreEqual(99.9f, _testObject.children[1].grandChild.data);
        }

        [Test]
        public void SetValue_ListElement_UpdatesValue()
        {
            MemberAccessSystem.SetValue(_testObject, "childrenList[0].childName", "UpdatedListChild");
            Assert.AreEqual("UpdatedListChild", _testObject.childrenList[0].childName);

            MemberAccessSystem.SetValue(_testObject, "childrenList[1].grandChild.message", "UpdatedMessage");
            Assert.AreEqual("UpdatedMessage", _testObject.childrenList[1].grandChild.message);
        }

        [Test]
        public void SetValue_NestedArray_UpdatesValue()
        {
            MemberAccessSystem.SetValue(_testObject, "child.numbers[1]", 999);
            Assert.AreEqual(999, _testObject.child.numbers[1]);
        }

        [Test]
        public void SetValue_InvalidIndex_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                MemberAccessSystem.SetValue(_testObject, "children[10].childName", "Invalid"));

            Assert.DoesNotThrow(() =>
                MemberAccessSystem.SetValue(_testObject, "children[-1].childName", "Invalid"));
        }

        [Test]
        public void SetValue_NonExistentProperty_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                MemberAccessSystem.SetValue(_testObject, "nonExistent", "Value"));

            Assert.DoesNotThrow(() =>
                MemberAccessSystem.SetValue(_testObject, "child.nonExistent", "Value"));
        }

        #endregion

        #region GetPropertyInfo Tests

        [Test]
        public void GetPropertyInfo_SimpleProperty_ReturnsPropertyInfo()
        {
            var propInfo = MemberAccessSystem.GetMemberInfo(_testObject, "name");
            Assert.IsNotNull(propInfo);
            Assert.AreEqual("name", propInfo.Name);
        }

        [Test]
        public void GetPropertyInfo_NestedProperty_ReturnsPropertyInfo()
        {
            var propInfo = MemberAccessSystem.GetMemberInfo(_testObject, "child.childName");
            Assert.IsNotNull(propInfo);
            Assert.AreEqual("childName", propInfo.Name);
        }


        [Test]
        public void GetPropertyInfo_ArrayElement_ReturnsNull()
        {
            // 配列要素自体はPropertyInfoを持たない
            var propInfo = MemberAccessSystem.GetMemberInfo(_testObject, "children[0]");
            Assert.IsNull(propInfo);
        }

        [Test]
        public void GetPropertyInfo_ArrayElementProperty_ReturnsPropertyInfo()
        {
            var propInfo = MemberAccessSystem.GetMemberInfo(_testObject, "children[0].childName");
            Assert.IsNotNull(propInfo);
            Assert.AreEqual("childName", propInfo.Name);
        }

        #endregion
    }
}