using NUnit.Framework;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Reflection;

namespace Lilium.RemoteControl.Tests
{
    [TestFixture]
    public class PropertyPathTests
    {
        #region FromSlash Tests

        [Test]
        public void FromSlash_SimpleProperty_ConvertsCorrectly()
        {
            var path = PropertyPath.FromSlash("name");
            Assert.AreEqual("name", path.Value);
        }

        [Test]
        public void FromSlash_NestedProperty_ConvertsCorrectly()
        {
            var path = PropertyPath.FromSlash("settings/volume");
            Assert.AreEqual("settings.volume", path.Value);
        }

        [Test]
        public void FromSlash_ArrayElement_ConvertsCorrectly()
        {
            var path = PropertyPath.FromSlash("components/0");
            Assert.AreEqual("components[0]", path.Value);
        }

        [Test]
        public void FromSlash_ArrayElementProperty_ConvertsCorrectly()
        {
            var path = PropertyPath.FromSlash("components/0/value");
            Assert.AreEqual("components[0].value", path.Value);
        }

        [Test]
        public void FromSlash_DeepNestedPath_ConvertsCorrectly()
        {
            var path = PropertyPath.FromSlash("items/2/children/0/name");
            Assert.AreEqual("items[2].children[0].name", path.Value);
        }

        [Test]
        public void FromSlash_EmptyString_ReturnsEmpty()
        {
            var path = PropertyPath.FromSlash("");
            Assert.AreEqual("", path.Value);
            Assert.IsTrue(path.IsEmpty);
        }

        [Test]
        public void FromSlash_Null_ReturnsEmpty()
        {
            var path = PropertyPath.FromSlash(null);
            Assert.AreEqual("", path.Value);
            Assert.IsTrue(path.IsEmpty);
        }

        #endregion

        #region ToSlash Tests

        [Test]
        public void ToSlash_SimpleProperty_ConvertsCorrectly()
        {
            var path = new PropertyPath("name");
            Assert.AreEqual("name", path.ToSlash());
        }

        [Test]
        public void ToSlash_NestedProperty_ConvertsCorrectly()
        {
            var path = new PropertyPath("settings.volume");
            Assert.AreEqual("settings/volume", path.ToSlash());
        }

        [Test]
        public void ToSlash_ArrayElement_ConvertsCorrectly()
        {
            var path = new PropertyPath("components[0]");
            Assert.AreEqual("components/0", path.ToSlash());
        }

        [Test]
        public void ToSlash_ArrayElementProperty_ConvertsCorrectly()
        {
            var path = new PropertyPath("components[0].value");
            Assert.AreEqual("components/0/value", path.ToSlash());
        }

        [Test]
        public void ToSlash_DeepNestedPath_ConvertsCorrectly()
        {
            var path = new PropertyPath("items[2].children[0].name");
            Assert.AreEqual("items/2/children/0/name", path.ToSlash());
        }

        #endregion

        #region RoundTrip Tests

        [Test]
        public void RoundTrip_SlashToDotBracketToSlash_PreservesPath()
        {
            var original = "components/0/settings/1/name";
            var path = PropertyPath.FromSlash(original);
            var result = path.ToSlash();
            Assert.AreEqual(original, result);
        }

        [Test]
        public void RoundTrip_DotBracketToSlashToDotBracket_PreservesPath()
        {
            var original = "components[0].settings[1].name";
            var path = new PropertyPath(original);
            var slashPath = path.ToSlash();
            var result = PropertyPath.FromSlash(slashPath);
            Assert.AreEqual(original, result.Value);
        }

        #endregion

        #region ImplicitConversion Tests

        [Test]
        public void ImplicitConversion_StringToPropertyPath_Works()
        {
            PropertyPath path = "components[0].value";
            Assert.AreEqual("components[0].value", path.Value);
        }

        [Test]
        public void ImplicitConversion_PropertyPathToString_Works()
        {
            PropertyPath path = new PropertyPath("components[0].value");
            string str = path;
            Assert.AreEqual("components[0].value", str);
        }

        #endregion

        #region Append Tests

        [Test]
        public void Append_ToExistingPath_AddsSegment()
        {
            var path = new PropertyPath("components[0]");
            var newPath = path.Append("value");
            Assert.AreEqual("components[0].value", newPath.Value);
        }

        [Test]
        public void Append_ToEmptyPath_ReturnsSegment()
        {
            var path = PropertyPath.Empty;
            var newPath = path.Append("value");
            Assert.AreEqual("value", newPath.Value);
        }

