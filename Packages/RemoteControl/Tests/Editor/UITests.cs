// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.TestTools;
using Lilium.RemoteControl;
using Lilium.RemoteControl.UI;

namespace Lilium.RemoteControl.Tests
{
    [LiveClass(Icon = "settings")]
    internal static class NavigateSelectorTestPage
    {
        [LiveProperty]
        public static float value { get => 1f; set { } }
    }

    [TestFixture]
    public class UITests
    {
        [SetUp]
        public void Setup()
        {
            LiveClass.Clear();

            // Nor may it leave objects in the registry. Several tests here assert on what the
            // registry does *not* hold -- the fallback to the live class when nothing is registered
            // -- so an object left behind by an earlier test does not fail that test, it fails a
            // later one, and which one depends on the order the run happened to take.
            LiveObjectRegistry.ClearAll();

            // A test that failed before its cleanup must not leave prefabs on offer for the next one.
            LivePrefabCatalog.Clear();
        }

        #region MenuItem Tests

        [Test]
        public void MenuItem_DefaultPosition_IsMain()
        {
            var item = new MenuItem();
            Assert.AreEqual(MenuItemPosition.Main, item.position);
        }

        [Test]
        public void MenuItem_DefaultOrder_IsZero()
        {
            var item = new MenuItem();
            Assert.AreEqual(0, item.order);
        }

        [Test]
        public void MenuItem_SetProperties_RetainsValues()
        {
            var item = new MenuItem
            {
                id = "test-menu",
                icon = "settings",
                label = "Test Label",
                position = MenuItemPosition.Bottom,
                order = 5
            };

            Assert.AreEqual("test-menu", item.id);
            Assert.AreEqual("settings", item.icon);
            Assert.AreEqual("Test Label", item.label);
            Assert.AreEqual(MenuItemPosition.Bottom, item.position);
            Assert.AreEqual(5, item.order);
        }

        [Test]
        public void MenuItem_PageAssignment_CategoryPage()
        {
            var item = new MenuItem
            {
                id = "cat-page",
                page = new CategoryPage()
            };

            Assert.IsNotNull(item.page);
            Assert.IsInstanceOf<CategoryPage>(item.page);
        }

        #endregion

        #region CategoryPage Tests

        [Test]
        public void CategoryPage_DefaultSelector_IsStandardObjectSelector()
        {
            var page = new CategoryPage();
            Assert.IsNotNull(page.selector);
            Assert.IsInstanceOf<StandardObjectSelector>(page.selector);
        }

        [Test]
        public void CategoryPage_DefaultFactory_IsStandardObjectFactory()
        {
            var page = new CategoryPage();
            Assert.IsNotNull(page.factory);
            Assert.IsInstanceOf<StandardObjectFactory>(page.factory);
        }

        #endregion

        #region StandardObjectSelector Tests

        [Test]
        public void StandardObjectSelector_NullCategory_ReturnsEmptyArray()
        {
            var selector = new StandardObjectSelector();
            Assert.AreEqual(0, selector.objects.Length);
        }

        [Test]
        public void StandardObjectSelector_EmptyCategory_ReturnsEmptyArray()
        {
            var selector = new StandardObjectSelector { category = "" };
            Assert.AreEqual(0, selector.objects.Length);
        }

        [Test]
        public void StandardObjectSelector_NoMatchingObjects_ReturnsEmptyArray()
        {
            var selector = new StandardObjectSelector { category = "nonexistent-category" };
            Assert.AreEqual(0, selector.objects.Length);
        }

        #endregion

        #region StandardObjectFactory Tests

        /// <summary>
        /// Applies a live class asset offering one factory per prefab, all under the same category.
        /// The caller disposes it with <see cref="_DropCatalogAsset"/>.
        /// </summary>
        static LiveClassAsset _ApplyCatalogAsset(string category, params GameObject[] prefabs)
        {
            var asset = ScriptableObject.CreateInstance<LiveClassAsset>();
            for (int i = 0; i < prefabs.Length; i++)
            {
                asset.prefabs.Add(new LiveClassAsset.PrefabDefinition
                {
                    category = category,
                    factory = new LiveGameObjectFactory { prefab = prefabs[i] },
                });
            }
            LivePrefabCatalog.Register(asset);
            return asset;
        }

