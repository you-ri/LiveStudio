// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// ExposedObjectRegistry.FindByCategoryの重複検出バグの回帰テスト。
    /// instancesに登録済みのコンポーネントがFindObjectsByTypeで再発見され、
    /// 異なるIDで重複登録されるケースを検証する。
    /// </summary>
    [TestFixture]
    public class FindByCategoryTests
    {
        private const string kTestCategory = "TestFindByCategory";

        [ExposedClass("TestFindByCategoryComponent", Category = kTestCategory, Icon = "test")]
        public class TestCategoryComponent : MonoBehaviour
        {
            [ExposedField]
            public int value;
        }

        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();
            ExposedClass.RegisterFromAttributes<TestCategoryComponent>();

            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove)
            {
                obj.Unregister();
            }
        }

        [TearDown]
        public void TearDown()
        {
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove)
            {
                obj.Unregister();
            }

            foreach (var go in _createdObjects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _createdObjects.Clear();
        }

        private GameObject CreateGameObjectWithComponent(string name)
        {
            var go = new GameObject(name);
            go.AddComponent<TestCategoryComponent>();
            _createdObjects.Add(go);
            return go;
        }

        [Test]
        public void FindByCategory_SingleComponent_ReturnsOne()
        {
            // Arrange: シーンにコンポーネントを1つ配置
            var go = CreateGameObjectWithComponent("TestObject");

            // Act
            var result = ExposedObjectRegistry.FindByCategory(kTestCategory);

            // Assert: 1つだけ返る
            Assert.AreEqual(1, result.Count, "コンポーネントが1つなら結果も1つであるべき");
        }

        [Test]
        public void FindByCategory_ComponentPreRegisteredInInstances_NoDuplicate()
        {
            // Arrange: シーンにコンポーネントを配置し、先にinstancesに別IDで登録する
            // （ExposedObjectContainerから登録されるケースのシミュレーション）
            var go = CreateGameObjectWithComponent("MainScreen");
            var component = go.GetComponent<TestCategoryComponent>();
            var exposedClass = ExposedClass.Find(typeof(TestCategoryComponent));

            // GUIDベースのIDでinstancesに事前登録（Container経由の登録をシミュレート）
            var guidId = "container-guid-" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            var preRegistered = ExposedObjectRegistry.GetOrCreate(guidId, exposedClass, component);
            Assert.IsNotNull(preRegistered, "事前登録が成功するべき");

            // Act: FindByCategoryを呼ぶ
            // ステップ2でinstancesからguidIdで見つかり、
            // ステップ3でFindObjectsByTypeでGetInstanceID()ベースのIDで再発見される
            var result = ExposedObjectRegistry.FindByCategory(kTestCategory);

            // Assert: 同一コンポーネントが重複して返されてはいけない
            Assert.AreEqual(1, result.Count,
                "同一コンポーネントがinstancesとFindObjectsByTypeの両方で見つかっても結果は1つであるべき");
        }

        [Test]
        public void FindByCategory_MultipleComponents_OnePreRegistered_CorrectCount()
        {
            // Arrange: シーンに2つのコンポーネントを配置し、1つだけ事前登録
            var go1 = CreateGameObjectWithComponent("Screen1");
            var go2 = CreateGameObjectWithComponent("Screen2");
            var component1 = go1.GetComponent<TestCategoryComponent>();
            var exposedClass = ExposedClass.Find(typeof(TestCategoryComponent));

            // component1のみGUIDベースIDで事前登録
            var guidId = "pre-registered-guid";
            ExposedObjectRegistry.GetOrCreate(guidId, exposedClass, component1);

            // Act
            var result = ExposedObjectRegistry.FindByCategory(kTestCategory);

            // Assert: 2つのコンポーネントなので結果は2つ
            Assert.AreEqual(2, result.Count,
                "2つの異なるコンポーネントがある場合、結果は2つであるべき");
        }

        [Test]
        public void FindByCategory_AllPreRegistered_NoDuplicates()
        {
            // Arrange: 全コンポーネントを事前登録
            var go1 = CreateGameObjectWithComponent("ScreenA");
            var go2 = CreateGameObjectWithComponent("ScreenB");
            var component1 = go1.GetComponent<TestCategoryComponent>();
            var component2 = go2.GetComponent<TestCategoryComponent>();
            var exposedClass = ExposedClass.Find(typeof(TestCategoryComponent));

            ExposedObjectRegistry.GetOrCreate("guid-a", exposedClass, component1);
            ExposedObjectRegistry.GetOrCreate("guid-b", exposedClass, component2);

            // Act
            var result = ExposedObjectRegistry.FindByCategory(kTestCategory);

            // Assert: 重複なく2つ返る
            Assert.AreEqual(2, result.Count,
                "全コンポーネントが事前登録済みでも重複なく2つ返るべき");

            // targetの重複がないことも確認
            var targets = result.Select(r => r.target).Distinct().ToList();
            Assert.AreEqual(2, targets.Count, "異なるtargetが2つ存在するべき");
        }

        [Test]
        public void FindByCategory_PreRegisteredComponent_RetainsOriginalId()
        {
            // Arrange: GUIDベースIDで事前登録されたコンポーネントのIDが保持されることを確認
            var go = CreateGameObjectWithComponent("MainScreen");
            var component = go.GetComponent<TestCategoryComponent>();
            var exposedClass = ExposedClass.Find(typeof(TestCategoryComponent));

            var originalId = "original-guid-id";
            ExposedObjectRegistry.GetOrCreate(originalId, exposedClass, component);

            // Act
            var result = ExposedObjectRegistry.FindByCategory(kTestCategory);

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(originalId, result[0].id,
                "事前登録時のIDが保持されるべき（GetInstanceIDベースのIDに置き換わってはいけない）");
        }
    }
}
