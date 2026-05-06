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
    [ExposedClass(Icon = "settings")]
    internal static class NavigateSelectorTestPage
    {
        [ExposedProperty]
        public static float value { get => 1f; set { } }
    }

    [TestFixture]
    public class UITests
    {
        [SetUp]
        public void Setup()
        {
            ExposedClass.Clear();
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

        [Test]
        public void StandardObjectFactory_NullFactories_ReturnsEmptyObjects()
        {
            var factory = new StandardObjectFactory { factories = null };
            Assert.AreEqual(0, factory.objects.Length);
        }

        [Test]
        public void StandardObjectFactory_NullFactories_ReturnsEmptyNames()
        {
            var factory = new StandardObjectFactory { factories = null };
            Assert.AreEqual(0, factory.objectNames.Length);
        }

        [Test]
        public void StandardObjectFactory_WithFactories_ReturnsCorrectObjects()
        {
            var go1 = new GameObject("Prefab1");
            var go2 = new GameObject("Prefab2");
            try
            {
                var f1 = new ExposedGameObjectFactory { prefab = go1 };
                var f2 = new ExposedGameObjectFactory { prefab = go2 };
                var factory = new StandardObjectFactory
                {
                    factories = new IExposedObjectFactory[] { f1, f2 }
                };
                var objects = factory.objects;

                Assert.AreEqual(2, objects.Length);
                Assert.AreEqual(f1, objects[0]);
                Assert.AreEqual(f2, objects[1]);
            }
            finally
            {
                GameObject.DestroyImmediate(go1);
                GameObject.DestroyImmediate(go2);
            }
        }

        [Test]
        public void StandardObjectFactory_WithFactories_ReturnsCorrectNames()
        {
            var go1 = new GameObject("Alpha");
            var go2 = new GameObject("Beta");
            try
            {
                var f1 = new ExposedGameObjectFactory { prefab = go1 };
                var f2 = new ExposedGameObjectFactory { prefab = go2 };
                var factory = new StandardObjectFactory
                {
                    factories = new IExposedObjectFactory[] { f1, f2 }
                };
                var names = factory.objectNames;

                Assert.AreEqual(2, names.Length);
                Assert.AreEqual("Alpha", names[0]);
                Assert.AreEqual("Beta", names[1]);
            }
            finally
            {
                GameObject.DestroyImmediate(go1);
                GameObject.DestroyImmediate(go2);
            }
        }

        [Test]
        public void StandardObjectFactory_WithNullFactoryEntry_ReturnsEmptyName()
        {
            var go = new GameObject("Valid");
            try
            {
                var f1 = new ExposedGameObjectFactory { prefab = go };
                var factory = new StandardObjectFactory
                {
                    factories = new IExposedObjectFactory[] { f1, null }
                };
                var names = factory.objectNames;

                Assert.AreEqual(2, names.Length);
                Assert.AreEqual("Valid", names[0]);
                Assert.AreEqual("", names[1]);
            }
            finally
            {
                GameObject.DestroyImmediate(go);
            }
        }

        [Test]
        public void StandardObjectFactory_CreateObject_InvalidIndex_DoesNotThrow()
        {
            var factory = new StandardObjectFactory { factories = new IExposedObjectFactory[0] };
            Assert.DoesNotThrow(() => factory.CreateObject(-1));
            Assert.DoesNotThrow(() => factory.CreateObject(0));
            Assert.DoesNotThrow(() => factory.CreateObject(100));
        }

        [Test]
        public void StandardObjectFactory_CreateObject_NullFactories_DoesNotThrow()
        {
            var factory = new StandardObjectFactory { factories = null };
            Assert.DoesNotThrow(() => factory.CreateObject(0));
        }

        [Test]
        public void StandardObjectFactory_CreateObject_NullFactoryEntry_DoesNotThrow()
        {
            var factory = new StandardObjectFactory
            {
                factories = new IExposedObjectFactory[] { null }
            };
            Assert.DoesNotThrow(() => factory.CreateObject(0));
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
                ExposedClass.Register<GameObject>("DestroyTest", new ExposedPropertyDefine[0]);
                var exposedClass = ExposedClass.Find(typeof(GameObject));
                var exposedObj = new ExposedObject("destroy-test-1", exposedClass, instance);

                var factory = new StandardObjectFactory();
                factory.DestroyObject("destroy-test-1");

                // ExposedObjectが解除されたことを確認
                Assert.IsNull(ExposedObjectRegistry.FindById("destroy-test-1"));
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

        [Test]
        public void StandardObjectFactory_RegisterPrefabs_NullFactories_DoesNotThrow()
        {
            var factory = new StandardObjectFactory { factories = null };
            Assert.DoesNotThrow(() => factory.RegisterPrefabs());
        }

        [Test]
        public void StandardObjectFactory_RegisterPrefabs_WithFactories_RegistersAll()
        {
            var go1 = new GameObject("RegPrefab1");
            var go2 = new GameObject("RegPrefab2");
            try
            {
                var factory = new StandardObjectFactory
                {
                    factories = new IExposedObjectFactory[]
                    {
                        new ExposedGameObjectFactory { prefab = go1 },
                        new ExposedGameObjectFactory { prefab = go2 }
                    }
                };
                // Asset でない生成 GameObject は GUID を持てないため、Warning が出ることを期待する。
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*RegPrefab1.*no guid.*"));
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*RegPrefab2.*no guid.*"));
                Assert.DoesNotThrow(() => factory.RegisterPrefabs());
            }
            finally
            {
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

        #region UIHandler Registration Tests

        [Test]
        public void UIHandler_RegistersSelectorsForCategoryPages()
        {
            ExposedClass.RegisterFromAttributes<ObjectSelectorBase>();
            ExposedClass.RegisterFromAttributes<ObjectFactoryBase>();

            var definition = ScriptableObject.CreateInstance<UIDefinition>();
            try
            {
                definition.menuItems.Add(new MenuItem
                {
                    id = "test-page",
                    label = "Test",
                    page = new CategoryPage()
                });

                // UIHandler のコンストラクタで selector が ExposedObject として登録される
                var handler = new UIHandler(null, definition);

                var selectorObj = ExposedObjectRegistry.FindById("ui.selector.test-page");
                Assert.IsNotNull(selectorObj, "Selector should be registered as ExposedObject");

                var factoryObj = ExposedObjectRegistry.FindById("ui.factory.test-page");
                Assert.IsNotNull(factoryObj, "Factory should be registered as ExposedObject");

                handler.Cleanup();
            }
            finally
            {
                ScriptableObject.DestroyImmediate(definition);
            }
        }

        [Test]
        public void UIHandler_Cleanup_UnregistersExposedObjects()
        {
            ExposedClass.RegisterFromAttributes<ObjectSelectorBase>();
            ExposedClass.RegisterFromAttributes<ObjectFactoryBase>();

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
                Assert.IsNotNull(ExposedObjectRegistry.FindById("ui.selector.cleanup-page"));
                Assert.IsNotNull(ExposedObjectRegistry.FindById("ui.factory.cleanup-page"));

                // Cleanup
                handler.Cleanup();

                // 解除を確認
                Assert.IsNull(ExposedObjectRegistry.FindById("ui.selector.cleanup-page"));
                Assert.IsNull(ExposedObjectRegistry.FindById("ui.factory.cleanup-page"));
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
            ExposedClass.RegisterFromAttributes<ObjectSelectorBase>();

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
            ExposedClass.RegisterFromAttributes<ObjectSelectorBase>();
            ExposedClass.RegisterFromAttributes<ObjectFactoryBase>();

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

                Assert.IsNotNull(ExposedObjectRegistry.FindById("ui.selector.page-a"));
                Assert.IsNotNull(ExposedObjectRegistry.FindById("ui.selector.page-b"));
                Assert.IsNotNull(ExposedObjectRegistry.FindById("ui.factory.page-a"));
                Assert.IsNotNull(ExposedObjectRegistry.FindById("ui.factory.page-b"));

                handler.Cleanup();
            }
            finally
            {
                ScriptableObject.DestroyImmediate(definition);
            }
        }

        [Test]
        public void NavigateObjectSelector_SerializesStaticExposedObjectAsReference()
        {
            // ExposedClass.Reset() で属性スキャン + 静的クラスの ExposedObject 登録を行う。
            ExposedClass.Reset();
            var staticClass = ExposedClass.Find(typeof(NavigateSelectorTestPage));
            Assert.IsNotNull(staticClass, "static ExposedClass must be registered");

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
                    var selectorObj = ExposedObjectRegistry.FindById("ui.selector.settings");
                    Assert.IsNotNull(selectorObj, "selector ExposedObject must be registered");

                    var json = ExposedPropertySerializer.ToJson(selectorObj, DefaultExposedObjectResolver.Instance);
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
        public void NavigateObjectSelector_FallbacksToExposedClassWhenRegistryEmpty()
        {
            // _RegisterStaticExposedObjects が未実行の状態 (Edit モード等) を再現するため、
            // ExposedClass 側の登録のみを行い、ExposedObjectRegistry には載せない。
            ExposedClass.Clear();
            ExposedClass.RegisterFromAttributes<ObjectSelectorBase>();
            ExposedClass.RegisterFromAttributes<ObjectFactoryBase>();
            ExposedClass.RegisterClass(typeof(NavigateSelectorTestPage));
            ExposedClass.RegisterProperties(typeof(NavigateSelectorTestPage));

            // precondition: 静的クラスは registry 未登録
            Assert.IsNull(ExposedObjectRegistry.FindById(nameof(NavigateSelectorTestPage)),
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
                    var selectorObj = ExposedObjectRegistry.FindById("ui.selector.settings");
                    Assert.IsNotNull(selectorObj);

                    var json = ExposedPropertySerializer.ToJson(selectorObj, DefaultExposedObjectResolver.Instance);
                    var parsed = JObject.Parse(json);
                    var objectsArray = parsed["objects"] as JArray;

                    Assert.IsNotNull(objectsArray, $"objects array must exist. json={json}");
                    Assert.AreEqual(1, objectsArray.Count,
                        $"fallback must resolve static ExposedClass. json={json}");
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