        static void _DropCatalogAsset(LiveClassAsset asset)
        {
            LivePrefabCatalog.Clear();
            if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
        }

        /// <summary>A factory on a page showing <paramref name="category"/> (empty: the scene page).</summary>
        static StandardObjectFactory _PageFactory(string category = null)
        {
            var factory = new StandardObjectFactory();
            factory.Initialize(null, category);
            return factory;
        }

        [Test]
        public void StandardObjectFactory_EmptyCatalog_ReturnsEmptyObjects()
        {
            Assert.AreEqual(0, _PageFactory().objects.Length);
        }

        [Test]
        public void StandardObjectFactory_EmptyCatalog_ReturnsEmptyNames()
        {
            Assert.AreEqual(0, _PageFactory().objectNames.Length);
        }

        [Test]
        public void StandardObjectFactory_DeclaredPrefabs_ReturnsCorrectObjects()
        {
            var go1 = new GameObject("Prefab1");
            var go2 = new GameObject("Prefab2");
            LiveClassAsset asset = null;
            try
            {
                asset = _ApplyCatalogAsset(null, go1, go2);
                var objects = _PageFactory().objects;

                Assert.AreEqual(2, objects.Length);
                Assert.AreEqual(asset.prefabs[0].factory, objects[0]);
                Assert.AreEqual(asset.prefabs[1].factory, objects[1]);
            }
            finally
            {
                _DropCatalogAsset(asset);
                GameObject.DestroyImmediate(go1);
                GameObject.DestroyImmediate(go2);
            }
        }

        [Test]
        public void StandardObjectFactory_DeclaredPrefabs_ReturnsCorrectNames()
        {
            var go1 = new GameObject("Alpha");
            var go2 = new GameObject("Beta");
            LiveClassAsset asset = null;
            try
            {
                asset = _ApplyCatalogAsset(null, go1, go2);
                var names = _PageFactory().objectNames;

                Assert.AreEqual(2, names.Length);
                Assert.AreEqual("Alpha", names[0]);
                Assert.AreEqual("Beta", names[1]);
            }
            finally
            {
                _DropCatalogAsset(asset);
                GameObject.DestroyImmediate(go1);
                GameObject.DestroyImmediate(go2);
            }
        }

