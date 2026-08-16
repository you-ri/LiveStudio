// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// 単数形 <c>GET /live/type/{name}</c> / <c>GET /live/enum/{name}</c> が返す定義は、
    /// 複数形 <c>/live/types</c> / <c>/live/enums</c> の配列要素と同じものである、という契約を固定する。
    ///
    /// クライアントは一覧で受けた定義と単数形で受けた定義を混在させて型テーブルを組む。
    /// ここがずれると、同じ型なのに取得経路によってプロパティの見え方が変わる。
    /// HTTP ハンドラを直接叩くテストが無いため、ハンドラが呼ぶシリアライザの層で検証する。
    /// </summary>
    [TestFixture]
    public class LiveTypeDefinitionSingleTests
    {
        private const string kTypeName = "TestSingleDefinitionType";
        private const string kEnumTypeName = "TestSingleDefinitionMode";

        public enum TestSingleDefinitionMode
        {
            First,
            Second,
        }

        [LiveClass(kTypeName, Category = "Test")]
        public class TestSingleDefinitionComponent : MonoBehaviour
        {
            [LiveField]
            public int value;

            [LiveField]
            public TestSingleDefinitionMode mode;

            [LiveField]
            public string label;
        }

        [SetUp]
        public void SetUp()
        {
            LiveClass.Clear();
            LiveClass.RegisterFromAttributes<TestSingleDefinitionComponent>();
            LiveEnum.Register<TestSingleDefinitionMode>(kEnumTypeName);
        }

        [Test]
        public void TypeDefinition_MatchesTheElementInTypesCollection()
        {
            var liveType = LiveClass.Find(kTypeName);
            Assert.IsNotNull(liveType, "Test type was not registered.");

            var single = JObject.Parse(LiveTypeInfoSerializer.ToJson(liveType));
            var collection = JObject.Parse(LiveTypeInfoSerializer.ToJson(new[] { liveType }));
            var element = (JObject)((JArray)collection["types"])[0];

            Assert.IsTrue(JToken.DeepEquals(element, single),
                $"Single definition diverged from the collection element.\nsingle: {single}\nelement: {element}");
        }

        [Test]
        public void EnumDefinition_MatchesTheElementInEnumsCollection()
        {
            LiveEnum liveEnum = null;
            foreach (var candidate in LiveEnum.all.Values)
            {
                if (candidate.typeName == kEnumTypeName)
                {
                    liveEnum = candidate;
                    break;
                }
            }
            Assert.IsNotNull(liveEnum, "Test enum was not registered.");

            var single = JObject.Parse(LiveTypeInfoSerializer.ToJson(liveEnum));
            var collection = JObject.Parse(LiveTypeInfoSerializer.ToJson(new[] { liveEnum }));
            var element = (JObject)((JArray)collection["enums"])[0];

            Assert.IsTrue(JToken.DeepEquals(element, single),
                $"Single definition diverged from the collection element.\nsingle: {single}\nelement: {element}");
        }

        [Test]
        public void UnknownTypeName_ResolvesToNull()
        {
            // 単数形ハンドラはここが null のとき 404 を返す (複数形は空集合を 200 で返す)。
            Assert.IsNull(LiveClass.Find("NoSuchLiveType"));
        }
    }
}
