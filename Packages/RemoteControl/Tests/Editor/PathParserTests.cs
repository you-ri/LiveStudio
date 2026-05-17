using NUnit.Framework;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Utility;

namespace Lilium.RemoteControl.Tests
{
    [TestFixture]
    public class PathParserTests
    {
        #region IsMatch Tests

        [Test]
        public void IsMatch_ExactMatch_ReturnsTrue()
        {
            Assert.IsTrue(PathParser.IsMatch("hello", "hello"));
            Assert.IsTrue(PathParser.IsMatch("world", "world"));
            Assert.IsTrue(PathParser.IsMatch("", ""));
        }

        [Test]
        public void IsMatch_NoMatch_ReturnsFalse()
        {
            Assert.IsFalse(PathParser.IsMatch("hello", "world"));
            Assert.IsFalse(PathParser.IsMatch("hello", "Hello"));
            Assert.IsFalse(PathParser.IsMatch("test", "testing"));
        }

        [Test]
        public void IsMatch_WildcardAsterisk_MatchesAnyCharacters()
        {
            // * at the beginning
            Assert.IsTrue(PathParser.IsMatch("hello", "*llo"));
            Assert.IsTrue(PathParser.IsMatch("world", "*rld"));
            Assert.IsTrue(PathParser.IsMatch("test", "*"));

            // * at the end
            Assert.IsTrue(PathParser.IsMatch("hello", "hel*"));
            Assert.IsTrue(PathParser.IsMatch("world", "w*"));
            Assert.IsTrue(PathParser.IsMatch("", "*"));

            // * in the middle
            Assert.IsTrue(PathParser.IsMatch("hello", "h*o"));
            Assert.IsTrue(PathParser.IsMatch("hello", "h*lo"));
            Assert.IsTrue(PathParser.IsMatch("testing", "test*ng"));

            // Multiple *
            Assert.IsTrue(PathParser.IsMatch("hello", "*e*o"));
            Assert.IsTrue(PathParser.IsMatch("hello", "h*l*"));
            Assert.IsTrue(PathParser.IsMatch("hello", "*"));
        }

        [Test]
        public void IsMatch_WildcardQuestion_MatchesSingleCharacter()
        {
            Assert.IsTrue(PathParser.IsMatch("hello", "h?llo"));
            Assert.IsTrue(PathParser.IsMatch("world", "w?rld"));
            Assert.IsTrue(PathParser.IsMatch("test", "t?st"));

            // Multiple ?
            Assert.IsTrue(PathParser.IsMatch("hello", "h??lo"));
            Assert.IsTrue(PathParser.IsMatch("hello", "?????"));

            // ? doesn't match empty
            Assert.IsFalse(PathParser.IsMatch("hello", "h?lo"));
            Assert.IsFalse(PathParser.IsMatch("test", "t?t"));
        }

        [Test]
        public void IsMatch_CombinedWildcards_WorksCorrectly()
        {
            Assert.IsTrue(PathParser.IsMatch("hello", "h*?"));
            Assert.IsTrue(PathParser.IsMatch("hello", "?*"));
            Assert.IsTrue(PathParser.IsMatch("hello", "*?"));
            Assert.IsTrue(PathParser.IsMatch("hello", "h?*o"));
            Assert.IsTrue(PathParser.IsMatch("testing", "t*?ng"));
        }

        [Test]
        public void IsMatch_RealUrlPatterns_WorksCorrectly()
        {
            // ExposedObjectHandler実際のパターンテスト
            Assert.IsTrue(PathParser.IsMatch("/exposed/object/123/property/name", "/exposed/object/*/property/*"));
            Assert.IsTrue(PathParser.IsMatch("/exposed/object/camera1/property/position", "/exposed/object/*/property/*"));
            Assert.IsTrue(PathParser.IsMatch("/exposed/object/light1/property/intensity/reset", "/exposed/object/*/property/*/reset"));

            Assert.IsFalse(PathParser.IsMatch("/exposed/objects", "/exposed/object/*/property/*"));
            Assert.IsFalse(PathParser.IsMatch("/exposed/object/123", "/exposed/object/*/property/*"));
        }

        [Test]
        public void IsMatch_NullOrEmptyInputs_ReturnsFalse()
        {
            Assert.IsFalse(PathParser.IsMatch(null, "pattern"));
            Assert.IsFalse(PathParser.IsMatch("input", null));
            Assert.IsFalse(PathParser.IsMatch(null, null));
        }