        /// <summary>
        /// A declaration with no maker is dropped at registration rather than listed as a blank
        /// entry, so an index into the list always names something that can be created.
        /// </summary>
        [Test]
        public void StandardObjectFactory_DeclarationWithoutFactory_IsNotOffered()
        {
            var go = new GameObject("Valid");
            LiveClassAsset asset = null;
            try
            {
                asset = _ApplyCatalogAsset(null, go);
                asset.prefabs.Add(new LiveClassAsset.PrefabDefinition());
                LivePrefabCatalog.Register(asset);

                var names = _PageFactory().objectNames;

                Assert.AreEqual(1, names.Length);
                Assert.AreEqual("Valid", names[0]);
            }
            finally
            {
                _DropCatalogAsset(asset);
                GameObject.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// The category on a declaration decides which category page offers it; the scene page
        /// (no category) offers every declared prefab.
        /// </summary>
        [Test]
        public void StandardObjectFactory_Category_SelectsWhichPageOffersThePrefab()
        {
            var camera = new GameObject("CameraPrefab");
            var prop = new GameObject("PropPrefab");
            LiveClassAsset cameraAsset = null;
            LiveClassAsset propAsset = null;
            try
            {
                cameraAsset = _ApplyCatalogAsset("Camera", camera);
                propAsset = _ApplyCatalogAsset("Prop", prop);

                CollectionAssert.AreEqual(
                    new[] { "CameraPrefab" }, _PageFactory("Camera").objectNames);
                CollectionAssert.AreEqual(
                    new[] { "PropPrefab" }, _PageFactory("Prop").objectNames);
                CollectionAssert.AreEqual(
                    new[] { "CameraPrefab", "PropPrefab" }, _PageFactory().objectNames);
            }
            finally
            {
                _DropCatalogAsset(cameraAsset);
                if (propAsset != null) UnityEngine.Object.DestroyImmediate(propAsset);
                GameObject.DestroyImmediate(camera);
                GameObject.DestroyImmediate(prop);
            }
        }

        /// <summary>An asset that is dropped stops offering what it declared.</summary>
        [Test]
        public void StandardObjectFactory_UnregisteredAsset_NoLongerOffersItsPrefabs()
        {
            var go = new GameObject("Gone");
            LiveClassAsset asset = null;
            try
            {
                asset = _ApplyCatalogAsset(null, go);
                Assert.AreEqual(1, _PageFactory().objectNames.Length);

                LivePrefabCatalog.Unregister(asset);
                Assert.AreEqual(0, _PageFactory().objectNames.Length);
            }
            finally
            {
                _DropCatalogAsset(asset);
                GameObject.DestroyImmediate(go);
            }
        }

        [Test]
        public void StandardObjectFactory_CreateObject_InvalidIndex_DoesNotThrow()
        {
            var factory = _PageFactory();
            Assert.DoesNotThrow(() => factory.CreateObject(-1));
            Assert.DoesNotThrow(() => factory.CreateObject(0));
            Assert.DoesNotThrow(() => factory.CreateObject(100));
        }

        [Test]
        public void StandardObjectFactory_DestroyObject_UnknownId_DoesNotThrow()
        {
            var factory = new StandardObjectFactory();
            Assert.DoesNotThrow(() => factory.DestroyObject("nonexistent-id"));
        }

        [Test]
        public void StandardObjectFactory_DestroyObject_ValidId_Destroys()
        {
            var undoGroup = UnityEditor.Undo.GetCurrentGroup();
            var instance = new GameObject("DestroyTestPrefab(Clone)");
            try
            {
                LiveClass.Register<GameObject>("DestroyTest", new LivePropertyDefine[0]);
                var liveClass = LiveClass.Find(typeof(GameObject));
                var liveObj = new LiveObjectHandle("destroy-test-1", liveClass, instance);

                var factory = new StandardObjectFactory();
                factory.DestroyObject("destroy-test-1");

                // LiveObjectが解除されたことを確認
                Assert.IsNull(LiveObjectRegistry.FindById("destroy-test-1"));
            }
            finally
            {
                // DestroyImmediate が呼ばれていない場合のフォールバック
                if (instance != null)
                    GameObject.DestroyImmediate(instance);

                // Undoスタックに残らないようにクリア
                UnityEditor.Undo.RevertAllDownToGroup(undoGroup);
            }
        }

        /// <summary>
        /// An object held by a source -- which is where a live scene puts what it stands up, the
        /// list belonging to the container that restored it -- is deletable: it leaves that list,
        /// is unregistered, and its GameObject goes with it.
        ///
        /// It used to be looked for in the container's own list only, so it fell through to the id
        /// lookup, which unregistered it and left both the entry and the GameObject standing: the
        /// object came straight back in the next listing and could never be deleted.
        /// </summary>
        [Test]
        public void StandardObjectFactory_DestroyObject_RemovesAnObjectHeldByASource()
        {
            var undoGroup = UnityEditor.Undo.GetCurrentGroup();
            var instance = new GameObject("RestoredInstance");
            LiveObjectContainer container = null;
            try
            {
                LiveClass.RegisterFromAttributes<LiveGameObjectWithTransform>();
                LiveClass.RegisterFromAttributes<LiveObjectContainer>();

                container = new LiveObjectContainer("SourceProbe", new List<ILiveObject>());
                container.Initialize();

                // The source is initialized while still empty, the way a scene's container is
                // before the live scene is restored into it: an object appended afterwards is not
                // one of the scene's own, so it is not persistent and may be deleted.
                var sourceList = new List<ILiveObject>();
                var owner = new object();
                container.AddSource(sourceList, owner);
                container.InitializeSource(owner);

                var restored = new LiveGameObjectWithTransform(instance);
                sourceList.Add(restored);
                restored.OnEnable();

                var id = restored.liveObject?.id;
                Assert.IsFalse(string.IsNullOrEmpty(id), "Precondition: the restored object is registered");

                var factory = new StandardObjectFactory();
                factory.Initialize(container, null);
                factory.DestroyObject(id);

                Assert.AreEqual(0, sourceList.Count, "The source list should no longer hold the object");
                Assert.IsNull(LiveObjectRegistry.FindById(id), "The object should be unregistered");
                Assert.IsTrue(instance == null, "The GameObject should be destroyed with it");
            }
            finally
            {
                container?.Shutdown();
                if (instance != null) GameObject.DestroyImmediate(instance);
                UnityEditor.Undo.RevertAllDownToGroup(undoGroup);
            }
        }

        /// <summary>
        /// An object in no container at all is still deleted whole: the id lookup finds the wrapper
        /// and has to ask it for the scene object it drives, rather than only accepting a target
        /// that is itself a GameObject or a Component.
        /// </summary>
        [Test]
        public void StandardObjectFactory_DestroyObject_UnwrapsAWrapperFoundById()
        {
            var undoGroup = UnityEditor.Undo.GetCurrentGroup();
            var instance = new GameObject("OrphanInstance");
            try
            {
                LiveClass.RegisterFromAttributes<LiveGameObjectWithTransform>();

                var orphan = new LiveGameObjectWithTransform(instance);
                orphan.OnEnable();
                var id = orphan.liveObject?.id;
                Assert.IsFalse(string.IsNullOrEmpty(id), "Precondition: the object is registered");

                _PageFactory().DestroyObject(id);

                Assert.IsNull(LiveObjectRegistry.FindById(id), "The object should be unregistered");
                Assert.IsTrue(instance == null, "Its GameObject should be destroyed too");
            }
            finally
            {
                if (instance != null) GameObject.DestroyImmediate(instance);
                UnityEditor.Undo.RevertAllDownToGroup(undoGroup);
            }
        }

        [Test]
        public void LivePrefabCatalog_RegisterNullAsset_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => LivePrefabCatalog.Register(null));
            Assert.DoesNotThrow(() => LivePrefabCatalog.Unregister(null));
        }

