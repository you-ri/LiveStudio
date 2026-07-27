// Copyright (c) You-Ri, 2026

using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// [Countdown] / [UrlButton] のスキーマ出力テスト。
    /// どちらも controller として emit され、既定値のフィールドは省略される。
    /// </summary>
    [TestFixture]
    public class FunctionControlAttributeTests
    {
        #region Test Classes

        [ExposedClass("ControlStub")]
        public class ControlStub
        {
            [ExposedFunction]
            [Countdown(5, message = "GET_READY", runningMessage = "WORKING", icon = "person")]
            public void CountdownAction() { }

            /// 秒数・文言なし: seconds は省略され、クライアントの既定に委ねられる
            [ExposedFunction, Countdown]
            public void BareCountdownAction() { }

            [ExposedFunction]
            public void PlainAction() { }

            [ExposedFunction(icon = "restart_alt")]
            public void IconAction() { }

            [ExposedProperty, UrlButton]
            public string guideUrl => "https://example.com/guide";

            [ExposedProperty, UrlButton(icon = "help")]
            public string helpUrl => "https://example.com/help";

            [ExposedProperty]
            public string plainString => "not a button";
        }

        #endregion

        JObject _typeJson;

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();
            ExposedClass.RegisterFromAttributes<ControlStub>();
            _typeJson = JObject.Parse(ExposedTypeInfoSerializer.ToJson(ExposedClass.Get<ControlStub>()));
        }

        [TearDown]
        public void TearDown()
        {
            ExposedClass.Clear();
        }

        JObject _Member(string collection, string name)
        {
            var found = _typeJson[collection]?.FirstOrDefault(m => (string)m["name"] == name) as JObject;
            Assert.IsNotNull(found, $"{collection} entry '{name}' is missing from the type schema");
            return found;
        }

        [Test]
        public void Countdown_EmitsSecondsAndTranslatedMessages()
        {
            var controller = _Member("functions", "CountdownAction")["controller"];
            Assert.AreEqual("Countdown", (string)controller["type"]);
            Assert.AreEqual(5, (int)controller["seconds"]);
            Assert.AreEqual("person", (string)controller["icon"]);
            // 翻訳データ未登録なのでキーがそのままフォールバックされる (LocalizationSystem の既定挙動)
            Assert.AreEqual("GET_READY", (string)controller["message"]);
            Assert.AreEqual("WORKING", (string)controller["runningMessage"]);
        }

        [Test]
        public void Countdown_OmitsUnsetFields()
        {
            var controller = _Member("functions", "BareCountdownAction")["controller"];
            Assert.AreEqual("Countdown", (string)controller["type"]);
            Assert.IsNull(controller["seconds"], "seconds 0 means 'use the client default' and must be omitted");
            Assert.IsNull(controller["message"]);
            Assert.IsNull(controller["runningMessage"]);
            Assert.IsNull(controller["icon"]);
        }

        [Test]
        public void PlainFunction_KeepsDefaultController()
        {
            // 属性なしの関数は controller キー自体を持たない (既存出力と byte 一致)
            Assert.IsNull(_Member("functions", "PlainAction")["controller"]);
        }

        [Test]
        public void FunctionIcon_IsEmittedOnlyWhenDeclared()
        {
            Assert.AreEqual("restart_alt", (string)_Member("functions", "IconAction")["icon"]);
            Assert.IsNull(_Member("functions", "PlainAction")["icon"],
                "a function without an icon must not gain an icon key (existing output stays byte-identical)");
        }

        [Test]
        public void UrlButton_EmitsDefaultAndCustomIcon()
        {
            var guide = _Member("properties", "guideUrl")["controller"];
            Assert.AreEqual("UrlButton", (string)guide["type"]);
            Assert.AreEqual("open_in_new", (string)guide["icon"], "the default icon is emitted explicitly");

            var help = _Member("properties", "helpUrl")["controller"];
            Assert.AreEqual("help", (string)help["icon"]);
        }

        [Test]
        public void PlainProperty_IsNotAUrlButton()
        {
            var controller = _Member("properties", "plainString")["controller"];
            Assert.AreNotEqual("UrlButton", (string)controller["type"]);
        }
    }
}