        [Test]
        public void Append_EmptySegment_ReturnsSamePath()
        {
            var path = new PropertyPath("components[0]");
            var newPath = path.Append("");
            Assert.AreEqual("components[0]", newPath.Value);
        }

        #endregion

        #region AppendIndex Tests

        [Test]
        public void AppendIndex_ToExistingPath_AddsBracketIndex()
        {
            var path = new PropertyPath("components");
            var newPath = path.AppendIndex(0);
            Assert.AreEqual("components[0]", newPath.Value);
        }

        [Test]
        public void AppendIndex_ToEmptyPath_ReturnsBracketIndex()
        {
            var path = PropertyPath.Empty;
            var newPath = path.AppendIndex(5);
            Assert.AreEqual("[5]", newPath.Value);
        }

        [Test]
        public void AppendIndex_MultipleIndexes_ChainsCorrectly()
        {
            var path = new PropertyPath("matrix");
            var newPath = path.AppendIndex(0).AppendIndex(1);
            Assert.AreEqual("matrix[0][1]", newPath.Value);
        }

        #endregion

        #region GetRootSegment Tests

        [Test]
        public void GetRootSegment_SimpleProperty_ReturnsProperty()
        {
            var path = new PropertyPath("name");
            Assert.AreEqual("name", path.GetRootSegment());
        }

        [Test]
        public void GetRootSegment_NestedProperty_ReturnsRoot()
        {
            var path = new PropertyPath("settings.volume");
            Assert.AreEqual("settings", path.GetRootSegment());
        }

        [Test]
        public void GetRootSegment_ArrayProperty_ReturnsPropertyName()
        {
            var path = new PropertyPath("components[0]");
            Assert.AreEqual("components", path.GetRootSegment());
        }

        [Test]
        public void GetRootSegment_EmptyPath_ReturnsEmpty()
        {
            var path = PropertyPath.Empty;
            Assert.AreEqual("", path.GetRootSegment());
        }

        #endregion

        #region StartsWith Tests

        [Test]
        public void StartsWith_MatchingPrefix_ReturnsTrue()
        {
            var path = new PropertyPath("components[0].value");
            var prefix = new PropertyPath("components[0]");
            Assert.IsTrue(path.StartsWith(prefix));
        }

        [Test]
        public void StartsWith_NonMatchingPrefix_ReturnsFalse()
        {
            var path = new PropertyPath("components[0].value");
            var prefix = new PropertyPath("settings");
            Assert.IsFalse(path.StartsWith(prefix));
        }

        [Test]
        public void StartsWith_EmptyPrefix_ReturnsTrue()
        {
            var path = new PropertyPath("components[0].value");
            Assert.IsTrue(path.StartsWith(PropertyPath.Empty));
        }

        #endregion

        #region StartsWithAsChild Tests

        [Test]
        public void StartsWithAsChild_ValidChild_ReturnsTrue()
        {
            var path = new PropertyPath("components[0].value");
            var parent = new PropertyPath("components[0]");
            Assert.IsTrue(path.StartsWithAsChild(parent));
        }

        [Test]
        public void StartsWithAsChild_NotValidChild_ReturnsFalse()
        {
            var path = new PropertyPath("components[0]value");
            var parent = new PropertyPath("components[0]");
            Assert.IsFalse(path.StartsWithAsChild(parent));
        }

        [Test]
        public void StartsWithAsChild_SamePath_ReturnsTrue()
        {
            var path = new PropertyPath("components[0]");
            var parent = new PropertyPath("components[0]");
            Assert.IsTrue(path.StartsWithAsChild(parent));
        }

        #endregion

        #region Equality Tests

        [Test]
        public void Equality_SamePaths_AreEqual()
        {
            var path1 = new PropertyPath("components[0].value");
            var path2 = new PropertyPath("components[0].value");
            Assert.AreEqual(path1, path2);
            Assert.IsTrue(path1 == path2);
            Assert.IsFalse(path1 != path2);
        }

        [Test]
        public void Equality_DifferentPaths_AreNotEqual()
        {
            var path1 = new PropertyPath("components[0].value");
            var path2 = new PropertyPath("components[1].value");
            Assert.AreNotEqual(path1, path2);
            Assert.IsFalse(path1 == path2);
            Assert.IsTrue(path1 != path2);
        }

        #endregion
    }
}