        /// <summary>
        /// Applying an asset also puts every declared prefab in the PrefabRegistry, which is what
        /// lets a saved @prefab entry be rebuilt.
        /// </summary>
        [Test]
        public void LivePrefabCatalog_Register_RegistersDeclaredPrefabs()
        {
            var go1 = new GameObject("RegPrefab1");
            var go2 = new GameObject("RegPrefab2");
            LiveClassAsset asset = null;
            try
            {
                // Asset でない生成 GameObject は GUID を持てないため、Warning が出ることを期待する。
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*RegPrefab1.*no guid.*"));
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*RegPrefab2.*no guid.*"));
                Assert.DoesNotThrow(() => asset = _ApplyCatalogAsset(null, go1, go2));
            }
            finally
            {
                _DropCatalogAsset(asset);
                GameObject.DestroyImmediate(go1);
                GameObject.DestroyImmediate(go2);
            }
        }

        #endregion

        #region ObjectSelectorBase / ObjectFactoryBase Tests

        [Test]
        public void ObjectSelectorBase_DefaultGetObjects_ReturnsEmptyArray()
        {
            var selector = new ObjectSelectorBase();
            Assert.AreEqual(0, selector.objects.Length);
        }

        [Test]
        public void ObjectFactoryBase_DefaultGetObjects_ReturnsEmptyArray()
        {
            var factory = new ObjectFactoryBase();
            Assert.AreEqual(0, factory.objects.Length);
        }

