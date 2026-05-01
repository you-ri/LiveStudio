// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// ExposedClass.onPropertyChanged / onPropertyChanging イベントの発火を検証するテスト。
    /// RemoteApp経由でプロパティを変更した際にイベントが発火しない不具合の回帰テスト。
    /// </summary>
    [TestFixture]
    public class ExposedClassEventsTests
    {
        #region Test Classes

        [Serializable]
        [ExposedClass("TestEventsNestedStruct")]
        public struct TestNestedStruct
        {
            [ExposedField]
            public int value;
        }

        [Serializable]
        [ExposedClass("TestEventsElement")]
        public class TestElement
        {
            [ExposedField]
            public bool flag;

            [ExposedField]
            public string label;
        }

        [Serializable]
        [ExposedClass("TestEventsTarget")]
        public class TestTarget
        {
            [ExposedField]
            public int intValue;

            [ExposedField]
            public string stringValue;

            [ExposedField]
            public TestNestedStruct nested;

            [ExposedField]
            public TestElement[] items;
        }

        #endregion

        private List<string> _changedPaths;
        private List<string> _changingPaths;
        private ExposedClass _exposedClass;

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();

            ExposedClass.RegisterFromAttributes<TestNestedStruct>();
            ExposedClass.RegisterFromAttributes<TestElement>();
            ExposedClass.RegisterFromAttributes<TestTarget>();

            // ExposedObjectRegistry.instances をクリア
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove)
            {
                obj.Unregister();
            }

            _changedPaths = new List<string>();
            _changingPaths = new List<string>();

            _exposedClass = ExposedClass.Get<TestTarget>();
            _exposedClass.onPropertyChanging += _OnPropertyChanging;
            _exposedClass.onPropertyChanged += _OnPropertyChanged;
        }

        [TearDown]
        public void TearDown()
        {
            _exposedClass.onPropertyChanging -= _OnPropertyChanging;
            _exposedClass.onPropertyChanged -= _OnPropertyChanged;
        }

        private void _OnPropertyChanging(ExposedProperty property, object newValue)
        {
            _changingPaths.Add(property.path.Value);
        }

        private void _OnPropertyChanged(ExposedProperty property, object oldValue)
        {
            _changedPaths.Add(property.path.Value);
        }

        #region SetValue直接呼び出し (基準テスト)

        [Test]
        public void SetValue_Primitive_FiresPropertyChanged()
        {
            // Arrange
            var target = new TestTarget { intValue = 10 };
            var exposedObj = new ExposedObject("test-events-1", _exposedClass, target);
            var prop = exposedObj.FindProperty("intValue");
            Assert.IsNotNull(prop);

            // Act
            prop.Value.SetValue(42);

            // Assert
            Assert.That(_changingPaths, Contains.Item("intValue"));
            Assert.That(_changedPaths, Contains.Item("intValue"));

            exposedObj.Unregister();
        }

        [Test]
        public void SetValue_String_FiresPropertyChanged()
        {
            // Arrange
            var target = new TestTarget { stringValue = "old" };
            var exposedObj = new ExposedObject("test-events-2", _exposedClass, target);
            var prop = exposedObj.FindProperty("stringValue");
            Assert.IsNotNull(prop);

            // Act
            prop.Value.SetValue("new");

            // Assert
            Assert.That(_changedPaths, Contains.Item("stringValue"));

            exposedObj.Unregister();
        }

        #endregion

        #region FromJson経由 (RemoteAppと同じ経路)

        [Test]
        public void FromJson_Primitive_FiresPropertyChanged()
        {
            // Arrange
            var target = new TestTarget { intValue = 10 };
            var exposedObj = new ExposedObject("test-events-3", _exposedClass, target);
            var prop = exposedObj.FindProperty("intValue");
            Assert.IsNotNull(prop);

            // Act — RemoteAppと同じ経路: ExposedPropertySerializer.FromJson(json, in prop)
            var json = "{\"value\": 99}";
            var p = prop.Value;
            ExposedPropertySerializer.FromJson(json, in p);

            // Assert
            Assert.That(_changedPaths, Contains.Item("intValue"),
                "FromJsonでプリミティブを更新した際にonPropertyChangedが発火すべき");

            exposedObj.Unregister();
        }

        [Test]
        public void FromJson_String_FiresPropertyChanged()
        {
            // Arrange
            var target = new TestTarget { stringValue = "old" };
            var exposedObj = new ExposedObject("test-events-4", _exposedClass, target);
            var prop = exposedObj.FindProperty("stringValue");
            Assert.IsNotNull(prop);

            // Act
            var json = "{\"value\": \"newValue\"}";
            var p = prop.Value;
            ExposedPropertySerializer.FromJson(json, in p);

            // Assert
            Assert.That(_changedPaths, Contains.Item("stringValue"),
                "FromJsonで文字列を更新した際にonPropertyChangedが発火すべき");

            exposedObj.Unregister();
        }

        [Test]
        public void FromJson_NestedStruct_FiresPropertyChanged()
        {
            // Arrange
            var target = new TestTarget { nested = new TestNestedStruct { value = 1 } };
            var exposedObj = new ExposedObject("test-events-5", _exposedClass, target);
            var prop = exposedObj.FindProperty("nested");
            Assert.IsNotNull(prop);

            // Act
            var json = "{\"value\": {\"value\": 42}}";
            var p = prop.Value;
            ExposedPropertySerializer.FromJson(json, in p);

            // Assert — nested自身のSetValue、またはnested.valueのSetValueでイベントが発火するはず
            Assert.IsTrue(
                _changedPaths.Any(p2 => p2.Contains("nested")),
                $"FromJsonで構造体を更新した際にonPropertyChangedが発火すべき。実際の発火パス: [{string.Join(", ", _changedPaths)}]");

            exposedObj.Unregister();
        }

        [Test]
        public void FromJson_ArrayElement_FiresPropertyChanged()
        {
            // Arrange
            var target = new TestTarget
            {
                items = new TestElement[]
                {
                    new TestElement { flag = false, label = "first" },
                    new TestElement { flag = true, label = "second" }
                }
            };
            var exposedObj = new ExposedObject("test-events-6", _exposedClass, target);

            // 配列要素[0]のflagプロパティを指定して更新
            var prop = exposedObj.FindProperty("items[0].flag");
            Assert.IsNotNull(prop);

            // Act
            var json = "{\"value\": true}";
            var p = prop.Value;
            ExposedPropertySerializer.FromJson(json, in p);

            // Assert — items[0].flagパスでイベントが発火すべき
            Assert.IsTrue(
                _changedPaths.Any(p2 => p2.Contains("items")),
                $"FromJsonで配列要素を更新した際にonPropertyChangedが発火すべき。実際の発火パス: [{string.Join(", ", _changedPaths)}]");

            exposedObj.Unregister();
        }

        [Test]
        public void FromJson_WholeArray_FiresPropertyChangedForElements()
        {
            // Arrange
            var target = new TestTarget
            {
                items = new TestElement[]
                {
                    new TestElement { flag = false, label = "a" }
                }
            };
            var exposedObj = new ExposedObject("test-events-7", _exposedClass, target);

            var prop = exposedObj.FindProperty("items");
            Assert.IsNotNull(prop);

            // Act — 配列全体を更新
            var json = "{\"value\": [{\"flag\": true, \"label\": \"x\"}, {\"flag\": false, \"label\": \"y\"}]}";
            var p = prop.Value;
            ExposedPropertySerializer.FromJson(json, in p);

            // Assert — 要素ごとにイベントが発火すべき
            Assert.IsTrue(
                _changedPaths.Any(p2 => p2.Contains("items")),
                $"FromJsonで配列全体を更新した際に要素のonPropertyChangedが発火すべき。実際の発火パス: [{string.Join(", ", _changedPaths)}]");

            exposedObj.Unregister();
        }

        #endregion

        #region CreateUnregistered経路 (RemoteAppフォールバック)

        [Test]
        public void CreateUnregistered_SetValue_FiresPropertyChanged()
        {
            // Arrange — CreateUnregisteredで生成したExposedObjectでもイベントが発火するか検証
            var target = new TestTarget { intValue = 10 };
            var unregisteredObj = ExposedObject.CreateUnregistered(_exposedClass, target);
            var prop = unregisteredObj.FindProperty("intValue");
            Assert.IsNotNull(prop);

            // Act
            prop.Value.SetValue(99);

            // Assert — targetTypeが同じExposedClassインスタンスを指しているためイベントが発火すべき
            Assert.That(_changedPaths, Contains.Item("intValue"),
                "CreateUnregisteredで生成したExposedObjectからのSetValueでもonPropertyChangedが発火すべき");
        }

        [Test]
        public void CreateUnregistered_FromJson_FiresPropertyChanged()
        {
            // Arrange — RemoteAppフォールバック経路の再現
            var target = new TestTarget { intValue = 10 };
            var unregisteredObj = ExposedObject.CreateUnregistered(_exposedClass, target);
            var prop = unregisteredObj.FindProperty("intValue");
            Assert.IsNotNull(prop);

            // Act
            var json = "{\"value\": 77}";
            var p = prop.Value;
            ExposedPropertySerializer.FromJson(json, in p);

            // Assert
            Assert.That(_changedPaths, Contains.Item("intValue"),
                "CreateUnregistered経由のFromJsonでもonPropertyChangedが発火すべき");
        }

        [Test]
        public void CreateUnregistered_TargetType_IsSameInstance()
        {
            // Arrange — targetTypeの同一性を確認
            var target = new TestTarget();
            var unregisteredObj = ExposedObject.CreateUnregistered(_exposedClass, target);

            // Assert
            Assert.AreSame(_exposedClass, unregisteredObj.targetType,
                "CreateUnregisteredのtargetTypeはExposedClass.Get<T>()と同一インスタンスであるべき");
        }

        #endregion

        #region object[]経由のオーナー切り替え (RemoteApp実経路)

        /// <summary>
        /// ObjectSelectorBase相当: object[]を返すラッパー。
        /// RemoteAppはこのselectorのobjects[0].xxx経由でプロパティにアクセスする。
        /// </summary>
        [Serializable]
        [ExposedClass("TestEventsSelector")]
        public class TestSelector
        {
            private object[] _targets;

            [ExposedProperty]
            public object[] objects => _targets;

            public TestSelector(params object[] targets) { _targets = targets; }
        }

        [Test]
        public void ObjectArrayPath_SetValue_FiresPropertyChanged()
        {
            // Arrange — Selector経由でTargetのプロパティにアクセスする構成
            ExposedClass.RegisterFromAttributes<TestSelector>();

            var target = new TestTarget { intValue = 10, items = new TestElement[0] };
            var targetExposedObj = new ExposedObject("test-target-obj", _exposedClass, target);

            var selector = new TestSelector(target);
            var selectorClass = ExposedClass.Get<TestSelector>();
            var selectorExposedObj = new ExposedObject("test-selector", selectorClass, selector);

            // Act — selector経由でobjects[0].intValueを見つけてSetValue
            var prop = selectorExposedObj.FindProperty("objects[0].intValue");
            Assert.IsNotNull(prop, "objects[0].intValueが見つからない");

            prop.Value.SetValue(99);

            // Assert — AvatarControllerのExposedClassでイベントが発火すべき
            Assert.That(_changedPaths, Contains.Item("intValue"),
                $"object[]経由のSetValueでもonPropertyChangedが発火すべき。実際の発火パス: [{string.Join(", ", _changedPaths)}]");

            targetExposedObj.Unregister();
            selectorExposedObj.Unregister();
        }

        [Test]
        public void ObjectArrayPath_AddArrayElement_FiresPropertyChanged()
        {
            // Arrange — RemoteAppのPOST（配列要素追加）と同じ経路
            ExposedClass.RegisterFromAttributes<TestSelector>();

            var target = new TestTarget { intValue = 10, items = new TestElement[0] };
            var targetExposedObj = new ExposedObject("test-target-add", _exposedClass, target);

            var selector = new TestSelector(target);
            var selectorClass = ExposedClass.Get<TestSelector>();
            var selectorExposedObj = new ExposedObject("test-selector-add", selectorClass, selector);

            // Act — selector経由でobjects[0].itemsを見つけてAddArrayElement
            var prop = selectorExposedObj.FindProperty("objects[0].items");
            Assert.IsNotNull(prop, "objects[0].itemsが見つからない");

            var json = "{\"value\":{}}";
            var p = prop.Value;
            ExposedPropertySerializer.AddArrayElement(json, in p);

            // Assert — itemsプロパティ自体、または要素プロパティでイベントが発火すべき
            Assert.IsTrue(
                _changedPaths.Any(path => path.Contains("items")),
                $"AddArrayElement時にonPropertyChangedが発火すべき。実際の発火パス: [{string.Join(", ", _changedPaths)}]");

            targetExposedObj.Unregister();
            selectorExposedObj.Unregister();
        }

        [Test]
        public void ObjectArrayPath_WithoutRegisteredTarget_PlainClass_OwnerStaysAsParent()
        {
            // Arrange — plain class（非UnityEngine.Object）のTargetが
            // ExposedObjectRegistryに未登録の場合、ownerは親のままになる
            ExposedClass.RegisterFromAttributes<TestSelector>();

            var target = new TestTarget { intValue = 10, items = new TestElement[0] };
            // 注意: targetExposedObjを作らない（未登録状態）
            // TestTargetはplain classなのでCreateUnregisteredの対象外

            var selector = new TestSelector(target);
            var selectorClass = ExposedClass.Get<TestSelector>();
            var selectorExposedObj = new ExposedObject("test-selector-unreg", selectorClass, selector);

            // Act — selector経由でobjects[0].intValueを見つけてSetValue
            var prop = selectorExposedObj.FindProperty("objects[0].intValue");
            Assert.IsNotNull(prop, "objects[0].intValueが見つからない");

            prop.Value.SetValue(99);

            // Assert — plain classはCreateUnregisteredの対象外なので、
            // TestTargetのExposedClassではなくSelectorのExposedClassで発火する
            Assert.IsFalse(_changedPaths.Contains("intValue"),
                "plain classの未登録ターゲットではTargetのExposedClassにイベントが届かない");

            selectorExposedObj.Unregister();
        }

        #endregion

        #region ExposedClass差し替え検証

        [Test]
        public void RegisterFromAttributes_MigratesEvents_AfterReplacement()
        {
            // Arrange — 購読後にRegisterFromAttributesを再度呼んだ場合、
            // イベント購読が新インスタンスに移行されることを検証
            var classBeforeReRegister = ExposedClass.Get<TestTarget>();

            // Act — 再登録（インスタンスが差し替わる）
            ExposedClass.RegisterFromAttributes<TestTarget>();
            var classAfterReRegister = ExposedClass.Get<TestTarget>();

            // 差し替わっている場合でも、イベントが移行されて発火することを検証
            var target = new TestTarget { intValue = 10 };
            var exposedObj = new ExposedObject("test-events-reregister", classAfterReRegister, target);
            var prop = exposedObj.FindProperty("intValue");
            prop.Value.SetValue(42);

            Assert.That(_changedPaths, Contains.Item("intValue"),
                "RegisterFromAttributes後もイベント購読が移行され、onPropertyChangedが発火すべき");

            exposedObj.Unregister();
        }

        #endregion
    }
}
