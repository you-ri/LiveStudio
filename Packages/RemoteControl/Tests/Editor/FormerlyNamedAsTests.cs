// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// [FormerlyNamedAs] によるクラス/フィールド/プロパティのリネーム互換のテスト。
    /// typeName やメンバー名を変更しても、旧名で書かれた JSON から復元できることを保証する。
    /// </summary>
    [TestFixture]
    public class FormerlyLiveAsTests
    {
        #region Test Classes

        [Serializable]
        [LiveClass("NewPlug")]
        [FormerlyNamedAs("OldPlug")]
        [FormerlyNamedAs("AncientPlug")]
        public class NewPlug
        {
            [LiveField]
            [FormerlyNamedAs("oldValue")]
            public int newValue;

            [LiveField]
            [FormerlyNamedAs("oldLabel")]
            public string newLabel;
        }

        [Serializable]
        [LiveClass("NoAliasClass")]
        public class NoAliasClass
        {
            [LiveField]
            public int value;
        }

        #endregion

        private TestLiveObjectResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            LiveClass.Clear();

            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            _resolver = new TestLiveObjectResolver();
        }

        [TearDown]
        public void TearDown()
        {
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();
        }

        #region Class alias

        [Test]
        public void Find_ByCurrentTypeName_ReturnsClass()
        {
            LiveClass.RegisterFromAttributes<NewPlug>();

            var ec = LiveClass.Find("NewPlug");
            Assert.IsNotNull(ec);
            Assert.AreEqual(typeof(NewPlug), ec.type);
        }

        [Test]
        public void Find_ByFormerTypeName_ReturnsSameClass()
        {
            LiveClass.RegisterFromAttributes<NewPlug>();

            var byCurrent = LiveClass.Find("NewPlug");
            var byOld = LiveClass.Find("OldPlug");
            var byAncient = LiveClass.Find("AncientPlug");

            Assert.IsNotNull(byOld);
            Assert.IsNotNull(byAncient);
            Assert.AreSame(byCurrent, byOld);
            Assert.AreSame(byCurrent, byAncient);
        }

        [Test]
        public void Find_UnknownTypeName_ReturnsNull()
        {
            LiveClass.RegisterFromAttributes<NewPlug>();
            Assert.IsNull(LiveClass.Find("NoSuchClass"));
        }

        [Test]
        public void LiveClass_formerTypeNames_ContainsAllAliases()
        {
            LiveClass.RegisterFromAttributes<NewPlug>();
            var ec = LiveClass.Find(typeof(NewPlug));
            CollectionAssert.AreEquivalent(new[] { "OldPlug", "AncientPlug" }, ec.formerTypeNames);
        }

        [Test]
        public void Unregister_RemovesAliases()
        {
            LiveClass.RegisterFromAttributes<NewPlug>();
            var ec = LiveClass.Find(typeof(NewPlug));
            LiveClass.Unregister(ec);

            Assert.IsNull(LiveClass.Find("NewPlug"));
            Assert.IsNull(LiveClass.Find("OldPlug"));
            Assert.IsNull(LiveClass.Find("AncientPlug"));
        }

        #endregion

        #region Field / Property alias

        [Test]
        public void FindProperty_ByCurrentName_ReturnsProperty()
        {
            LiveClass.RegisterFromAttributes<NewPlug>();
            var ec = LiveClass.Find(typeof(NewPlug));

            Assert.IsNotNull(ec.FindProperty("newValue"));
            Assert.IsNotNull(ec.FindProperty("newLabel"));
        }

        [Test]
        public void FindProperty_ByFormerFieldName_ReturnsRenamedProperty()
        {
            LiveClass.RegisterFromAttributes<NewPlug>();
            var ec = LiveClass.Find(typeof(NewPlug));

            var current = ec.FindProperty("newValue");
            var viaOld = ec.FindProperty("oldValue");
            Assert.IsNotNull(viaOld);
            Assert.AreSame(current, viaOld);
        }

        [Test]
        public void FindProperty_ByFormerPropertyName_ReturnsRenamedProperty()
        {
            LiveClass.RegisterFromAttributes<NewPlug>();
            var ec = LiveClass.Find(typeof(NewPlug));

            var current = ec.FindProperty("newLabel");
            var viaOld = ec.FindProperty("oldLabel");
            Assert.IsNotNull(viaOld);
            Assert.AreSame(current, viaOld);
        }

        [Test]
        public void PropertyType_formerNames_PopulatedFromAttribute()
        {
            LiveClass.RegisterFromAttributes<NewPlug>();
            var ec = LiveClass.Find(typeof(NewPlug));

            var valueProp = ec.FindProperty("newValue");
            CollectionAssert.AreEquivalent(new[] { "oldValue" }, valueProp.formerNames);
        }

        [Test]
        public void PropertyType_formerNames_EmptyForUnrenamed()
        {
            LiveClass.RegisterFromAttributes<NoAliasClass>();
            var ec = LiveClass.Find(typeof(NoAliasClass));
            var prop = ec.FindProperty("value");
            Assert.IsNotNull(prop);
            Assert.AreEqual(0, prop.formerNames.Length);
        }

        #endregion

        #region Scene load round-trip

        [Test]
        public void LiveSceneFromJson_LoadsLegacyTypeName()
        {
            // 旧 typeName "OldPlug" で書かれた JSON が NewPlug クラスに復元される
            LiveClass.RegisterFromAttributes<NewPlug>();
            var ec = LiveClass.Find(typeof(NewPlug));
            var target = new NewPlug();
            new LiveObjectHandle("plug-1", ec, target);

            var legacyJson = @"{
              ""format"": ""jp.lilium.remotecontrol.scene"",
              ""formatVersion"": 1,
              ""objects"": [
                { ""@type"": ""OldPlug"", ""@id"": ""plug-1"", ""newValue"": 42, ""newLabel"": ""ok"" }
              ]
            }";

            LiveSceneSerializer.LiveSceneFromJson(legacyJson, _resolver);

            Assert.AreEqual(42, target.newValue);
            Assert.AreEqual("ok", target.newLabel);
        }

        [Test]
        public void LiveSceneFromJson_LoadsLegacyFieldName()
        {
            // 旧 field 名 "oldValue" で書かれた JSON が newValue に復元される
            LiveClass.RegisterFromAttributes<NewPlug>();
            var ec = LiveClass.Find(typeof(NewPlug));
            var target = new NewPlug();
            new LiveObjectHandle("plug-2", ec, target);

            var legacyJson = @"{
              ""format"": ""jp.lilium.remotecontrol.scene"",
              ""formatVersion"": 1,
              ""objects"": [
                { ""@type"": ""NewPlug"", ""@id"": ""plug-2"", ""oldValue"": 77, ""oldLabel"": ""legacy"" }
              ]
            }";

            LiveSceneSerializer.LiveSceneFromJson(legacyJson, _resolver);

            Assert.AreEqual(77, target.newValue);
            Assert.AreEqual("legacy", target.newLabel);
        }

        [Test]
        public void LiveSceneFromJson_LoadsLegacyTypeAndFieldNames()
        {
            // typeName とフィールド名の両方が旧名のケース
            LiveClass.RegisterFromAttributes<NewPlug>();
            var ec = LiveClass.Find(typeof(NewPlug));
            var target = new NewPlug();
            new LiveObjectHandle("plug-3", ec, target);

            var legacyJson = @"{
              ""format"": ""jp.lilium.remotecontrol.scene"",
              ""formatVersion"": 1,
              ""objects"": [
                { ""@type"": ""AncientPlug"", ""@id"": ""plug-3"", ""oldValue"": 9, ""oldLabel"": ""ancient"" }
              ]
            }";

            LiveSceneSerializer.LiveSceneFromJson(legacyJson, _resolver);

            Assert.AreEqual(9, target.newValue);
            Assert.AreEqual("ancient", target.newLabel);
        }

        [Test]
        public void LiveSceneToJson_EmitsCurrentNamesOnly()
        {
            // 書き出しは常に最新の typeName / field 名で行う（互換属性は読み取り専用の役割）
            LiveClass.RegisterFromAttributes<NewPlug>();
            var ec = LiveClass.Find(typeof(NewPlug));
            new LiveObjectHandle("plug-4", ec, new NewPlug { newValue = 1, newLabel = "hi" });

            var json = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);
            var entry = (JObject)((JArray)JObject.Parse(json)["objects"])[0];

            Assert.AreEqual("NewPlug", entry["@type"]?.Value<string>(),
                "Must always emit the current typeName, never a former alias");
            Assert.IsNotNull(entry["newValue"]);
            Assert.IsNotNull(entry["newLabel"]);
            Assert.IsNull(entry["oldValue"], "Former field names must not appear in output");
            Assert.IsNull(entry["oldLabel"], "Former property names must not appear in output");
        }

        #endregion
    }
}
