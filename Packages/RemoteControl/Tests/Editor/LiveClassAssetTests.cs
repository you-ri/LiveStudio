// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Attribute-less exposure: define-based registration metadata overrides and the
    /// LiveClassAsset / RemoteControlContainer flow that exposes arbitrary components
    /// through preset assets + the standard IExposedPropertyTable scene reference table.
    /// </summary>
    public class LiveClassAssetTests
    {
        private readonly List<GameObject> _gameObjects = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

        private GameObject _CreateGameObject(string name)
        {
            var go = new GameObject(name);
            _gameObjects.Add(go);
            return go;
        }

        private LiveClassAsset _CreatePreset()
        {
            var preset = ScriptableObject.CreateInstance<LiveClassAsset>();
            _assets.Add(preset);
            return preset;
        }

        private static LiveClassAsset.InstanceBinding _AddBinding(
            LiveClassAsset preset, RemoteControlContainer container, UnityEngine.Object target)
        {
            var entry = new LiveClassAsset.InstanceBinding
            {
                key = System.Guid.NewGuid().ToString(),
                typeName = target.GetType().AssemblyQualifiedName,
            };
            preset.bindings.Add(entry);
            container.SetReferenceValue(new PropertyName(entry.key), target);
            return entry;
        }

        [TearDown]
        public void TearDown()
        {
            // Destroying the container GameObjects fires OnDisable, which unregisters bindings.
            foreach (var go in _gameObjects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _gameObjects.Clear();

            foreach (var asset in _assets)
            {
                if (asset != null) Object.DestroyImmediate(asset);
            }
            _assets.Clear();
        }

        // --- Define-based registration with metadata overrides ---

        private class PlainTarget
        {
            public float number;
            public string text = "abc";
            public bool invoked;
            public void DoThing() { invoked = true; }
        }

        [Test]
        public void Register_WithMetadataOverrides_AppliesControlLabelHelp()
        {
            LiveClass.Register(typeof(PlainTarget), "BindingTestPlainTarget",
                new[]
                {
                    new LivePropertyDefine
                    {
                        name = "number",
                        path = "number",
                        isPersistable = true,
                        control = new SliderAttribute(0f, 10f),
                        label = "Number Label",
                        help = "HELP_TEXT",
                    },
                    new LivePropertyDefine { name = "text", path = "text" },
                });

            var liveClass = LiveClass.Find(typeof(PlainTarget));
            Assert.That(liveClass, Is.Not.Null);

            var number = liveClass.FindProperty("number");
            Assert.That(number, Is.Not.Null);
            Assert.That(number.controlAttribute, Is.TypeOf<SliderAttribute>());
            Assert.That(((SliderAttribute)number.controlAttribute).maxValue, Is.EqualTo(10f));
            Assert.That(number.label, Is.EqualTo("Number Label"));
            Assert.That(number.help, Is.EqualTo("HELP_TEXT"));
            Assert.That(number.isPersistable, Is.True);

            var text = liveClass.FindProperty("text");
            Assert.That(text, Is.Not.Null);
            Assert.That(text.controlAttribute.controlName, Is.EqualTo("default"));
            Assert.That(text.label, Is.Null);
        }

        [Test]
        public void Register_WithFunctionDefines_ExposesMethod()
        {
            LiveClass.Register(typeof(PlainTarget), "BindingTestPlainTargetFn",
                new LivePropertyDefine[0],
                new[]
                {
                    new LiveFunctionDefine { path = "DoThing", label = "Do The Thing" },
                });

            var liveClass = LiveClass.Find(typeof(PlainTarget));
            var fn = liveClass.FindFunction("dothing");
            Assert.That(fn, Is.Not.Null);
            Assert.That(fn.label, Is.EqualTo("Do The Thing"));

            var target = new PlainTarget();
            fn.Invoke(target, null);
            Assert.That(target.invoked, Is.True);
        }

        // --- Preset type definitions ---

        [Test]
        public void Preset_TypeDefinition_RegistersLiveClass()
        {
            var preset = _CreatePreset();
            var definition = preset.GetOrAddTypeDefinition(typeof(Light));
            definition.members.Add(new LiveClassAssetMember { path = "intensity", control = new SliderControl { min = 0f, max = 8f } });
            definition.members.Add(new LiveClassAssetMember { path = "enabled" });
            definition.members.Add(new LiveClassAssetMember { path = "range", persistable = false });

            LiveClassAssetSystem.RegisterTypes(preset);

            var liveClass = LiveClass.Find(typeof(Light));
            Assert.That(liveClass, Is.Not.Null);
            Assert.That(liveClass.FindProperty("intensity"), Is.Not.Null);
            Assert.That(liveClass.FindProperty("intensity").controlAttribute, Is.TypeOf<SliderAttribute>());
            Assert.That(liveClass.FindProperty("enabled"), Is.Not.Null);
            Assert.That(liveClass.FindProperty("intensity").isPersistable, Is.True);
            Assert.That(liveClass.FindProperty("range").isPersistable, Is.False);
        }

        // --- Container: reference table + instance registration ---

        private RemoteControlContainer _CreateContainer(LiveClassAsset preset)
        {
            var go = _CreateGameObject("Container");
            var container = go.AddComponent<RemoteControlContainer>();
            container.assets.Add(preset);
            return container;
        }

        [Test]
        public void Container_ResolvesAndRegistersInstance()
        {
            var preset = _CreatePreset();
            var definition = preset.GetOrAddTypeDefinition(typeof(Light));
            definition.members.Add(new LiveClassAssetMember { path = "intensity", control = new SliderControl { max = 8f } });
            definition.members.Add(new LiveClassAssetMember { path = "Reset", isFunction = true, label = "Reset Light" });

            var lightGo = _CreateGameObject("BindingLight");
            var light = lightGo.AddComponent<Light>();

            var container = _CreateContainer(preset);
            var entry = _AddBinding(preset, container, light);
            container.Reload();

            var handle = LiveObjectRegistry.FindById(entry.key);
            Assert.That(handle, Is.Not.Null, "Binding key must be registered as the LiveObject id");
            Assert.That(handle.Value.target, Is.SameAs(light));

            // Value round-trip through the live property.
            var property = handle.Value.FindProperty("intensity");
            Assert.That(property, Is.Not.Null);
            property.Value.SetValue(3.5f);
            Assert.That(light.intensity, Is.EqualTo(3.5f).Within(0.0001f));

            // Function exposed from the definition.
            Assert.That(handle.Value.GetFunction("reset"), Is.Not.Null);
        }

        [Test]
        public void Container_UnboundKey_IsSkippedWithoutError()
        {
            var preset = _CreatePreset();
            var definition = preset.GetOrAddTypeDefinition(typeof(Light));
            definition.members.Add(new LiveClassAssetMember { path = "intensity" });

            var container = _CreateContainer(preset);
            var entry = new LiveClassAsset.InstanceBinding
            {
                key = System.Guid.NewGuid().ToString(),
                typeName = typeof(Light).AssemblyQualifiedName,
            };
            preset.bindings.Add(entry);
            container.Reload();

            Assert.That(LiveObjectRegistry.FindById(entry.key), Is.Null);
        }

        [Test]
        public void Container_Disable_UnregistersInstances()
        {
            var preset = _CreatePreset();
            preset.GetOrAddTypeDefinition(typeof(Light)).members.Add(new LiveClassAssetMember { path = "intensity" });

            var lightGo = _CreateGameObject("BindingLightDisable");
            var light = lightGo.AddComponent<Light>();

            var container = _CreateContainer(preset);
            var entry = _AddBinding(preset, container, light);
            container.Reload();

            Assert.That(LiveObjectRegistry.FindById(entry.key), Is.Not.Null);

            container.enabled = false;
            Assert.That(LiveObjectRegistry.FindById(entry.key), Is.Null);
        }

        [Test]
        public void Container_Disable_UnregistersAssetDefinedType()
        {
            var preset = _CreatePreset();
            preset.GetOrAddTypeDefinition(typeof(Light)).members.Add(new LiveClassAssetMember { path = "intensity" });

            var container = _CreateContainer(preset);
            container.Reload();
            Assert.That(LiveClass.Has(typeof(Light)), Is.True);

            container.enabled = false;
            Assert.That(LiveClass.Has(typeof(Light)), Is.False,
                "The last container to drop an asset takes its type registration with it, so an " +
                "asset carried in by a bundle leaves nothing behind when the bundle unloads");
        }

        [Test]
        public void Container_Reload_UnregistersTypeDroppedFromAsset()
        {
            var preset = _CreatePreset();
            var definition = preset.GetOrAddTypeDefinition(typeof(Light));
            definition.members.Add(new LiveClassAssetMember { path = "intensity" });

            var container = _CreateContainer(preset);
            container.Reload();
            Assert.That(LiveClass.Has(typeof(Light)), Is.True);

            // How the editor window removes a class: the definition leaves the asset first, and
            // the reload has to take the registration with it anyway.
            preset.typeDefinitions.Remove(definition);
            container.Reload();

            Assert.That(LiveClass.Has(typeof(Light)), Is.False,
                "A type no longer declared by the asset must not stay registered for the rest of " +
                "the session (it would keep showing up in /live/types and in components lists)");
        }

        [Test]
        public void Container_Disable_KeepsTypeStillBoundByAnotherContainer()
        {
            var preset = _CreatePreset();
            preset.GetOrAddTypeDefinition(typeof(Light)).members.Add(new LiveClassAssetMember { path = "intensity" });

            var lightA = _CreateGameObject("SharedLightA").AddComponent<Light>();
            var lightB = _CreateGameObject("SharedLightB").AddComponent<Light>();

            var containerA = _CreateContainer(preset);
            var entryA = _AddBinding(preset, containerA, lightA);
            containerA.Reload();

            var containerB = _CreateContainer(preset);
            var entryB = _AddBinding(preset, containerB, lightB);
            containerB.Reload();

            containerA.enabled = false;

            Assert.That(LiveObjectRegistry.FindById(entryA.key), Is.Null);
            Assert.That(LiveClass.Has(typeof(Light)), Is.True,
                "B still exposes an instance of the type, and its handle holds this LiveClass");
            Assert.That(LiveObjectRegistry.FindById(entryB.key), Is.Not.Null);
        }

        [Test]
        public void Container_RuntimeBindings_StayOutOfSerializedObjectList()
        {
            var preset = _CreatePreset();
            preset.GetOrAddTypeDefinition(typeof(Light)).members.Add(new LiveClassAssetMember { path = "intensity" });

            var light = _CreateGameObject("BindingLightSerialize").AddComponent<Light>();

            var container = _CreateContainer(preset);
            _AddBinding(preset, container, light);
            container.Reload();

            // _objects is [SerializeReference]: anything put there is written into the scene file.
            Assert.That(container._objects, Is.Empty);
            Assert.That(container.bindingObjects.Count, Is.EqualTo(1));
        }

        [Test]
        public void Container_SameType_TwoInstances_ShareDefinition()
        {
            var preset = _CreatePreset();
            var definition = preset.GetOrAddTypeDefinition(typeof(Light));
            definition.members.Add(new LiveClassAssetMember { path = "intensity" });
            definition.members.Add(new LiveClassAssetMember { path = "range" });

            var lightA = _CreateGameObject("LightA").AddComponent<Light>();
            var lightB = _CreateGameObject("LightB").AddComponent<Light>();

            var container = _CreateContainer(preset);
            var entryA = _AddBinding(preset, container, lightA);
            var entryB = _AddBinding(preset, container, lightB);
            container.Reload();

            var liveClass = LiveClass.Find(typeof(Light));
            var handleA = LiveObjectRegistry.FindById(entryA.key);
            var handleB = LiveObjectRegistry.FindById(entryB.key);
            Assert.That(handleA, Is.Not.Null);
            Assert.That(handleB, Is.Not.Null);
            Assert.That(ReferenceEquals(handleA.Value.targetType, liveClass), Is.True);
            Assert.That(ReferenceEquals(handleB.Value.targetType, liveClass), Is.True);
        }

        [Test]
        public void Container_HostContainer_PersistenceRoundTrip()
        {
            var preset = _CreatePreset();
            var definition = preset.GetOrAddTypeDefinition(typeof(Light));
            definition.members.Add(new LiveClassAssetMember { path = "intensity", persistable = true });

            var lightGo = _CreateGameObject("BindingLightSave");
            var light = lightGo.AddComponent<Light>();

            // The container is the host. Its runtime wrappers are a source of their own (they are
            // deliberately kept out of the serialized _objects list), which is what
            // RemoteControlBehaviour merges and the live-scene save then enumerates.
            var hostGo = _CreateGameObject("BindingHost");
            var host = hostGo.AddComponent<RemoteControlContainer>();
            host.assets.Add(preset);
            var entry = _AddBinding(preset, host, light);
            host.Reload();

            var container = new LiveObjectContainer(hostGo.name, host._objects);
            container.AddSource(host.bindingObjects, host.bindingObjects);
            container.Initialize();
            try
            {
                light.intensity = 7.25f;

                // EnumerateAllObjects, not .objects: the save path walks the main list plus every
                // merged source, and the binding wrappers are a source.
                var all = new List<ILiveObject>(container.EnumerateAllObjects());
                var resolved = LiveObjectGraph.ResolveLiveObjects(all, container);
                var saved = LiveSceneSerializer.LiveSceneToJson(resolved, container, SerializeMode.Snapshot);

                var parsed = JObject.Parse(saved);
                var objectsArr = (JArray)parsed["objects"];
                Assert.That(objectsArr, Is.Not.Null);
                JObject json = null;
                foreach (var token in objectsArr)
                {
                    var entryId = token["@source"]?.Value<string>() ?? token["@id"]?.Value<string>();
                    if (entryId == entry.key) { json = (JObject)token; break; }
                }
                Assert.That(json, Is.Not.Null, "Binding entry must be present in saved JSON. Actual: " + saved);
                Assert.That(json["intensity"].Value<float>(), Is.EqualTo(7.25f).Within(0.001f));

                light.intensity = 1f;
                LiveSceneSerializer.LiveSceneFromJson(saved, container);
                Assert.That(light.intensity, Is.EqualTo(7.25f).Within(0.001f));
            }
            finally
            {
                container.Shutdown();
            }
        }
    }
}