        [Test]
        public void ObjectFactoryBase_DefaultGetObjectNames_ReturnsEmptyArray()
        {
            var factory = new ObjectFactoryBase();
            Assert.AreEqual(0, factory.objectNames.Length);
        }

        [Test]
        public void ObjectFactoryBase_DefaultCreateObject_DoesNotThrow()
        {
            var factory = new ObjectFactoryBase();
            Assert.DoesNotThrow(() => factory.CreateObject(0));
        }

        [Test]
        public void ObjectFactoryBase_DefaultDestroyObject_DoesNotThrow()
        {
            var factory = new ObjectFactoryBase();
            Assert.DoesNotThrow(() => factory.DestroyObject("any-id"));
        }

        #endregion

        #region UIDefinition Tests

        [Test]
        public void UIDefinition_DefaultMenuItems_IsEmptyList()
        {
            var definition = ScriptableObject.CreateInstance<UIDefinition>();
            try
            {
                Assert.IsNotNull(definition.menuItems);
                Assert.AreEqual(0, definition.menuItems.Count);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(definition);
            }
        }

        [Test]
        public void UIDefinition_AddMenuItems_RetainsItems()
        {
            var definition = ScriptableObject.CreateInstance<UIDefinition>();
            try
            {
                definition.menuItems.Add(new MenuItem { id = "page1", label = "Page 1" });
                definition.menuItems.Add(new MenuItem { id = "page2", label = "Page 2" });

                Assert.AreEqual(2, definition.menuItems.Count);
                Assert.AreEqual("page1", definition.menuItems[0].id);
                Assert.AreEqual("page2", definition.menuItems[1].id);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(definition);
            }
        }

        #endregion

        [Test]
        public void TheCreateButton_IsOffTheLiveData()
        {
            // What a "+" produces is carried by the inventory: the spawned object names the prefab it
            // came from and the structure lane rebuilds it under its recorded id. Recording the press
            // as well stands up a second one -- the call takes an index, so replaying it mints a fresh
            // id next to the one the inventory already rebuilt.
            LiveClass.RegisterFromAttributes<ObjectFactoryBase>();
            var liveClass = LiveClass.Find(typeof(ObjectFactoryBase));

            Assert.IsNotNull(liveClass);
            Assert.AreEqual(FrameLane.None, _LaneOf(liveClass, nameof(ObjectFactoryBase.CreateObject)));
        }

        [Test]
        public void TheDeleteButton_StaysOnTheLiveData()
        {
            // Unlike the "+": it is addressed by an id that means the same thing on replay, applying
            // it twice is harmless, and it reaches what the inventory cannot -- the structure lane
            // only takes away what it stood up itself.
            LiveClass.RegisterFromAttributes<ObjectFactoryBase>();
            var liveClass = LiveClass.Find(typeof(ObjectFactoryBase));

            Assert.IsNotNull(liveClass);
            Assert.AreEqual(FrameLane.Event, _LaneOf(liveClass, nameof(ObjectFactoryBase.DestroyObject)));
        }

        private static FrameLane _LaneOf(LiveClass liveClass, string methodName)
        {
            var functions = liveClass.functionTypes;
            for (int i = 0; i < functions.Length; i++)
            {
                if (functions[i].methodInfo?.Name == methodName) return functions[i].lane;
            }

            Assert.Fail($"{methodName} is not exposed at all");
            return FrameLane.Event;
        }

        #region UIHandler Registration Tests

        [Test]
        public void UIHandler_RegistersSelectorsForCategoryPages()
        {
            LiveClass.RegisterFromAttributes<ObjectSelectorBase>();
            LiveClass.RegisterFromAttributes<ObjectFactoryBase>();

            var definition = ScriptableObject.CreateInstance<UIDefinition>();
            try
            {
                definition.menuItems.Add(new MenuItem
                {
                    id = "test-page",
                    label = "Test",
                    page = new CategoryPage()
                });

                // UIHandler のコンストラクタで selector が LiveObjectHandle として登録される
                var handler = new UIHandler(null, definition);

                var selectorObj = LiveObjectRegistry.FindById("ui.selector.test-page");
                Assert.IsNotNull(selectorObj, "Selector should be registered as LiveObjectHandle");

                var factoryObj = LiveObjectRegistry.FindById("ui.factory.test-page");
                Assert.IsNotNull(factoryObj, "Factory should be registered as LiveObjectHandle");

                handler.Cleanup();
            }
            finally
            {
                ScriptableObject.DestroyImmediate(definition);
            }
        }