        [Test]
        public void IsMatch_EmptyStrings_HandledCorrectly()
        {
            Assert.IsTrue(PathParser.IsMatch("", ""));
            Assert.IsTrue(PathParser.IsMatch("", "*"));
            Assert.IsTrue(PathParser.IsMatch("", "**"));
            Assert.IsFalse(PathParser.IsMatch("", "?"));
            Assert.IsFalse(PathParser.IsMatch("", "a"));
            Assert.IsFalse(PathParser.IsMatch("test", ""));
        }

        #endregion

        #region IsMatchIgnoreCase Tests

        [Test]
        public void IsMatchIgnoreCase_CaseInsensitive_ReturnsTrue()
        {
            Assert.IsTrue(PathParser.IsMatchIgnoreCase("Hello", "hello"));
            Assert.IsTrue(PathParser.IsMatchIgnoreCase("WORLD", "world"));
            Assert.IsTrue(PathParser.IsMatchIgnoreCase("TeSt", "test"));
            Assert.IsTrue(PathParser.IsMatchIgnoreCase("MiXeD", "mixed"));
        }

        [Test]
        public void IsMatchIgnoreCase_WithWildcards_WorksCorrectly()
        {
            Assert.IsTrue(PathParser.IsMatchIgnoreCase("Hello", "H*o"));
            Assert.IsTrue(PathParser.IsMatchIgnoreCase("WORLD", "w*d"));
            Assert.IsTrue(PathParser.IsMatchIgnoreCase("TeSt", "t?st"));
            Assert.IsTrue(PathParser.IsMatchIgnoreCase("MiXeD", "m?x?d"));
        }

        [Test]
        public void IsMatchIgnoreCase_RealUrlPatterns_WorksCorrectly()
        {
            Assert.IsTrue(PathParser.IsMatchIgnoreCase("/EXPOSED/OBJECT/123/PROPERTY/NAME", "/exposed/object/*/property/*"));
            Assert.IsTrue(PathParser.IsMatchIgnoreCase("/Exposed/Object/Camera1/Property/Position", "/exposed/object/*/property/*"));
        }

        [Test]
        public void IsMatchIgnoreCase_NullInputs_ReturnsFalse()
        {
            Assert.IsFalse(PathParser.IsMatchIgnoreCase(null, "pattern"));
            Assert.IsFalse(PathParser.IsMatchIgnoreCase("input", null));
            Assert.IsFalse(PathParser.IsMatchIgnoreCase(null, null));
        }

        #endregion

        #region GetPathSegment Tests

        [Test]
        public void GetPathSegment_ValidPath_ReturnsCorrectSegment()
        {
            string path = "/exposed/object/123/property/name";

            Assert.AreEqual("exposed", PathParser.GetPathSegment(path, 0));
            Assert.AreEqual("object", PathParser.GetPathSegment(path, 1));
            Assert.AreEqual("123", PathParser.GetPathSegment(path, 2));
            Assert.AreEqual("property", PathParser.GetPathSegment(path, 3));
            Assert.AreEqual("name", PathParser.GetPathSegment(path, 4));
        }

        [Test]
        public void GetPathSegment_PathWithoutLeadingSlash_ReturnsCorrectSegment()
        {
            string path = "exposed/object/123";

            Assert.AreEqual("exposed", PathParser.GetPathSegment(path, 0));
            Assert.AreEqual("object", PathParser.GetPathSegment(path, 1));
            Assert.AreEqual("123", PathParser.GetPathSegment(path, 2));
        }

        [Test]
        public void GetPathSegment_PathWithTrailingSlash_ReturnsCorrectSegment()
        {
            string path = "/exposed/object/123/";

            Assert.AreEqual("exposed", PathParser.GetPathSegment(path, 0));
            Assert.AreEqual("object", PathParser.GetPathSegment(path, 1));
            Assert.AreEqual("123", PathParser.GetPathSegment(path, 2));
        }

        [Test]
        public void GetPathSegment_IndexOutOfRange_ReturnsNull()
        {
            string path = "/exposed/object/123";

            Assert.IsNull(PathParser.GetPathSegment(path, 5));
            Assert.IsNull(PathParser.GetPathSegment(path, 10));
            Assert.IsNull(PathParser.GetPathSegment(path, 100));
        }

        [Test]
        public void GetPathSegment_NegativeIndex_ReturnsNull()
        {
            string path = "/exposed/object/123";

            Assert.IsNull(PathParser.GetPathSegment(path, -1));
            Assert.IsNull(PathParser.GetPathSegment(path, -5));
        }

