// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Scene;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Load+Save時の@ref ID破損・ExposedObject増殖バグの回帰テスト。
    /// 3つのバグを検証:
    /// 1. ExposedObjectContainer.FindByTarget のnull比較バグ
    /// 2. ExposedUnityObjectProxyコンストラクタでの不要なExposedObject生成
    /// 3. ExposedObjectRegistry.GetOrCreateがIDパラメータを無視する問題
    /// </summary>
    [TestFixture]
    public class ExposedObjectLoadSaveTests
    {
        #region Test Classes

        [Serializable]
        [ExposedClass("TestLoadSaveProxy", Icon = "test")]
        public class TestProxy : ExposedUnityObjectBase, IExposedObject
        {
            [SerializeField]
            public string _referenceName;

            string IExposedObject.name
            {
                get => _referenceName;
                set => _referenceName = value;
            }

            public override string id => _referenceName;

            [ExposedField]
            public int value;

            public TestProxy()
            {
            }

            public TestProxy(string referenceName)
            {
                _referenceName = referenceName;
                _exposedObject = ExposedObjectRegistry.Create<TestProxy>(this, id);
            }
        }

        public class MockExposedObjectResolver : IExposedObjectResolver
        {
            public ExposedObject FindById(string id) => ExposedObjectRegistry.FindById(id);
            public ExposedObject FindByTarget(object target) => ExposedObjectRegistry.FindByTarget(target);
        }

        #endregion

        private MockExposedObjectResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();

            ExposedClass.RegisterFromAttributes<TestProxy>();

            // ExposedObjectRegistry.instances をクリア
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove)
            {
                obj.Unregister();
            }

            _resolver = new MockExposedObjectResolver();
        }

        [TearDown]
        public void TearDown()
        {
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove)
            {
                obj.Unregister();
            }
        }

        #region Bug 1: ExposedObjectContainer.FindByTarget null比較

        [Test]
        public void FindByTarget_NullTarget_ReturnsNull()
        {
            // Arrange
            var go = new GameObject("TestContainer");
            var container = new ExposedObjectContainer(go.name, new List<IExposedObject>());

            try
            {
                // Act
                var result = container.FindByTarget(null);

                // Assert: nullターゲットに対してnullが返る（null == null誤マッチしない）
                Assert.IsNull(result);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void FindByTarget_NonUnityObjectTarget_DoesNotMatchNullReference()
        {
            // Arrange: referenceがnullのオブジェクトをContainerに追加
            var go = new GameObject("TestContainer");
            var container = new ExposedObjectContainer(go.name, new List<IExposedObject>());

            var proxy = new TestProxy("test-id-1");
            container.AddExposedObject(proxy);

            // ExposedObjectRegistry.instancesに登録されていないオブジェクトで検索
            var unregisteredTarget = new object();

            try
            {
                // Act: reference==nullのオブジェクトに対して非UnityObjectでFindByTargetしても
                //      null == null でマッチしてはいけない
                var result = container.FindByTarget(unregisteredTarget);

                // Assert: Container内のnull referenceオブジェクトにマッチしない
                // ExposedObjectRegistry.FindByTargetにフォールバックするが、未登録なのでnull
                Assert.IsNull(result, "非UnityObjectターゲットがnull referenceのオブジェクトにマッチしてはいけない");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void FindByTarget_MultipleNullReferences_DoesNotReturnFirstNullMatch()
        {
            // Arrange: referenceがnullのオブジェクトを複数Containerに追加
            var go = new GameObject("TestContainer");
            var container = new ExposedObjectContainer(go.name, new List<IExposedObject>());

            var proxy1 = new TestProxy("id-1");
            var proxy2 = new TestProxy("id-2");
            var proxy3 = new TestProxy("id-3");
            container.AddExposedObject(proxy1);
            container.AddExposedObject(proxy2);
            container.AddExposedObject(proxy3);

            try
            {
                // Act: 全てのプロキシのreferenceはnull（UnityEngine.Objectではない）
                // 別のプロキシで検索しても、null==nullマッチで最初の要素を返してはいけない
                var result = container.FindByTarget(proxy2);

                // Assert: proxy2自体はcontainerのreference比較ではマッチしない
                // ExposedObjectRegistry.FindByTargetにフォールバックし、proxy2のExposedObjectを返す
                if (result != null)
                {
                    Assert.AreEqual("id-2", result.id, "正しいIDのExposedObjectが返されるべき");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        #endregion

        #region Bug 2: コンストラクタでの不要なExposedObject生成

        [Test]
        public void ExposedGameObject_Constructor_NullReference_DoesNotCreateExposedObject()
        {
            // Arrange & Act: nullでExposedGameObjectを作成（デシリアライズ時のシミュレーション）
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();

            var initialCount = ExposedObjectRegistry.instances.Count;
            var proxy = new ExposedGameObject(null);

            // Assert: referenceがnullの場合、ExposedObjectは生成されない
            Assert.IsNull(proxy.exposedObject, "null referenceでExposedObjectが生成されてはいけない");
            Assert.AreEqual(initialCount, ExposedObjectRegistry.instances.Count, "ExposedObjectRegistry.instancesに不要なエントリが追加されてはいけない");
        }

        [Test]
        public void ExposedGameObject_Constructor_ValidReference_CreatesExposedObject()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            var testGo = new GameObject("TestGO");

            try
            {
                var initialCount = ExposedObjectRegistry.instances.Count;

                // Act
                var proxy = new ExposedGameObject(testGo);

                // Assert: 有効なreferenceの場合はExposedObjectが生成される
                Assert.IsNotNull(proxy.exposedObject, "有効なreferenceでExposedObjectが生成されるべき");
                Assert.AreEqual(initialCount + 1, ExposedObjectRegistry.instances.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(testGo);
            }
        }

        #endregion

        #region Bug 3: GetOrCreate IDパラメータ無視

        [Test]
        public void GetOrCreate_ExistingTargetWithDifferentId_ReturnsExisting()
        {
            // Arrange
            ExposedClass.RegisterFromAttributes<TestProxy>();

            var target = new object();
            var firstId = "first-id";
            var exposedClass = ExposedClass.Find(typeof(TestProxy));

            // 最初のIDでExposedObject作成
            var initial = ExposedObjectRegistry.GetOrCreate(firstId, exposedClass, target);
            Assert.AreEqual(firstId, initial.id);

            // Act: 異なるIDで再取得を試みる
            var resolved = ExposedObjectRegistry.GetOrCreate("different-id", exposedClass, target);

            // Assert: IDは変更されず、既存のExposedObjectがそのまま返される（IDは不変）
            Assert.AreSame(initial, resolved, "同じtargetに対しては既存のExposedObjectが返されるべき");
            Assert.AreEqual(firstId, resolved.id, "IDは最初に設定された値のまま変更されない");
        }

        [Test]
        public void GetOrCreate_ExistingTargetWithSameId_ReturnsSameInstance()
        {
            // Arrange
            var target = new TestProxy("same-id");
            var exposedClass = ExposedClass.Find(typeof(TestProxy));

            var initial = ExposedObjectRegistry.GetOrCreate("same-id", exposedClass, target);

            // Act: 同じIDで再度呼び出し
            var result = ExposedObjectRegistry.GetOrCreate("same-id", exposedClass, target);

            // Assert: 同じインスタンスが返される
            Assert.AreSame(initial, result, "同じIDの場合は同じインスタンスが返されるべき");
        }

        [Test]
        public void GetOrCreate_SameTargetSameId_ReturnsSameInstance()
        {
            // Arrange: 同じターゲット・同じIDで重複作成しないことを確認
            var target = new object();
            var exposedClass = ExposedClass.Find(typeof(TestProxy));
            var id = "dedup-test-id";

            var initial = ExposedObjectRegistry.GetOrCreate(id, exposedClass, target);
            var initialCount = ExposedObjectRegistry.instances.Count;

            // Act: 同じターゲット・同じIDで再度呼び出し
            var result = ExposedObjectRegistry.GetOrCreate(id, exposedClass, target);

            // Assert: 同じインスタンスが返され、instancesは増えない
            Assert.AreSame(initial, result, "同一ターゲット・同一IDでは同じインスタンスを返すべき");
            Assert.AreEqual(initialCount, ExposedObjectRegistry.instances.Count, "instancesが増えてはいけない");
        }

        [Test]
        public void GetOrCreate_DifferentId_SameTarget_ReturnsExistingAndNoNewInstance()
        {
            // Arrange: 同じターゲットで異なるIDのExposedObjectが作られるシナリオ
            var target = new object();
            var exposedClass = ExposedClass.Find(typeof(TestProxy));

            var initialCount = ExposedObjectRegistry.instances.Count;

            // 最初のIDで作成
            var firstId = System.Guid.NewGuid().ToString();
            var first = ExposedObjectRegistry.GetOrCreate(firstId, exposedClass, target);
            Assert.AreEqual(initialCount + 1, ExposedObjectRegistry.instances.Count);

            // Act: 異なるIDで再取得を試みる
            var secondId = "different-id";
            var result = ExposedObjectRegistry.GetOrCreate(secondId, exposedClass, target);

            // Assert: 既存のExposedObjectがそのまま返される（IDは不変、インスタンス数も変わらない）
            Assert.AreSame(first, result, "同じtargetに対しては既存インスタンスが返されるべき");
            Assert.AreEqual(firstId, result.id, "IDは最初に設定された値のまま");
            Assert.AreEqual(initialCount + 1, ExposedObjectRegistry.instances.Count, "インスタンス数は増えない");
            Assert.IsNull(ExposedObjectRegistry.FindById(secondId), "新しいIDでは検索不可（作成されていない）");
        }

        #endregion

        #region Bug 4: @name が null になるバグ

        [Serializable]
        [ExposedClass("TestNameFallbackProxy", Icon = "test")]
        public class TestNameFallbackProxy : ExposedUnityObjectBase
        {
            [SerializeField]
            public string _referenceName;

            [SerializeField, ExposedField, Hide]
            [FormerlyExposedAs("name")]
            private string _fallbackName;

            [ExposedProperty]
            public override string name
            {
                get => _fallbackName;
                set => _fallbackName = value;
            }

            public override string id => _referenceName;

            [ExposedField]
            public int value;

            public TestNameFallbackProxy()
            {
            }

            public TestNameFallbackProxy(string referenceName, string name = null)
            {
                _referenceName = referenceName;
                _fallbackName = name;
                _exposedObject = ExposedObjectRegistry.Create<TestNameFallbackProxy>(this, id);
            }
        }

        [Test]
        public void FromJson_AtNameField_IsRestoredToNameProperty()
        {
            // Arrange: @name を含むJSONをデシリアライズし、nameプロパティに復元されることを確認
            ExposedClass.RegisterFromAttributes<TestNameFallbackProxy>();

            var proxy = new TestNameFallbackProxy("name-test-id");
            Assert.IsTrue(string.IsNullOrEmpty(proxy.name), "初期状態でnameはnull/空であるべき");

            // @name を含むJSON
            var json = @"{
                ""@type"": ""TestNameFallbackProxy"",
                ""@id"": ""name-test-id"",
                ""@name"": ""TestCamera"",
                ""value"": 42
            }";

            // Act
            ExposedPropertySerializer.FromJson(json, proxy.exposedObject, _resolver);

            // Assert
            Assert.AreEqual("TestCamera", proxy.name, "@nameがnameプロパティに復元されるべき");
            Assert.AreEqual(42, proxy.value, "通常のプロパティも復元されるべき");
        }

        [Test]
        public void FromJson_AtNameField_DoesNotOverwriteExistingName()
        {
            // Arrange: 既にnameが設定されている場合、@nameで上書きしない
            ExposedClass.RegisterFromAttributes<TestNameFallbackProxy>();

            var proxy = new TestNameFallbackProxy("name-existing-id", "ExistingName");
            Assert.AreEqual("ExistingName", proxy.name);

            var json = @"{
                ""@type"": ""TestNameFallbackProxy"",
                ""@id"": ""name-existing-id"",
                ""@name"": ""NewName"",
                ""value"": 10
            }";

            // Act
            ExposedPropertySerializer.FromJson(json, proxy.exposedObject, _resolver);

            // Assert: 既存のnameは上書きされない
            Assert.AreEqual("ExistingName", proxy.name, "既存のnameは@nameで上書きされるべきではない");
        }

        [Test]
        public void LoadSaveCycle_Name_IsPreservedWhenDirty()
        {
            // Arrange: name を dirty にしてから save すれば、Shadow Field 経由の name プロパティとして
            // 通常シリアライズされ、Load で復元されることを確認。@name メタは永続化されない。
            ExposedClass.RegisterFromAttributes<TestNameFallbackProxy>();

            var proxy = new TestNameFallbackProxy("name-cycle-id", "InitialName");
            proxy.value = 99;

            ExposedPropertyUtility.SetDefault(proxy.exposedObject);
            proxy.value = 100;
            proxy.name = "Camera"; // name を default から変えて dirty にする

            // Save
            var json1 = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            Assert.IsNotEmpty(json1);

            // 通常プロパティ name が dirty 値で含まれる
            Assert.IsTrue(json1.Contains("\"name\": \"Camera\"") || json1.Contains("\"name\":\"Camera\""),
                "SceneToJson の出力に name プロパティが含まれるべき (dirty時)");

            // クリア
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newProxy = new TestNameFallbackProxy("name-cycle-id", "InitialName");
            ExposedPropertyUtility.SetDefault(newProxy.exposedObject);

            // Load
            ExposedSceneSerializer.SceneFromJson(json1, _resolver);

            Assert.AreEqual("Camera", newProxy.name, "Load 後に name プロパティが復元されるべき");
            Assert.AreEqual(100, newProxy.value, "値も復元されるべき");
        }

        #endregion

        #region 統合テスト: Load+Saveサイクル

        [Test]
        public void LoadSaveCycle_RefIds_ArePreserved()
        {
            // Arrange: 複数のExposedObjectを作成してシリアライズ→デシリアライズ→再シリアライズ
            var id1 = "ref-id-aaa";
            var id2 = "ref-id-bbb";
            var id3 = "ref-id-ccc";

            var target1 = new TestProxy(id1) { value = 10 };
            var target2 = new TestProxy(id2) { value = 20 };
            var target3 = new TestProxy(id3) { value = 30 };

            // デフォルト値をキャプチャしてdirty検出を有効化
            ExposedPropertyUtility.SetDefault(target1.exposedObject);
            ExposedPropertyUtility.SetDefault(target2.exposedObject);
            ExposedPropertyUtility.SetDefault(target3.exposedObject);

            // 値を変更してdirtyにする
            target1.value = 100;
            target2.value = 200;
            target3.value = 300;

            // シリアライズ（Save相当）
            var json1 = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            Assert.IsNotEmpty(json1);

            // 全インスタンスをクリア（Load前状態のシミュレーション）
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            // 新しいターゲットで再登録（ResolveReferences相当）
            var newTarget1 = new TestProxy(id1);
            var newTarget2 = new TestProxy(id2);
            var newTarget3 = new TestProxy(id3);

            // デシリアライズ（Load相当）
            ExposedSceneSerializer.SceneFromJson(json1, _resolver);

            // デフォルト値を再キャプチャ
            if (newTarget1.exposedObject != null) ExposedPropertyUtility.SetDefault(newTarget1.exposedObject);
            if (newTarget2.exposedObject != null) ExposedPropertyUtility.SetDefault(newTarget2.exposedObject);
            if (newTarget3.exposedObject != null) ExposedPropertyUtility.SetDefault(newTarget3.exposedObject);

            // 再シリアライズ（Save相当）
            var json2 = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert: 各IDのExposedObjectが正しいIDを保持している
            var obj1 = ExposedObjectRegistry.FindById(id1);
            var obj2 = ExposedObjectRegistry.FindById(id2);
            var obj3 = ExposedObjectRegistry.FindById(id3);

            Assert.IsNotNull(obj1, $"ID '{id1}' のExposedObjectが存在するべき");
            Assert.IsNotNull(obj2, $"ID '{id2}' のExposedObjectが存在するべき");
            Assert.IsNotNull(obj3, $"ID '{id3}' のExposedObjectが存在するべき");

            // 全てのIDが同一にならないことを確認（Bug1の回帰テスト）
            Assert.AreNotEqual(obj1.id, obj2.id, "異なるオブジェクトのIDが同一になってはいけない");
            Assert.AreNotEqual(obj2.id, obj3.id, "異なるオブジェクトのIDが同一になってはいけない");
        }

        [Test]
        public void GetOrCreate_AfterDeserializationSimulation_KeepsFirstId()
        {
            // Arrange: デシリアライズシミュレーション
            // コンストラクタでExposedObjectが作られ、
            // その後GetOrCreateで別IDで呼ばれても最初のIDが保持されるパターン
            var exposedClass = ExposedClass.Find(typeof(TestProxy));
            var secondId = "second-id";

            var target = new TestProxy("temp"); // コンストラクタでExposedObject生成
            var firstId = target.exposedObject?.id;
            Assert.IsNotNull(firstId, "コンストラクタでExposedObjectが生成されるべき");

            // Act: 異なるIDでGetOrCreate
            var resolved = ExposedObjectRegistry.GetOrCreate(secondId, exposedClass, target);

            // Assert: 最初のIDが保持される（IDは不変）
            Assert.AreEqual(firstId, resolved.id, "最初に設定されたIDが保持されるべき");
            Assert.AreEqual(1, ExposedObjectRegistry.instances.Count(x => ReferenceEquals(x.target, target)),
                "同一ターゲットに対するExposedObjectは1つだけ存在するべき");
        }

        #endregion

        #region SceneToJson includeStatic option

        [ExposedClass("TestStaticClass", Icon = "test")]
        public static class TestStaticClass
        {
            [ExposedField]
            public static int staticValue = 42;
        }

        [Test]
        public void SceneToJson_ExcludeNone_ContainsStaticObject()
        {
            // Arrange
            var exposedClass = ExposedClass.RegisterClass(typeof(TestStaticClass));
            Assert.IsNotNull(exposedClass);
            Assert.IsTrue(exposedClass.isStatic);
            var staticObj = new ExposedObject("static-test", exposedClass, null);

            var proxy = new TestProxy("instance-test");

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Snapshot, ExcludeFilter.None);

            // Assert
            Assert.IsTrue(json.Contains("TestStaticClass"), "staticオブジェクトがJSON出力に含まれるべき");
            Assert.IsTrue(json.Contains("TestLoadSaveProxy"), "instanceオブジェクトもJSON出力に含まれるべき");

            staticObj.Unregister();
        }

        [Test]
        public void SceneToJson_ExcludeStatic_ExcludesStaticObject()
        {
            // Arrange
            var exposedClass = ExposedClass.RegisterClass(typeof(TestStaticClass));
            Assert.IsNotNull(exposedClass);
            Assert.IsTrue(exposedClass.isStatic);
            var staticObj = new ExposedObject("static-test", exposedClass, null);

            var proxy = new TestProxy("instance-test");

            // Act
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Snapshot, ExcludeFilter.Static);

            // Assert
            Assert.IsFalse(json.Contains("TestStaticClass"), "staticオブジェクトはJSON出力から除外されるべき");
            Assert.IsTrue(json.Contains("TestLoadSaveProxy"), "instanceオブジェクトはJSON出力に含まれるべき");

            staticObj.Unregister();
        }

        [Test]
        public void SceneToJson_DefaultExclude_ContainsStaticObject()
        {
            // Arrange
            var exposedClass = ExposedClass.RegisterClass(typeof(TestStaticClass));
            Assert.IsNotNull(exposedClass);
            var staticObj = new ExposedObject("static-test", exposedClass, null);

            // Act: デフォルト（exclude省略 = ExcludeFilter.None）
            var json = ExposedSceneSerializer.SceneToJson(new List<ExposedObject>(ExposedObjectRegistry.instances), _resolver);

            // Assert
            Assert.IsTrue(json.Contains("TestStaticClass"), "デフォルトではstaticオブジェクトがJSON出力に含まれるべき");

            staticObj.Unregister();
        }

        #endregion

        #region Delta保存: ScriptableObjectインライン保存

        /// <summary>
        /// テスト用ScriptableObject（AvatarExpressionConfig相当）
        /// </summary>
        [ExposedClass("TestConfig")]
        public class TestConfigSO : ScriptableObject
        {
            [ExposedField]
            public float blendTime = 0.25f;

            [ExposedField]
            public string configName = "default";
        }

        /// <summary>
        /// テスト用コンポーネント（AvatarController相当）
        /// ScriptableObjectをフィールドとして参照する
        /// </summary>
        [ExposedClass("TestAvatar")]
        public class TestAvatarComponent : MonoBehaviour
        {
            [ExposedField]
            public TestConfigSO config;

            [ExposedField]
            public int level = 1;
        }

        /// <summary>
        /// SceneFromJsonがExposedGameObjectのcomponents経由で
        /// インラインScriptableObjectのプロパティを正しく適用するか検証。
        /// studio.jsonのPlay→Load→Stop→Saveで objects:[] になる問題の根本原因テスト。
        /// </summary>
        [Test]
        public void SceneFromJson_InlineScriptableObject_AppliesValues()
        {
            ExposedClass.RegisterFromAttributes<TestConfigSO>();
            ExposedClass.RegisterFromAttributes<TestAvatarComponent>();
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();

            var go = new GameObject("Test Avatar");
            var avatarComp = go.AddComponent<TestAvatarComponent>();
            var configSO = ScriptableObject.CreateInstance<TestConfigSO>();
            configSO.blendTime = 0.7f; // 初期値
            configSO.configName = "initial";
            avatarComp.config = configSO;
            avatarComp.level = 1;

            try
            {
                var proxy = new ExposedGameObject(go);
                proxy.OnEnable();
                var proxyId = proxy.id;

                // JSONで異なる値を指定（studio.json相当）
                var loadJson = $@"{{
                    ""format"": ""jp.lilium.remotecontrol.scene"",
                    ""formatVersion"": 1,
                    ""objects"": [
                        {{
                            ""@type"": ""GameObject"",
                            ""@id"": ""{proxyId}"",
                            ""@name"": ""Test Avatar"",
                            ""components"": [
                                {{
                                    ""@type"": ""TestAvatar"",
                                    ""level"": 5,
                                    ""config"": {{
                                        ""@type"": ""TestConfig"",
                                        ""blendTime"": 0.25,
                                        ""configName"": ""modified""
                                    }}
                                }}
                            ]
                        }}
                    ]
                }}";

                // Act
                ExposedSceneSerializer.SceneFromJson(loadJson, _resolver);

                // Assert: コンポーネントのプロパティが更新されている
                Assert.AreEqual(5, avatarComp.level,
                    "SceneFromJsonでcomponent直接プロパティが適用されるべき");
                Assert.AreEqual(0.25f, configSO.blendTime, 0.001f,
                    "SceneFromJsonでインラインSO内のプロパティが適用されるべき");
                Assert.AreEqual("modified", configSO.configName,
                    "SceneFromJsonでインラインSO内の文字列プロパティが適用されるべき");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configSO);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// IDが異なりReplaceIdが行われるケースで、SceneFromJsonが
        /// components経由のインラインScriptableObjectプロパティを正しく適用するか検証。
        /// Play mode再入時のGUID再生成シナリオ。
        /// </summary>
        [Test]
        public void SceneFromJson_AfterReplaceId_InlineScriptableObject_AppliesValues()
        {
            ExposedClass.RegisterFromAttributes<TestConfigSO>();
            ExposedClass.RegisterFromAttributes<TestAvatarComponent>();
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();

            var go = new GameObject("Test Avatar");
            var avatarComp = go.AddComponent<TestAvatarComponent>();
            var configSO = ScriptableObject.CreateInstance<TestConfigSO>();
            configSO.blendTime = 0.7f;
            configSO.configName = "initial";
            avatarComp.config = configSO;
            avatarComp.level = 1;

            try
            {
                var proxy = new ExposedGameObject(go);
                proxy.OnEnable();

                // JSONには異なるID（前セッションのID）を使用 → ReplaceIdが必要
                var savedId = "saved-id-from-previous-session";
                var loadJson = $@"{{
                    ""format"": ""jp.lilium.remotecontrol.scene"",
                    ""formatVersion"": 1,
                    ""objects"": [
                        {{
                            ""@type"": ""GameObject"",
                            ""@id"": ""{savedId}"",
                            ""@name"": ""Test Avatar"",
                            ""components"": [
                                {{
                                    ""@type"": ""TestAvatar"",
                                    ""level"": 5,
                                    ""config"": {{
                                        ""@type"": ""TestConfig"",
                                        ""blendTime"": 0.25,
                                        ""configName"": ""modified""
                                    }}
                                }}
                            ]
                        }}
                    ]
                }}";

                // Act
                ExposedSceneSerializer.SceneFromJson(loadJson, _resolver);

                // Assert: ReplaceIdで解決された
                var resolved = ExposedObjectRegistry.FindById(savedId);
                Assert.IsNotNull(resolved, "ReplaceId後にsaved IDで検索できるべき");

                // Assert: 値が適用されている
                Assert.AreEqual(5, avatarComp.level,
                    "ReplaceId後のSceneFromJsonでlevelが適用されるべき");
                Assert.AreEqual(0.25f, configSO.blendTime, 0.001f,
                    "ReplaceId後のSceneFromJsonでインラインSO内blendTimeが適用されるべき");
                Assert.AreEqual("modified", configSO.configName,
                    "ReplaceId後のSceneFromJsonでインラインSO内configNameが適用されるべき");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configSO);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 2つのコンポーネントがあり、JSONに空の{}要素が含まれるケースを検証。
        /// studio.jsonの実際のフォーマットに合わせたテスト。
        /// </summary>
        [Test]
        public void SceneFromJson_WithEmptyComponentInJson_AppliesValues()
        {
            ExposedClass.RegisterFromAttributes<TestConfigSO>();
            ExposedClass.RegisterFromAttributes<TestAvatarComponent>();
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();

            // 2つのExposedClassコンポーネントを持つGameObject
            var go = new GameObject("Test Avatar");
            var avatarComp = go.AddComponent<TestAvatarComponent>();
            var configSO = ScriptableObject.CreateInstance<TestConfigSO>();
            configSO.blendTime = 0.7f;
            avatarComp.config = configSO;
            avatarComp.level = 1;

            // 2つ目のExposedClassコンポーネント（InputActions相当）
            // TestProxy extends ExposedUnityObjectBaseなのでMonoBehaviourではない
            // 代わりにTestAvatarComponentをもう1つ追加して2つ目のコンポーネントをシミュレート
            // 注: 実際はAvatarInputだが、ここでは型を増やさないため省略

            try
            {
                var proxy = new ExposedGameObject(go);
                proxy.OnEnable();
                var proxyId = proxy.id;

                // 実際のstudio.jsonフォーマット: 2番目のcomponentが{}
                var loadJson = $@"{{
                    ""format"": ""jp.lilium.remotecontrol.scene"",
                    ""formatVersion"": 1,
                    ""objects"": [
                        {{
                            ""@type"": ""GameObject"",
                            ""@id"": ""{proxyId}"",
                            ""@name"": ""Test Avatar"",
                            ""components"": [
                                {{
                                    ""@type"": ""TestAvatar"",
                                    ""level"": 5,
                                    ""config"": {{
                                        ""@type"": ""TestConfig"",
                                        ""blendTime"": 0.25
                                    }}
                                }},
                                {{}}
                            ]
                        }}
                    ]
                }}";

                // Act
                ExposedSceneSerializer.SceneFromJson(loadJson, _resolver);

                // Assert
                Assert.AreEqual(5, avatarComp.level,
                    "空の{}コンポーネントがあってもlevelが適用されるべき");
                Assert.AreEqual(0.25f, configSO.blendTime, 0.001f,
                    "空の{}コンポーネントがあってもblendTimeが適用されるべき");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configSO);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// ExposedObjectContainerをリゾルバーとして使用するケースで、
        /// ReplaceId後のSceneFromJsonが正しく値を適用するか検証。
        /// 実際のRemoteControlProviderと同じフロー。
        /// </summary>
        [Test]
        public void SceneFromJson_WithContainer_AfterReplaceId_AppliesValues()
        {
            ExposedClass.RegisterFromAttributes<TestConfigSO>();
            ExposedClass.RegisterFromAttributes<TestAvatarComponent>();
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<ExposedObjectContainer>();

            var containerGo = new GameObject("Container");
            var container = new ExposedObjectContainer(containerGo.name, new List<IExposedObject>());

            var avatarGo = new GameObject("Test Avatar");
            var avatarComp = avatarGo.AddComponent<TestAvatarComponent>();
            var configSO = ScriptableObject.CreateInstance<TestConfigSO>();
            configSO.blendTime = 0.7f;
            configSO.configName = "initial";
            avatarComp.config = configSO;
            avatarComp.level = 1;

            try
            {
                // ExposedGameObjectをContainerに登録（Initialize相当）
                var proxy = new ExposedGameObject(avatarGo);
                container.AddExposedObject(proxy);
                container.Initialize();

                var savedId = "saved-id-from-previous-session";
                var loadJson = $@"{{
                    ""format"": ""jp.lilium.remotecontrol.scene"",
                    ""formatVersion"": 1,
                    ""objects"": [
                        {{
                            ""@type"": ""GameObject"",
                            ""@id"": ""{savedId}"",
                            ""@name"": ""Test Avatar"",
                            ""components"": [
                                {{
                                    ""@type"": ""TestAvatar"",
                                    ""level"": 5,
                                    ""config"": {{
                                        ""@type"": ""TestConfig"",
                                        ""blendTime"": 0.25,
                                        ""configName"": ""modified""
                                    }}
                                }}
                            ]
                        }}
                    ]
                }}";

                // Act: ContainerをリゾルバーとしてSceneFromJson
                ExposedSceneSerializer.SceneFromJson(loadJson, container);

                // Assert: 値が適用されている
                Assert.AreEqual(5, avatarComp.level,
                    "Container経由のSceneFromJsonでlevelが適用されるべき");
                Assert.AreEqual(0.25f, configSO.blendTime, 0.001f,
                    "Container経由のSceneFromJsonでインラインSO内blendTimeが適用されるべき");
                Assert.AreEqual("modified", configSO.configName,
                    "Container経由のSceneFromJsonでインラインSO内configNameが適用されるべき");
            }
            finally
            {
                container.Shutdown();
                UnityEngine.Object.DestroyImmediate(configSO);
                UnityEngine.Object.DestroyImmediate(avatarGo);
                UnityEngine.Object.DestroyImmediate(containerGo);
            }
        }

        /// <summary>
        /// ScriptableObjectがインラインで保存されるケースで、
        /// Load→Saveサイクル後にobjectsが空にならないことを確認。
        /// </summary>
        [Test]
        public void LoadSaveCycle_InlineScriptableObject_DeltaSavePreservesObjects()
        {
            // テストクラスを登録
            ExposedClass.RegisterFromAttributes<TestConfigSO>();
            ExposedClass.RegisterFromAttributes<TestAvatarComponent>();
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();

            // Arrange: GameObjectとコンポーネントのセットアップ
            var go = new GameObject("Test Avatar");
            var avatarComp = go.AddComponent<TestAvatarComponent>();
            var configSO = ScriptableObject.CreateInstance<TestConfigSO>();
            configSO.blendTime = 0.25f;
            configSO.configName = "default";
            avatarComp.config = configSO;
            avatarComp.level = 1;

            try
            {
                // ExposedGameObject プロキシを作成（Container登録相当）
                var proxy = new ExposedGameObject(go);
                proxy.OnEnable();
                Assert.IsNotNull(proxy.exposedObject, "ExposedGameObject should have ExposedObject");

                // デフォルト値をキャプチャ（Container.Initialize相当）
                // inline children（コンポーネント・ScriptableObject）の defaults も
                // 登録しないと pending delta で差分が検出できない。
                var initialResolved = ExposedObjectGraph.ResolveExposedObjects(
                    new object[] { proxy }, _resolver);
                foreach (var obj in initialResolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // 値を変更してDelta JSON（保存済みデータ）を作成
                configSO.blendTime = 0.5f;
                configSO.configName = "modified";
                avatarComp.level = 5;

                var savedJson = ExposedSceneSerializer.SceneToJson(
                    initialResolved,
                    _resolver, SerializeMode.Delta);

                Assert.IsTrue(savedJson.Contains("\"blendTime\""), "保存JSONにblendTimeが含まれるべき");
                Assert.IsTrue(savedJson.Contains("\"level\": 5"), "保存JSONにlevel変更が含まれるべき");

                // Play mode再入シミュレーション: 値をデフォルトに戻す
                // rootId は保持したまま（ReplaceId フォールバックは廃止）
                configSO.blendTime = 0.25f;
                configSO.configName = "default";
                avatarComp.level = 1;

                proxy.OnDisable();
                proxy.OnEnable();
                Assert.IsNotNull(proxy.exposedObject);

                // デフォルトを再キャプチャ（値はデフォルトに戻っている）
                var reResolved = ExposedObjectGraph.ResolveExposedObjects(
                    new object[] { proxy }, _resolver);
                foreach (var obj in reResolved)
                    ExposedPropertyUtility.SetDefault(obj);

                // Act: LoadCurrentData相当 - SceneFromJsonで読み込み
                ExposedSceneSerializer.SceneFromJson(savedJson, _resolver);

                // Assert: 値が復元されている
                Assert.AreEqual(5, avatarComp.level, "Load後にlevelが復元されるべき");
                Assert.AreEqual(0.5f, configSO.blendTime, 0.001f, "Load後にblendTimeが復元されるべき");
                Assert.AreEqual("modified", configSO.configName, "Load後にconfigNameが復元されるべき");

                // Act: SaveCurrentData相当 - Delta保存
                var resavedJson = ExposedSceneSerializer.SceneToJson(
                    reResolved,
                    _resolver, SerializeMode.Delta);

                // Assert: objectsが空でない
                var jRoot = JObject.Parse(resavedJson);
                var objects = jRoot["objects"] as JArray;
                Assert.IsNotNull(objects, "objects配列が存在するべき");
                Assert.IsTrue(objects.Count > 0,
                    "ReplaceId後のDelta保存でobjectsが空にならないべき（objects:[]問題）");

                // 変更された値が保存されている: 新フォーマットでは TestAvatar が pending エントリとして出力される
                var avatarCompData = objects.FirstOrDefault(o => o["@type"]?.ToString() == "TestAvatar") as JObject;
                Assert.IsNotNull(avatarCompData,
                    $"Delta保存にTestAvatarコンポーネントのpendingエントリが含まれるべき. JSON: {resavedJson}");
                Assert.AreEqual(5, avatarCompData["level"]?.Value<int>(),
                    $"Delta保存にlevel変更が含まれるべき. JSON: {resavedJson}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configSO);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Play→Stop→Play→Stopの連続サイクルで、インラインScriptableObjectの
        /// 値がDelta保存で消えないことを確認。
        /// RemoteControlProviderの実フロー（Initialize→Load→Save→Revert）を2回再現。
        /// </summary>
        [Test]
        public void LoadSaveCycle_RepeatedPlayStop_InlineScriptableObject_Preserved()
        {
            ExposedClass.RegisterFromAttributes<TestConfigSO>();
            ExposedClass.RegisterFromAttributes<TestAvatarComponent>();
            ExposedClass.RegisterFromAttributes<ExposedGameObject>();

            var go = new GameObject("Test Avatar");
            var avatarComp = go.AddComponent<TestAvatarComponent>();
            var configSO = ScriptableObject.CreateInstance<TestConfigSO>();
            configSO.blendTime = 0.25f; // SO初期値
            configSO.configName = "default";
            avatarComp.config = configSO;
            avatarComp.level = 1;

            try
            {
                // --- 初回 Play→Stop ---

                // 1. Initialize: プロキシ登録 + デフォルトキャプチャ（inline children 含む）
                var proxy = new ExposedGameObject(go);
                proxy.OnEnable();
                var resolved1 = ExposedObjectGraph.ResolveExposedObjects(
                    new object[] { proxy }, _resolver);
                foreach (var obj in resolved1)
                    ExposedPropertyUtility.SetDefault(obj);

                // 2. Load: 保存済みJSONを適用（初期値と異なる値）
                var savedJson = $@"{{
                    ""format"": ""jp.lilium.remotecontrol.scene"",
                    ""formatVersion"": 1,
                    ""objects"": [{{
                        ""@type"": ""GameObject"",
                        ""@id"": ""{proxy.id}"",
                        ""@name"": ""Test Avatar"",
                        ""components"": [{{
                            ""@type"": ""TestAvatar"",
                            ""level"": 5,
                            ""config"": {{
                                ""@type"": ""TestConfig"",
                                ""blendTime"": 0.8,
                                ""configName"": ""saved""
                            }}
                        }}]
                    }}]
                }}";
                ExposedSceneSerializer.SceneFromJson(savedJson, _resolver);
                Assert.AreEqual(0.8f, configSO.blendTime, 0.001f, "1回目Load後にblendTimeが適用されるべき");

                // 3. Save: Delta保存
                var json1 = ExposedSceneSerializer.SceneToJson(
                    resolved1,
                    _resolver, SerializeMode.Delta);
                Assert.IsTrue(json1.Contains("\"blendTime\""),
                    "1回目Save: blendTimeがDelta出力に含まれるべき");

                // 4. Revert: デフォルト値に戻す（FromJson経由、RevertAllToDefault相当）
                var defaultJson = ExposedObjectDefaultRegistry.GetDefaults(proxy.exposedObject);
                Assert.IsNotNull(defaultJson, "デフォルトJSONが存在するべき");
                ExposedPropertySerializer.FromJson(defaultJson.ToString(), proxy.exposedObject, _resolver, captureDefaults: false);
                Assert.AreEqual(0.25f, configSO.blendTime, 0.001f, "Revert後にblendTimeが初期値に戻るべき");
                Assert.AreEqual(1, avatarComp.level, "Revert後にlevelが初期値に戻るべき");

                // --- 2回目 Play→Stop（SO値がrevertされた状態から開始）---

                // 5. Shutdown + 再Initialize（Play mode再入シミュレーション）
                proxy.OnDisable();
                proxy.OnEnable();
                var resolved2 = ExposedObjectGraph.ResolveExposedObjects(
                    new object[] { proxy }, _resolver);
                foreach (var obj in resolved2)
                    ExposedPropertyUtility.SetDefault(obj);

                // 6. Load: 1回目で保存したJSONを適用
                ExposedSceneSerializer.SceneFromJson(json1, _resolver);
                Assert.AreEqual(0.8f, configSO.blendTime, 0.001f, "2回目Load後にblendTimeが適用されるべき");

                // 7. Save: Delta保存（2回目）
                var json2 = ExposedSceneSerializer.SceneToJson(
                    resolved2,
                    _resolver, SerializeMode.Delta);

                // Assert: 2回目もobjectsが空にならない
                var jRoot = JObject.Parse(json2);
                var objects = jRoot["objects"] as JArray;
                Assert.IsNotNull(objects);
                Assert.IsTrue(objects.Count > 0,
                    "2回目のDelta保存でobjectsが空にならないべき（連続Play→Stop問題）");
                Assert.IsTrue(json2.Contains("\"blendTime\""),
                    "2回目Save: blendTimeがDelta出力に含まれるべき");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configSO);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        #endregion

        #region Delta保存: ReplaceId後のデフォルト保持

        /// <summary>
        /// SceneFromJsonでReplaceIdが行われた後、Delta保存でオブジェクトが消えないことを確認。
        /// ExposedAsset削除後のPlay→Load→Stop→Save問題の再現テスト。
        /// </summary>
        [Test]
        public void LoadSaveCycle_AfterReplaceId_DeltaSavePreservesObjects()
        {
            // Arrange: プロキシを作成（Initialize時のシミュレーション）
            var proxy = new TestProxy("original-guid-aaa") { value = 50 };
            var originalExposedObject = proxy.exposedObject;
            Assert.IsNotNull(originalExposedObject);

            // デフォルト値をキャプチャ（Container.Initialize相当）
            ExposedPropertyUtility.SetDefault(originalExposedObject);

            // 値を変更してDelta JSON（保存済みデータ）を作成
            proxy.value = 100;
            var savedJson = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances),
                _resolver, SerializeMode.Delta);
            Assert.IsTrue(savedJson.Contains("\"value\": 100"), "保存JSONに変更値が含まれるべき");

            // Play mode再入をシミュレーション:
            // 1. 全インスタンスをクリア
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            // 2. 新しいGUIDでプロキシを再作成（Play mode開始時の状態）
            var newProxy = new TestProxy("new-guid-bbb") { value = 50 }; // デフォルト値に戻る
            var newExposedObject = newProxy.exposedObject;
            Assert.IsNotNull(newExposedObject);

            // 3. デフォルトをキャプチャ（Container.Initialize相当）
            ExposedPropertyUtility.SetDefault(newExposedObject);

            // Act: LoadCurrentData相当 - SceneFromJsonで読み込み
            // _TryResolveByTypeNameでReplaceIdが行われるはず
            ExposedSceneSerializer.SceneFromJson(savedJson, _resolver);

            // Assert: ReplaceId後のExposedObjectが見つかる
            var resolved = ExposedObjectRegistry.FindById("original-guid-aaa");
            Assert.IsNotNull(resolved, "ReplaceId後にsaved IDで検索できるべき");
            Assert.AreEqual(100, ((TestProxy)resolved.target).value, "Load後に保存値が復元されるべき");

            // Act: SaveCurrentData相当 - Delta保存
            var resavedJson = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances),
                _resolver, SerializeMode.Delta);

            // Assert: オブジェクトが空でないこと
            Assert.IsTrue(resavedJson.Contains("\"value\": 100"),
                "ReplaceId後のDelta保存で変更値が保持されるべき（objects:[]にならない）");
            Assert.IsTrue(resavedJson.Contains("original-guid-aaa"),
                "ReplaceId後のIDが保存されるべき");
        }

        /// <summary>
        /// ReplaceId後、ロード値がデフォルトと同一でもDelta保存でオブジェクトが保持されることを確認。
        /// （ScriptableObjectのインライン値がデフォルトと一致するケース）
        /// </summary>
        [Test]
        public void LoadSaveCycle_AfterReplaceId_DefaultValuesStillSaved()
        {
            // Arrange: プロキシをデフォルト値（value=0）で作成
            var proxy = new TestProxy("original-guid-ccc") { value = 0 };
            ExposedPropertyUtility.SetDefault(proxy.exposedObject);

            // デフォルトと異なる値で保存JSONを作成
            proxy.value = 42;
            var savedJson = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances),
                _resolver, SerializeMode.Delta);

            // Play mode再入シミュレーション
            var toRemove = ExposedObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newProxy = new TestProxy("new-guid-ddd") { value = 0 };
            ExposedPropertyUtility.SetDefault(newProxy.exposedObject);

            // Act: Load
            ExposedSceneSerializer.SceneFromJson(savedJson, _resolver);

            // Assert: 値が復元
            var resolved = ExposedObjectRegistry.FindById("original-guid-ccc");
            Assert.IsNotNull(resolved, "ReplaceId後にsaved IDで検索できるべき");

            // Act: Delta保存
            var resavedJson = ExposedSceneSerializer.SceneToJson(
                new List<ExposedObject>(ExposedObjectRegistry.instances),
                _resolver, SerializeMode.Delta);

            // Assert: 値が保持される
            Assert.IsTrue(resavedJson.Contains("\"value\": 42"),
                "ReplaceId後のDelta保存で復元値が保持されるべき");
        }

        #endregion
    }
}