        [Test]
        public void UIHandler_Cleanup_UnregistersLiveObjects()
        {
            LiveClass.RegisterFromAttributes<ObjectSelectorBase>();
            LiveClass.RegisterFromAttributes<ObjectFactoryBase>();

            var definition = ScriptableObject.CreateInstance<UIDefinition>();
            try
            {
                definition.menuItems.Add(new MenuItem
                {
                    id = "cleanup-page",
                    label = "Cleanup",
                    page = new CategoryPage()
                });

                var handler = new UIHandler(null, definition);

                // 登録を確認
                Assert.IsNotNull(LiveObjectRegistry.FindById("ui.selector.cleanup-page"));
                Assert.IsNotNull(LiveObjectRegistry.FindById("ui.factory.cleanup-page"));

                // Cleanup
                handler.Cleanup();

                // 解除を確認
                Assert.IsNull(LiveObjectRegistry.FindById("ui.selector.cleanup-page"));
                Assert.IsNull(LiveObjectRegistry.FindById("ui.factory.cleanup-page"));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(definition);
            }
        }

        [Test]
        public void UIHandler_NullDefinition_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                var handler = new UIHandler(null, null);
                handler.Cleanup();
            });
        }

        [Test]
        public void UIHandler_EmptyDefinition_DoesNotThrow()
        {
            var definition = ScriptableObject.CreateInstance<UIDefinition>();
            try
            {
                Assert.DoesNotThrow(() =>
                {
                    var handler = new UIHandler(null, definition);
                    handler.Cleanup();
                });
            }
            finally
            {
                ScriptableObject.DestroyImmediate(definition);
            }
        }

        [Test]
        public void UIHandler_MenuItemWithoutPage_DoesNotThrow()
        {
            LiveClass.RegisterFromAttributes<ObjectSelectorBase>();

            var definition = ScriptableObject.CreateInstance<UIDefinition>();
            try
            {
                definition.menuItems.Add(new MenuItem
                {
                    id = "no-page",
                    label = "No Page",
                    page = null
                });

                Assert.DoesNotThrow(() =>
                {
                    var handler = new UIHandler(null, definition);
                    handler.Cleanup();
                });
            }
            finally
            {
                ScriptableObject.DestroyImmediate(definition);
            }
        }

        [Test]
        public void UIHandler_MultipleMenuItems_RegistersAllSelectors()
        {
            LiveClass.RegisterFromAttributes<ObjectSelectorBase>();
            LiveClass.RegisterFromAttributes<ObjectFactoryBase>();

            var definition = ScriptableObject.CreateInstance<UIDefinition>();
            try
            {
                definition.menuItems.Add(new MenuItem
                {
                    id = "page-a",
                    label = "Page A",
                    page = new CategoryPage()
                });
                definition.menuItems.Add(new MenuItem
                {
                    id = "page-b",
                    label = "Page B",
                    page = new CategoryPage()
                });

                var handler = new UIHandler(null, definition);

                Assert.IsNotNull(LiveObjectRegistry.FindById("ui.selector.page-a"));
                Assert.IsNotNull(LiveObjectRegistry.FindById("ui.selector.page-b"));
                Assert.IsNotNull(LiveObjectRegistry.FindById("ui.factory.page-a"));
                Assert.IsNotNull(LiveObjectRegistry.FindById("ui.factory.page-b"));

                handler.Cleanup();
            }
            finally
            {
                ScriptableObject.DestroyImmediate(definition);
            }
        }

        [Test]
        public void NavigateObjectSelector_SerializesStaticLiveObjectAsReference()
        {
            // LiveClass.Reset() で属性スキャン + 静的クラスの LiveObjectHandle 登録を行う。
            LiveClass.Reset();
            var staticClass = LiveClass.Find(typeof(NavigateSelectorTestPage));
            Assert.IsNotNull(staticClass, "static LiveClass must be registered");

            var definition = ScriptableObject.CreateInstance<UIDefinition>();
            try
            {
                definition.menuItems.Add(new MenuItem
                {
                    id = "settings",
                    label = "Settings",
                    page = new NavigatePage
                    {
                        selector = new NavigateObjectSelector
                        {
                            objectIds = new[] { nameof(NavigateSelectorTestPage) }
                        }
                    }
                });

                var handler = new UIHandler(null, definition);
                try
                {
                    var selectorObj = LiveObjectRegistry.FindById("ui.selector.settings");
                    Assert.IsNotNull(selectorObj, "selector LiveObjectHandle must be registered");

                    var json = LivePropertySerializer.ToJson(selectorObj.Value, DefaultLiveObjectResolver.Instance);
                    var parsed = JObject.Parse(json);
                    var objectsArray = parsed["objects"] as JArray;

                    Assert.IsNotNull(objectsArray, $"objects array must exist. json={json}");
                    Assert.AreEqual(1, objectsArray.Count, $"objects array must have one element. json={json}");
                    Assert.AreEqual(nameof(NavigateSelectorTestPage), objectsArray[0]["@ref"]?.Value<string>());
                    Assert.AreEqual(nameof(NavigateSelectorTestPage), objectsArray[0]["@type"]?.Value<string>());
                }
                finally
                {
                    handler.Cleanup();
                }
            }
            finally
            {
                ScriptableObject.DestroyImmediate(definition);
            }
        }

        [Test]
        public void NavigateObjectSelector_FallbacksToLiveClassWhenRegistryEmpty()
        {
            // _RegisterStaticLiveObjects が未実行の状態 (Edit モード等) を再現するため、
            // LiveClass 側の登録のみを行い、LiveObjectRegistry には載せない。
            LiveClass.Clear();
            LiveClass.RegisterFromAttributes<ObjectSelectorBase>();
            LiveClass.RegisterFromAttributes<ObjectFactoryBase>();
            LiveClass.RegisterClass(typeof(NavigateSelectorTestPage));
            LiveClass.RegisterProperties(typeof(NavigateSelectorTestPage));

            // precondition: 静的クラスは registry 未登録
            Assert.IsNull(LiveObjectRegistry.FindById(nameof(NavigateSelectorTestPage)),
                "precondition: static class must not be in registry yet");

            var definition = ScriptableObject.CreateInstance<UIDefinition>();
            try
            {
                definition.menuItems.Add(new MenuItem
                {
                    id = "settings",
                    label = "Settings",
                    page = new NavigatePage
                    {
                        selector = new NavigateObjectSelector
                        {
                            objectIds = new[] { nameof(NavigateSelectorTestPage) }
                        }
                    }
                });

                var handler = new UIHandler(null, definition);
                try
                {
                    var selectorObj = LiveObjectRegistry.FindById("ui.selector.settings");
                    Assert.IsNotNull(selectorObj);

                    var json = LivePropertySerializer.ToJson(selectorObj.Value, DefaultLiveObjectResolver.Instance);
                    var parsed = JObject.Parse(json);
                    var objectsArray = parsed["objects"] as JArray;

                    Assert.IsNotNull(objectsArray, $"objects array must exist. json={json}");
                    Assert.AreEqual(1, objectsArray.Count,
                        $"fallback must resolve static LiveClass. json={json}");
                    Assert.AreEqual(nameof(NavigateSelectorTestPage), objectsArray[0]["@ref"]?.Value<string>());
                }
                finally
                {
                    handler.Cleanup();
                }
            }
            finally
            {
                ScriptableObject.DestroyImmediate(definition);
            }
        }

        #endregion
    }
}
