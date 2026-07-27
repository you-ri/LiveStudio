// Copyright (c) You-Ri, 2026

using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// [Layout] のスキーマ出力テスト。
    /// - Auto はグループ深さから horizontal/vertical に解決される
    /// - 明示指定は深さより優先される
    /// - columns / grow は既定値のときは出力しない
    /// - 関数 ([ExposedFunction]) にも同じスキーマで出る
    /// - 属性を付けていないメンバーには layout キー自体が出ない (既存出力の byte 不変)
    /// </summary>
    [TestFixture]
    public class LayoutAttributeTests
    {
        #region Test Classes

        [ExposedClass("LayoutStub")]
        public class LayoutStub
        {
            /// 深さ1: Auto → horizontal
            [ExposedProperty, Layout("row")]
            public int depth1 { get; set; }

            /// 深さ2: Auto → vertical
            [ExposedProperty, Layout("row/left")]
            public int depth2 { get; set; }

            /// 深さ3: Auto → horizontal
            [ExposedProperty, Layout("row/left/inner")]
            public int depth3 { get; set; }

            /// 明示指定は深さより優先
            [ExposedProperty, Layout("row/right", direction = LayoutDirection.Horizontal)]
            public int explicitDirection { get; set; }

            /// columns / grow は既定値以外のときだけ出す
            [ExposedProperty, Layout("row/right/fps", columns = 2, grow = 0)]
            public int tuned { get; set; }

            /// 前後・重複スラッシュは正規化される
            [ExposedProperty, Layout("/row//left/")]
            public int messyPath { get; set; }

            /// [Layout] 無し → layout キーを出さない
            [ExposedProperty]
            public int plain { get; set; }

            /// 関数にも同じスキーマで出る (ボタンの横並び用)
            [ExposedFunction, Layout("row/actions")]
            public void DoSomething() { }

            [ExposedFunction]
            public void PlainAction() { }

            /// 関数がセクションを開始できる (ボタンだけのセクション)
            [ExposedFunction]
            [Section("bolt", "ACTIONS"), Experimental]
            public void SectionStartingAction() { }
        }

        #endregion

        JObject _typeJson;

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();
            ExposedClass.RegisterFromAttributes<LayoutStub>();
            _typeJson = JObject.Parse(ExposedTypeInfoSerializer.ToJson(ExposedClass.Get<LayoutStub>()));
        }

        [TearDown]
        public void TearDown()
        {
            ExposedClass.Clear();
        }

        JObject _Property(string name)
        {
            var found = _typeJson["properties"]?.FirstOrDefault(p => (string)p["name"] == name) as JObject;
            Assert.IsNotNull(found, $"property '{name}' is missing from the type schema");
            return found;
        }

        JObject _Function(string name)
        {
            var found = _typeJson["functions"]?.FirstOrDefault(f => (string)f["name"] == name) as JObject;
            Assert.IsNotNull(found, $"function '{name}' is missing from the type schema");
            return found;
        }

        [Test]
        public void AutoDirection_AlternatesWithDepth()
        {
            Assert.AreEqual("horizontal", (string)_Property("depth1")["layout"]["direction"]);
            Assert.AreEqual("vertical", (string)_Property("depth2")["layout"]["direction"]);
            Assert.AreEqual("horizontal", (string)_Property("depth3")["layout"]["direction"]);
        }

        [Test]
        public void ExplicitDirection_OverridesDepth()
        {
            // 深さ2なので Auto なら vertical になるところを、明示指定で horizontal にする
            Assert.AreEqual("horizontal", (string)_Property("explicitDirection")["layout"]["direction"]);
        }

        [Test]
        public void Path_IsEmittedAndNormalized()
        {
            Assert.AreEqual("row/left", (string)_Property("depth2")["layout"]["path"]);
            Assert.AreEqual("row/left", (string)_Property("messyPath")["layout"]["path"],
                "leading/trailing/duplicate separators must be normalized away");
            Assert.AreEqual("vertical", (string)_Property("messyPath")["layout"]["direction"],
                "normalization must also drive the depth used for Auto resolution");
        }

        [Test]
        public void ColumnsAndGrow_AreOmittedAtDefaults()
        {
            var plainGroup = _Property("depth1")["layout"];
            Assert.IsNull(plainGroup["columns"], "columns must be omitted when 0");
            Assert.IsNull(plainGroup["grow"], "grow must be omitted when 1 (the default)");

            var tuned = _Property("tuned")["layout"];
            Assert.AreEqual(2, (int)tuned["columns"]);
            Assert.AreEqual(0, (int)tuned["grow"]);
        }

        [Test]
        public void Functions_CarryTheSameSchema()
        {
            var layout = _Function("DoSomething")["layout"];
            Assert.IsNotNull(layout, "[Layout] on an [ExposedFunction] must be emitted");
            Assert.AreEqual("row/actions", (string)layout["path"]);
            Assert.AreEqual("vertical", (string)layout["direction"]);
        }

        [Test]
        public void MembersWithoutTheAttribute_OmitTheKeyEntirely()
        {
            Assert.IsNull(_Property("plain")["layout"],
                "a property without [Layout] must not gain a layout key (existing output stays byte-identical)");
            Assert.IsNull(_Function("PlainAction")["layout"],
                "a function without [Layout] must not gain a layout key");
            Assert.IsNull(_Function("PlainAction")["section"],
                "a function without [Section] must not gain a section key");
        }

        [Test]
        public void Functions_CanStartASection()
        {
            var section = _Function("SectionStartingAction")["section"];
            Assert.IsNotNull(section, "[Section] on a function must be emitted so a buttons-only section works");
            Assert.AreEqual("bolt", (string)section["icon"]);
            Assert.AreEqual("ACTIONS", (string)section["title"]);
            // [Experimental] は Section.accessLevel より優先される (プロパティ側と同じ規則)
            Assert.AreEqual((int)AccessLevel.Experimental, (int)section["accessLevel"]);
        }
    }
}
