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
    /// Load+Save時の@ref ID破損・LiveObject増殖バグの回帰テスト。
    /// 3つのバグを検証:
    /// 1. LiveObjectContainer.FindByTarget のnull比較バグ
    /// 2. LiveUnityObjectProxyコンストラクタでの不要なLiveObject生成
    /// 3. LiveObjectRegistry.GetOrCreateがIDパラメータを無視する問題
    /// </summary>
    [TestFixture]
    public class LiveObjectLoadSaveTests
    {
        #region Test Classes

        [Serializable]
        [LiveClass("TestLoadSaveProxy", Icon = "test")]
        public class TestProxy : LiveUnityObjectBase, ILiveObject
        {
            [SerializeField]
            public string _referenceName;

            string ILiveObject.name
            {
                get => _referenceName;
                set => _referenceName = value;
            }

            public override string id => _referenceName;

            [LiveField]
            public int value;

            public TestProxy()
            {
            }

            public TestProxy(string referenceName)
            {
                _referenceName = referenceName;
                _liveObject = LiveObjectRegistry.Create<TestProxy>(this, id);
            }
        }

        #endregion

        private TestLiveObjectResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            LiveClass.Clear();

            LiveClass.RegisterFromAttributes<TestProxy>();

            // LiveObjectRegistry.instances をクリア
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove)
            {
                obj.Unregister();
            }

            _resolver = new TestLiveObjectResolver();
        }

        [TearDown]
        public void TearDown()
        {
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove)
            {
                obj.Unregister();
            }
        }

        #region Bug 1: LiveObjectContainer.FindByTarget null比較

        [Test]
        public void FindByTarget_NullTarget_ReturnsNull()
        {
            // Arrange
            var go = new GameObject("TestContainer");
            var container = new LiveObjectContainer(go.name, new List<ILiveObject>());

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
            var container = new LiveObjectContainer(go.name, new List<ILiveObject>());

            var proxy = new TestProxy("test-id-1");
            container.AddLiveObject(proxy);

            // LiveObjectRegistry.instancesに登録されていないオブジェクトで検索
            var unregisteredTarget = new object();

            try
            {
                // Act: reference==nullのオブジェクトに対して非UnityObjectでFindByTargetしても
                //      null == null でマッチしてはいけない
                var result = container.FindByTarget(unregisteredTarget);

                // Assert: Container内のnull referenceオブジェクトにマッチしない
                // LiveObjectRegistry.FindByTargetにフォールバックするが、未登録なのでnull
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
            var container = new LiveObjectContainer(go.name, new List<ILiveObject>());

            var proxy1 = new TestProxy("id-1");
            var proxy2 = new TestProxy("id-2");
            var proxy3 = new TestProxy("id-3");
            container.AddLiveObject(proxy1);
            container.AddLiveObject(proxy2);
            container.AddLiveObject(proxy3);

            try
            {
                // Act: 全てのプロキシのreferenceはnull（UnityEngine.Objectではない）
                // 別のプロキシで検索しても、null==nullマッチで最初の要素を返してはいけない
                var result = container.FindByTarget(proxy2);

                // Assert: proxy2自体はcontainerのreference比較ではマッチしない
                // LiveObjectRegistry.FindByTargetにフォールバックし、proxy2のLiveObjectを返す
                if (result != null)
                {
                    Assert.AreEqual("id-2", result.Value.id, "正しいIDのLiveObjectが返されるべき");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        #endregion

        #region Bug 2: コンストラクタでの不要なLiveObject生成

        [Test]
        public void LiveGameObject_Constructor_NullReference_DoesNotCreateLiveObject()
        {
            // Arrange & Act: nullでLiveGameObjectを作成（デシリアライズ時のシミュレーション）
            LiveClass.RegisterFromAttributes<LiveGameObject>();

            var initialCount = LiveObjectRegistry.instances.Count;
            var proxy = new LiveGameObject(null);

            // Assert: referenceがnullの場合、LiveObjectは生成されない
            Assert.IsNull(proxy.liveObject, "null referenceでLiveObjectが生成されてはいけない");
            Assert.AreEqual(initialCount, LiveObjectRegistry.instances.Count, "LiveObjectRegistry.instancesに不要なエントリが追加されてはいけない");
        }

        [Test]
        public void LiveGameObject_Constructor_ValidReference_CreatesLiveObject()
        {
            // Arrange
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            var testGo = new GameObject("TestGO");

            try
            {
                var initialCount = LiveObjectRegistry.instances.Count;

                // Act
                var proxy = new LiveGameObject(testGo);

                // Assert: 有効なreferenceの場合はLiveObjectが生成される
                Assert.IsNotNull(proxy.liveObject, "有効なreferenceでLiveObjectが生成されるべき");
                Assert.AreEqual(initialCount + 1, LiveObjectRegistry.instances.Count);
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
            LiveClass.RegisterFromAttributes<TestProxy>();

            var target = new object();
            var firstId = "first-id";
            var liveClass = LiveClass.Find(typeof(TestProxy));

            // 最初のIDでLiveObject作成
            var initial = LiveObjectRegistry.GetOrCreate(firstId, liveClass, target);
            Assert.AreEqual(firstId, initial.id);

            // Act: 異なるIDで再取得を試みる
            var resolved = LiveObjectRegistry.GetOrCreate("different-id", liveClass, target);

            // Assert: IDは変更されず、既存のLiveObjectがそのまま返される（IDは不変）
            Assert.AreEqual(initial, resolved, "同じtargetに対しては既存のLiveObjectが返されるべき");
            Assert.AreEqual(firstId, resolved.id, "IDは最初に設定された値のまま変更されない");
        }

        [Test]
        public void GetOrCreate_ExistingTargetWithSameId_ReturnsSameInstance()
        {
            // Arrange
            var target = new TestProxy("same-id");
            var liveClass = LiveClass.Find(typeof(TestProxy));

            var initial = LiveObjectRegistry.GetOrCreate("same-id", liveClass, target);

            // Act: 同じIDで再度呼び出し
            var result = LiveObjectRegistry.GetOrCreate("same-id", liveClass, target);

            // Assert: 同じインスタンスが返される
            Assert.AreEqual(initial, result, "同じIDの場合は同じインスタンスが返されるべき");
        }

        [Test]
        public void GetOrCreate_SameTargetSameId_ReturnsSameInstance()
        {
            // Arrange: 同じターゲット・同じIDで重複作成しないことを確認
            var target = new object();
            var liveClass = LiveClass.Find(typeof(TestProxy));
            var id = "dedup-test-id";

            var initial = LiveObjectRegistry.GetOrCreate(id, liveClass, target);
            var initialCount = LiveObjectRegistry.instances.Count;

            // Act: 同じターゲット・同じIDで再度呼び出し
            var result = LiveObjectRegistry.GetOrCreate(id, liveClass, target);

            // Assert: 同じインスタンスが返され、instancesは増えない
            Assert.AreEqual(initial, result, "同一ターゲット・同一IDでは同じインスタンスを返すべき");
            Assert.AreEqual(initialCount, LiveObjectRegistry.instances.Count, "instancesが増えてはいけない");
        }

        [Test]
        public void GetOrCreate_DifferentId_SameTarget_ReturnsExistingAndNoNewInstance()
        {
            // Arrange: 同じターゲットで異なるIDのLiveObjectが作られるシナリオ
            var target = new object();
            var liveClass = LiveClass.Find(typeof(TestProxy));

            var initialCount = LiveObjectRegistry.instances.Count;

            // 最初のIDで作成
            var firstId = System.Guid.NewGuid().ToString();
            var first = LiveObjectRegistry.GetOrCreate(firstId, liveClass, target);
            Assert.AreEqual(initialCount + 1, LiveObjectRegistry.instances.Count);

            // Act: 異なるIDで再取得を試みる
            var secondId = "different-id";
            var result = LiveObjectRegistry.GetOrCreate(secondId, liveClass, target);

            // Assert: 既存のLiveObjectがそのまま返される（IDは不変、インスタンス数も変わらない）
            Assert.AreEqual(first, result, "同じtargetに対しては既存インスタンスが返されるべき");
            Assert.AreEqual(firstId, result.id, "IDは最初に設定された値のまま");
            Assert.AreEqual(initialCount + 1, LiveObjectRegistry.instances.Count, "インスタンス数は増えない");
            Assert.IsNull(LiveObjectRegistry.FindById(secondId), "新しいIDでは検索不可（作成されていない）");
        }

        #endregion

        #region Bug 4: @name が null になるバグ

        [Serializable]
        [LiveClass("TestNameFallbackProxy", Icon = "test")]
        public class TestNameFallbackProxy : LiveUnityObjectBase
        {
            [SerializeField]
            public string _referenceName;

            [SerializeField, LiveField, Hide]
            [FormerlyNamedAs("name")]
            private string _fallbackName;

            [LiveProperty]
            public override string name
            {
                get => _fallbackName;
                set => _fallbackName = value;
            }

            public override string id => _referenceName;

            [LiveField]
            public int value;

            public TestNameFallbackProxy()
            {
            }

            public TestNameFallbackProxy(string referenceName, string name = null)
            {
                _referenceName = referenceName;
                _fallbackName = name;
                _liveObject = LiveObjectRegistry.Create<TestNameFallbackProxy>(this, id);
            }
        }

        [Test]
        public void FromJson_AtNameField_IsRestoredToNameProperty()
        {
            // Arrange: @name を含むJSONをデシリアライズし、nameプロパティに復元されることを確認
            LiveClass.RegisterFromAttributes<TestNameFallbackProxy>();

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
            LivePropertySerializer.FromJson(json, proxy.liveObject.Value, _resolver);

            // Assert
            Assert.AreEqual("TestCamera", proxy.name, "@nameがnameプロパティに復元されるべき");
            Assert.AreEqual(42, proxy.value, "通常のプロパティも復元されるべき");
        }

        [Test]
        public void FromJson_AtNameField_DoesNotOverwriteExistingName()
        {
            // Arrange: 既にnameが設定されている場合、@nameで上書きしない
            LiveClass.RegisterFromAttributes<TestNameFallbackProxy>();

            var proxy = new TestNameFallbackProxy("name-existing-id", "ExistingName");
            Assert.AreEqual("ExistingName", proxy.name);

            var json = @"{
                ""@type"": ""TestNameFallbackProxy"",
                ""@id"": ""name-existing-id"",
                ""@name"": ""NewName"",
                ""value"": 10
            }";

            // Act
            LivePropertySerializer.FromJson(json, proxy.liveObject.Value, _resolver);

            // Assert: 既存のnameは上書きされない
            Assert.AreEqual("ExistingName", proxy.name, "既存のnameは@nameで上書きされるべきではない");
        }

        [Test]
        public void LoadSaveCycle_Name_IsPreservedWhenDirty()
        {
            // Arrange: name を dirty にしてから save すれば、Shadow Field 経由の name プロパティとして
            // 通常シリアライズされ、Load で復元されることを確認。@name メタは永続化されない。
            LiveClass.RegisterFromAttributes<TestNameFallbackProxy>();

            var proxy = new TestNameFallbackProxy("name-cycle-id", "InitialName");
            proxy.value = 99;

            LivePropertyUtility.SetDefault(proxy.liveObject.Value);
            proxy.value = 100;
            proxy.name = "Camera"; // name を default から変えて dirty にする

            // Save
            var json1 = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            Assert.IsNotEmpty(json1);

            // 通常プロパティ name が dirty 値で含まれる
            Assert.IsTrue(json1.Contains("\"name\": \"Camera\"") || json1.Contains("\"name\":\"Camera\""),
                "LiveSceneToJson の出力に name プロパティが含まれるべき (dirty時)");

            // クリア
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newProxy = new TestNameFallbackProxy("name-cycle-id", "InitialName");
            LivePropertyUtility.SetDefault(newProxy.liveObject.Value);

            // Load
            LiveSceneSerializer.LiveSceneFromJson(json1, _resolver);

            Assert.AreEqual("Camera", newProxy.name, "Load 後に name プロパティが復元されるべき");
            Assert.AreEqual(100, newProxy.value, "値も復元されるべき");
        }

        #endregion

        #region 統合テスト: Load+Saveサイクル

        [Test]
        public void LoadSaveCycle_RefIds_ArePreserved()
        {
            // Arrange: 複数のLiveObjectを作成してシリアライズ→デシリアライズ→再シリアライズ
            var id1 = "ref-id-aaa";
            var id2 = "ref-id-bbb";
            var id3 = "ref-id-ccc";

            var target1 = new TestProxy(id1) { value = 10 };
            var target2 = new TestProxy(id2) { value = 20 };
            var target3 = new TestProxy(id3) { value = 30 };

            // デフォルト値をキャプチャしてdirty検出を有効化
            LivePropertyUtility.SetDefault(target1.liveObject.Value);
            LivePropertyUtility.SetDefault(target2.liveObject.Value);
            LivePropertyUtility.SetDefault(target3.liveObject.Value);

            // 値を変更してdirtyにする
            target1.value = 100;
            target2.value = 200;
            target3.value = 300;

            // シリアライズ（Save相当）
            var json1 = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);
            Assert.IsNotEmpty(json1);

            // 全インスタンスをクリア（Load前状態のシミュレーション）
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            // 新しいターゲットで再登録（ResolveReferences相当）
            var newTarget1 = new TestProxy(id1);
            var newTarget2 = new TestProxy(id2);
            var newTarget3 = new TestProxy(id3);

            // デシリアライズ（Load相当）
            LiveSceneSerializer.LiveSceneFromJson(json1, _resolver);

            // デフォルト値を再キャプチャ
            if (newTarget1.liveObject != null) LivePropertyUtility.SetDefault(newTarget1.liveObject.Value);
            if (newTarget2.liveObject != null) LivePropertyUtility.SetDefault(newTarget2.liveObject.Value);
            if (newTarget3.liveObject != null) LivePropertyUtility.SetDefault(newTarget3.liveObject.Value);

            // 再シリアライズ（Save相当）
            var json2 = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Delta);

            // Assert: 各IDのLiveObjectが正しいIDを保持している
            var obj1 = LiveObjectRegistry.FindById(id1);
            var obj2 = LiveObjectRegistry.FindById(id2);
            var obj3 = LiveObjectRegistry.FindById(id3);

            Assert.IsNotNull(obj1, $"ID '{id1}' のLiveObjectが存在するべき");
            Assert.IsNotNull(obj2, $"ID '{id2}' のLiveObjectが存在するべき");
            Assert.IsNotNull(obj3, $"ID '{id3}' のLiveObjectが存在するべき");

            // 全てのIDが同一にならないことを確認（Bug1の回帰テスト）
            Assert.AreNotEqual(obj1.Value.id, obj2.Value.id, "異なるオブジェクトのIDが同一になってはいけない");
            Assert.AreNotEqual(obj2.Value.id, obj3.Value.id, "異なるオブジェクトのIDが同一になってはいけない");
        }

        [Test]
        public void GetOrCreate_AfterDeserializationSimulation_KeepsFirstId()
        {
            // Arrange: デシリアライズシミュレーション
            // コンストラクタでLiveObjectが作られ、
            // その後GetOrCreateで別IDで呼ばれても最初のIDが保持されるパターン
            var liveClass = LiveClass.Find(typeof(TestProxy));
            var secondId = "second-id";

            var target = new TestProxy("temp"); // コンストラクタでLiveObject生成
            var firstId = target.liveObject?.id;
            Assert.IsNotNull(firstId, "コンストラクタでLiveObjectが生成されるべき");

            // Act: 異なるIDでGetOrCreate
            var resolved = LiveObjectRegistry.GetOrCreate(secondId, liveClass, target);

            // Assert: 最初のIDが保持される（IDは不変）
            Assert.AreEqual(firstId, resolved.id, "最初に設定されたIDが保持されるべき");
            Assert.AreEqual(1, LiveObjectRegistry.instances.Count(x => ReferenceEquals(x.target, target)),
                "同一ターゲットに対するLiveObjectは1つだけ存在するべき");
        }

        #endregion

        #region LiveSceneToJson includeStatic option

        [LiveClass("TestStaticClass", Icon = "test")]
        public static class TestStaticClass
        {
            [LiveField]
            public static int staticValue = 42;
        }

        [Test]
        public void LiveSceneToJson_ExcludeNone_ContainsStaticObject()
        {
            // Arrange
            var liveClass = LiveClass.RegisterClass(typeof(TestStaticClass));
            Assert.IsNotNull(liveClass);
            Assert.IsTrue(liveClass.isStatic);
            var staticObj = new LiveObjectHandle("static-test", liveClass, null);

            var proxy = new TestProxy("instance-test");

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Snapshot, ExcludeFilter.None);

            // Assert
            Assert.IsTrue(json.Contains("TestStaticClass"), "staticオブジェクトがJSON出力に含まれるべき");
            Assert.IsTrue(json.Contains("TestLoadSaveProxy"), "instanceオブジェクトもJSON出力に含まれるべき");

            staticObj.Unregister();
        }

        [Test]
        public void LiveSceneToJson_ExcludeStatic_ExcludesStaticObject()
        {
            // Arrange
            var liveClass = LiveClass.RegisterClass(typeof(TestStaticClass));
            Assert.IsNotNull(liveClass);
            Assert.IsTrue(liveClass.isStatic);
            var staticObj = new LiveObjectHandle("static-test", liveClass, null);

            var proxy = new TestProxy("instance-test");

            // Act
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver, SerializeMode.Snapshot, ExcludeFilter.Static);

            // Assert
            Assert.IsFalse(json.Contains("TestStaticClass"), "staticオブジェクトはJSON出力から除外されるべき");
            Assert.IsTrue(json.Contains("TestLoadSaveProxy"), "instanceオブジェクトはJSON出力に含まれるべき");

            staticObj.Unregister();
        }

        [Test]
        public void LiveSceneToJson_DefaultExclude_ContainsStaticObject()
        {
            // Arrange
            var liveClass = LiveClass.RegisterClass(typeof(TestStaticClass));
            Assert.IsNotNull(liveClass);
            var staticObj = new LiveObjectHandle("static-test", liveClass, null);

            // Act: デフォルト（exclude省略 = ExcludeFilter.None）
            var json = LiveSceneSerializer.LiveSceneToJson(new List<LiveObjectHandle>(LiveObjectRegistry.instances), _resolver);

            // Assert
            Assert.IsTrue(json.Contains("TestStaticClass"), "デフォルトではstaticオブジェクトがJSON出力に含まれるべき");

            staticObj.Unregister();
        }

        #endregion

        #region Delta保存: ScriptableObjectインライン保存

        /// <summary>
        /// テスト用ScriptableObject（AvatarExpressionConfig相当）
        /// </summary>
        [LiveClass("TestConfig")]
        public class TestConfigSO : ScriptableObject
        {
            [LiveField]
            public float blendTime = 0.25f;

            [LiveField]
            public string configName = "default";
        }

        /// <summary>
        /// テスト用コンポーネント（AvatarController相当）
        /// ScriptableObjectをフィールドとして参照する
        /// </summary>
        [LiveClass("TestAvatar")]
        public class TestAvatarComponent : MonoBehaviour
        {
            [LiveField]
            public TestConfigSO config;

            [LiveField]
            public int level = 1;
        }

        /// <summary>
        /// LiveSceneFromJsonがLiveGameObjectのcomponents経由で
        /// インラインScriptableObjectのプロパティを正しく適用するか検証。
        /// studio.jsonのPlay→Load→Stop→Saveで objects:[] になる問題の根本原因テスト。
        /// </summary>
        [Test]
        public void LiveSceneFromJson_InlineScriptableObject_AppliesValues()
        {
            LiveClass.RegisterFromAttributes<TestConfigSO>();
            LiveClass.RegisterFromAttributes<TestAvatarComponent>();
            LiveClass.RegisterFromAttributes<LiveGameObject>();

            var go = new GameObject("Test Avatar");
            var avatarComp = go.AddComponent<TestAvatarComponent>();
            var configSO = ScriptableObject.CreateInstance<TestConfigSO>();
            configSO.blendTime = 0.7f; // 初期値
            configSO.configName = "initial";
            avatarComp.config = configSO;
            avatarComp.level = 1;

            try
            {
                var proxy = new LiveGameObject(go);
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
                LiveSceneSerializer.LiveSceneFromJson(loadJson, _resolver);

                // Assert: コンポーネントのプロパティが更新されている
                Assert.AreEqual(5, avatarComp.level,
                    "LiveSceneFromJsonでcomponent直接プロパティが適用されるべき");
                Assert.AreEqual(0.25f, configSO.blendTime, 0.001f,
                    "LiveSceneFromJsonでインラインSO内のプロパティが適用されるべき");
                Assert.AreEqual("modified", configSO.configName,
                    "LiveSceneFromJsonでインラインSO内の文字列プロパティが適用されるべき");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configSO);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// IDが異なりReplaceIdが行われるケースで、LiveSceneFromJsonが
        /// components経由のインラインScriptableObjectプロパティを正しく適用するか検証。
        /// Play mode再入時のGUID再生成シナリオ。
        /// </summary>
        [Test]
        public void LiveSceneFromJson_AfterReplaceId_InlineScriptableObject_AppliesValues()
        {
            LiveClass.RegisterFromAttributes<TestConfigSO>();
            LiveClass.RegisterFromAttributes<TestAvatarComponent>();
            LiveClass.RegisterFromAttributes<LiveGameObject>();

            var go = new GameObject("Test Avatar");
            var avatarComp = go.AddComponent<TestAvatarComponent>();
            var configSO = ScriptableObject.CreateInstance<TestConfigSO>();
            configSO.blendTime = 0.7f;
            configSO.configName = "initial";
            avatarComp.config = configSO;
            avatarComp.level = 1;

            try
            {
                var proxy = new LiveGameObject(go);
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
                LiveSceneSerializer.LiveSceneFromJson(loadJson, _resolver);

                // Assert: ReplaceIdで解決された
                var resolved = LiveObjectRegistry.FindById(savedId);
                Assert.IsNotNull(resolved, "ReplaceId後にsaved IDで検索できるべき");

                // Assert: 値が適用されている
                Assert.AreEqual(5, avatarComp.level,
                    "ReplaceId後のLiveSceneFromJsonでlevelが適用されるべき");
                Assert.AreEqual(0.25f, configSO.blendTime, 0.001f,
                    "ReplaceId後のLiveSceneFromJsonでインラインSO内blendTimeが適用されるべき");
                Assert.AreEqual("modified", configSO.configName,
                    "ReplaceId後のLiveSceneFromJsonでインラインSO内configNameが適用されるべき");
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
        public void LiveSceneFromJson_WithEmptyComponentInJson_AppliesValues()
        {
            LiveClass.RegisterFromAttributes<TestConfigSO>();
            LiveClass.RegisterFromAttributes<TestAvatarComponent>();
            LiveClass.RegisterFromAttributes<LiveGameObject>();

            // 2つのLiveClassコンポーネントを持つGameObject
            var go = new GameObject("Test Avatar");
            var avatarComp = go.AddComponent<TestAvatarComponent>();
            var configSO = ScriptableObject.CreateInstance<TestConfigSO>();
            configSO.blendTime = 0.7f;
            avatarComp.config = configSO;
            avatarComp.level = 1;

            // 2つ目のLiveClassコンポーネント（InputActions相当）
            // TestProxy extends LiveUnityObjectBaseなのでMonoBehaviourではない
            // 代わりにTestAvatarComponentをもう1つ追加して2つ目のコンポーネントをシミュレート
            // 注: 実際はAvatarInputだが、ここでは型を増やさないため省略

            try
            {
                var proxy = new LiveGameObject(go);
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
                LiveSceneSerializer.LiveSceneFromJson(loadJson, _resolver);

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
        /// LiveObjectContainerをリゾルバーとして使用するケースで、
        /// ReplaceId後のLiveSceneFromJsonが正しく値を適用するか検証。
        /// 実際のRemoteControlProviderと同じフロー。
        /// </summary>
        [Test]
        public void LiveSceneFromJson_WithContainer_AfterReplaceId_AppliesValues()
        {
            LiveClass.RegisterFromAttributes<TestConfigSO>();
            LiveClass.RegisterFromAttributes<TestAvatarComponent>();
            LiveClass.RegisterFromAttributes<LiveGameObject>();
            LiveClass.RegisterFromAttributes<LiveObjectContainer>();

            var containerGo = new GameObject("Container");
            var container = new LiveObjectContainer(containerGo.name, new List<ILiveObject>());

            var avatarGo = new GameObject("Test Avatar");
            var avatarComp = avatarGo.AddComponent<TestAvatarComponent>();
            var configSO = ScriptableObject.CreateInstance<TestConfigSO>();
            configSO.blendTime = 0.7f;
            configSO.configName = "initial";
            avatarComp.config = configSO;
            avatarComp.level = 1;

            try
            {
                // LiveGameObjectをContainerに登録（Initialize相当）
                var proxy = new LiveGameObject(avatarGo);
                container.AddLiveObject(proxy);
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

                // Act: ContainerをリゾルバーとしてLiveSceneFromJson
                LiveSceneSerializer.LiveSceneFromJson(loadJson, container);

                // Assert: 値が適用されている
                Assert.AreEqual(5, avatarComp.level,
                    "Container経由のLiveSceneFromJsonでlevelが適用されるべき");
                Assert.AreEqual(0.25f, configSO.blendTime, 0.001f,
                    "Container経由のLiveSceneFromJsonでインラインSO内blendTimeが適用されるべき");
                Assert.AreEqual("modified", configSO.configName,
                    "Container経由のLiveSceneFromJsonでインラインSO内configNameが適用されるべき");
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
            LiveClass.RegisterFromAttributes<TestConfigSO>();
            LiveClass.RegisterFromAttributes<TestAvatarComponent>();
            LiveClass.RegisterFromAttributes<LiveGameObject>();

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
                // LiveGameObject プロキシを作成（Container登録相当）
                var proxy = new LiveGameObject(go);
                proxy.OnEnable();
                Assert.IsNotNull(proxy.liveObject, "LiveGameObject should have LiveObjectHandle");

                // デフォルト値をキャプチャ（Container.Initialize相当）
                // inline children（コンポーネント・ScriptableObject）の defaults も
                // 登録しないと pending delta で差分が検出できない。
                var initialResolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { proxy }, _resolver);
                foreach (var obj in initialResolved)
                    LivePropertyUtility.SetDefault(obj);

                // 値を変更してDelta JSON（保存済みデータ）を作成
                configSO.blendTime = 0.5f;
                configSO.configName = "modified";
                avatarComp.level = 5;

                var savedJson = LiveSceneSerializer.LiveSceneToJson(
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
                Assert.IsNotNull(proxy.liveObject);

                // デフォルトを再キャプチャ（値はデフォルトに戻っている）
                var reResolved = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { proxy }, _resolver);
                foreach (var obj in reResolved)
                    LivePropertyUtility.SetDefault(obj);

                // Act: LoadCurrentData相当 - LiveSceneFromJsonで読み込み
                LiveSceneSerializer.LiveSceneFromJson(savedJson, _resolver);

                // Assert: 値が復元されている
                Assert.AreEqual(5, avatarComp.level, "Load後にlevelが復元されるべき");
                Assert.AreEqual(0.5f, configSO.blendTime, 0.001f, "Load後にblendTimeが復元されるべき");
                Assert.AreEqual("modified", configSO.configName, "Load後にconfigNameが復元されるべき");

                // Act: SaveCurrentData相当 - Delta保存
                var resavedJson = LiveSceneSerializer.LiveSceneToJson(
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
            LiveClass.RegisterFromAttributes<TestConfigSO>();
            LiveClass.RegisterFromAttributes<TestAvatarComponent>();
            LiveClass.RegisterFromAttributes<LiveGameObject>();

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
                var proxy = new LiveGameObject(go);
                proxy.OnEnable();
                var resolved1 = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { proxy }, _resolver);
                foreach (var obj in resolved1)
                    LivePropertyUtility.SetDefault(obj);

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
                LiveSceneSerializer.LiveSceneFromJson(savedJson, _resolver);
                Assert.AreEqual(0.8f, configSO.blendTime, 0.001f, "1回目Load後にblendTimeが適用されるべき");

                // 3. Save: Delta保存
                var json1 = LiveSceneSerializer.LiveSceneToJson(
                    resolved1,
                    _resolver, SerializeMode.Delta);
                Assert.IsTrue(json1.Contains("\"blendTime\""),
                    "1回目Save: blendTimeがDelta出力に含まれるべき");

                // 4. Revert: デフォルト値に戻す（FromJson経由、RevertAllToDefault相当）
                var defaultJson = LiveObjectDefaultRegistry.GetDefaults(proxy.liveObject.Value);
                Assert.IsNotNull(defaultJson, "デフォルトJSONが存在するべき");
                LivePropertySerializer.FromJson(defaultJson.ToString(), proxy.liveObject.Value, _resolver, captureDefaults: false);
                Assert.AreEqual(0.25f, configSO.blendTime, 0.001f, "Revert後にblendTimeが初期値に戻るべき");
                Assert.AreEqual(1, avatarComp.level, "Revert後にlevelが初期値に戻るべき");

                // --- 2回目 Play→Stop（SO値がrevertされた状態から開始）---

                // 5. Shutdown + 再Initialize（Play mode再入シミュレーション）
                proxy.OnDisable();
                proxy.OnEnable();
                var resolved2 = LiveObjectGraph.ResolveLiveObjects(
                    new object[] { proxy }, _resolver);
                foreach (var obj in resolved2)
                    LivePropertyUtility.SetDefault(obj);

                // 6. Load: 1回目で保存したJSONを適用
                LiveSceneSerializer.LiveSceneFromJson(json1, _resolver);
                Assert.AreEqual(0.8f, configSO.blendTime, 0.001f, "2回目Load後にblendTimeが適用されるべき");

                // 7. Save: Delta保存（2回目）
                var json2 = LiveSceneSerializer.LiveSceneToJson(
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
        /// LiveSceneFromJsonでReplaceIdが行われた後、Delta保存でオブジェクトが消えないことを確認。
        /// LiveAsset削除後のPlay→Load→Stop→Save問題の再現テスト。
        /// </summary>
        [Test]
        public void LoadSaveCycle_AfterReplaceId_DeltaSavePreservesObjects()
        {
            // Arrange: プロキシを作成（Initialize時のシミュレーション）
            var proxy = new TestProxy("original-guid-aaa") { value = 50 };
            var originalLiveObject = proxy.liveObject.Value;
            Assert.IsNotNull(proxy.liveObject);

            // デフォルト値をキャプチャ（Container.Initialize相当）
            LivePropertyUtility.SetDefault(originalLiveObject);

            // 値を変更してDelta JSON（保存済みデータ）を作成
            proxy.value = 100;
            var savedJson = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances),
                _resolver, SerializeMode.Delta);
            Assert.IsTrue(savedJson.Contains("\"value\": 100"), "保存JSONに変更値が含まれるべき");

            // Play mode再入をシミュレーション:
            // 1. 全インスタンスをクリア
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            // 2. 新しいGUIDでプロキシを再作成（Play mode開始時の状態）
            var newProxy = new TestProxy("new-guid-bbb") { value = 50 }; // デフォルト値に戻る
            var newLiveObject = newProxy.liveObject.Value;
            Assert.IsNotNull(newProxy.liveObject);

            // 3. デフォルトをキャプチャ（Container.Initialize相当）
            LivePropertyUtility.SetDefault(newLiveObject);

            // Act: LoadCurrentData相当 - LiveSceneFromJsonで読み込み
            // _TryResolveByTypeNameでReplaceIdが行われるはず
            LiveSceneSerializer.LiveSceneFromJson(savedJson, _resolver);

            // Assert: ReplaceId後のLiveObjectが見つかる
            var resolved = LiveObjectRegistry.FindById("original-guid-aaa");
            Assert.IsNotNull(resolved, "ReplaceId後にsaved IDで検索できるべき");
            Assert.AreEqual(100, ((TestProxy)resolved.Value.target).value, "Load後に保存値が復元されるべき");

            // Act: SaveCurrentData相当 - Delta保存
            var resavedJson = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances),
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
            LivePropertyUtility.SetDefault(proxy.liveObject.Value);

            // デフォルトと異なる値で保存JSONを作成
            proxy.value = 42;
            var savedJson = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances),
                _resolver, SerializeMode.Delta);

            // Play mode再入シミュレーション
            var toRemove = LiveObjectRegistry.instances.ToList();
            foreach (var obj in toRemove) obj.Unregister();

            var newProxy = new TestProxy("new-guid-ddd") { value = 0 };
            LivePropertyUtility.SetDefault(newProxy.liveObject.Value);

            // Act: Load
            LiveSceneSerializer.LiveSceneFromJson(savedJson, _resolver);

            // Assert: 値が復元
            var resolved = LiveObjectRegistry.FindById("original-guid-ccc");
            Assert.IsNotNull(resolved, "ReplaceId後にsaved IDで検索できるべき");

            // Act: Delta保存
            var resavedJson = LiveSceneSerializer.LiveSceneToJson(
                new List<LiveObjectHandle>(LiveObjectRegistry.instances),
                _resolver, SerializeMode.Delta);

            // Assert: 値が保持される
            Assert.IsTrue(resavedJson.Contains("\"value\": 42"),
                "ReplaceId後のDelta保存で復元値が保持されるべき");
        }

        #endregion
    }
}