        [Test]
        public void GetPathSegment_NullOrEmptyPath_ReturnsNull()
        {
            Assert.IsNull(PathParser.GetPathSegment(null, 0));
            Assert.IsNull(PathParser.GetPathSegment("", 0));
        }

        [Test]
        public void GetPathSegment_SingleSegment_ReturnsCorrectly()
        {
            Assert.AreEqual("test", PathParser.GetPathSegment("/test", 0));
            Assert.AreEqual("test", PathParser.GetPathSegment("test", 0));
            Assert.AreEqual("test", PathParser.GetPathSegment("/test/", 0));
        }

        [Test]
        public void GetPathSegment_RealApiPaths_ReturnsCorrectSegments()
        {
            // ExposedObjectHandlerで実際に使用されるパス
            string objectPath = "/exposed/object/camera1";
            Assert.AreEqual("exposed", PathParser.GetPathSegment(objectPath, 0));
            Assert.AreEqual("object", PathParser.GetPathSegment(objectPath, 1));
            Assert.AreEqual("camera1", PathParser.GetPathSegment(objectPath, 2));

            string propertyPath = "/exposed/object/light1/property/intensity";
            Assert.AreEqual("exposed", PathParser.GetPathSegment(propertyPath, 0));
            Assert.AreEqual("object", PathParser.GetPathSegment(propertyPath, 1));
            Assert.AreEqual("light1", PathParser.GetPathSegment(propertyPath, 2));
            Assert.AreEqual("property", PathParser.GetPathSegment(propertyPath, 3));
            Assert.AreEqual("intensity", PathParser.GetPathSegment(propertyPath, 4));
        }

        #endregion

        #region GetPathSegmentFrom Tests

        [Test]
        public void GetPathSegmentFrom_ValidPath_ReturnsCorrectSegments()
        {
            string path = "/exposed/object/123/property/name";

            Assert.AreEqual("exposed/object/123/property/name", PathParser.GetPathSegmentFrom(path, 0));
            Assert.AreEqual("object/123/property/name", PathParser.GetPathSegmentFrom(path, 1));
            Assert.AreEqual("123/property/name", PathParser.GetPathSegmentFrom(path, 2));
            Assert.AreEqual("property/name", PathParser.GetPathSegmentFrom(path, 3));
            Assert.AreEqual("name", PathParser.GetPathSegmentFrom(path, 4));
        }

        [Test]
        public void GetPathSegmentFrom_CustomSeparator_ReturnsCorrectSegments()
        {
            string path = "/exposed/object/123/property/name";

            Assert.AreEqual("exposed.object.123.property.name", PathParser.GetPathSegmentFrom(path, 0, "."));
            Assert.AreEqual("object-123-property-name", PathParser.GetPathSegmentFrom(path, 1, "-"));
            Assert.AreEqual("123|property|name", PathParser.GetPathSegmentFrom(path, 2, "|"));
        }

        [Test]
        public void GetPathSegmentFrom_IndexOutOfRange_ReturnsNull()
        {
            string path = "/exposed/object/123";

            Assert.IsNull(PathParser.GetPathSegmentFrom(path, 5));
            Assert.IsNull(PathParser.GetPathSegmentFrom(path, 10));
        }

        [Test]
        public void GetPathSegmentFrom_NegativeIndex_ReturnsNull()
        {
            string path = "/exposed/object/123";

            Assert.IsNull(PathParser.GetPathSegmentFrom(path, -1));
            Assert.IsNull(PathParser.GetPathSegmentFrom(path, -5));
        }

        [Test]
        public void GetPathSegmentFrom_NullOrEmptyPath_ReturnsNull()
        {
            Assert.IsNull(PathParser.GetPathSegmentFrom(null, 0));
            Assert.IsNull(PathParser.GetPathSegmentFrom("", 0));
        }

        [Test]
        public void GetPathSegmentFrom_LastIndex_ReturnsSingleSegment()
        {
            string path = "/exposed/object/123/property/name";

            Assert.AreEqual("name", PathParser.GetPathSegmentFrom(path, 4));
            Assert.AreEqual("name", PathParser.GetPathSegmentFrom(path, 4, "."));
        }

        [Test]
        public void GetPathSegmentFrom_SingleSegment_ReturnsCorrectly()
        {
            Assert.AreEqual("test", PathParser.GetPathSegmentFrom("/test", 0));
            Assert.AreEqual("test", PathParser.GetPathSegmentFrom("test", 0));
        }

        #endregion
    }
}