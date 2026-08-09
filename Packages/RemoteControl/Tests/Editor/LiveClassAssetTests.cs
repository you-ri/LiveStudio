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
    /// LiveClassAsset / LiveClassBinding flow that exposes arbitrary components
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
            LiveClassAsset preset, LiveClassBinding resolver, UnityEngine.Object target)
        {
            var entry = new LiveClassAsset.InstanceBinding
            {
                key = System.Guid.NewGuid().ToString(),
                typeName = target.GetType().AssemblyQualifiedName,
            };
            preset.bindings.Add(entry);
            resolver.SetReferenceValue(new PropertyName(entry.key), target);
            return entry;
        }

        [TearDown]
        public void TearDown()
        {
            // Destroying the resolver GameObjects fires OnDisable, which unregisters bindings.
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

        // --- Resolver: reference table + instance registration ---

        private LiveClassBinding _CreateResolver(LiveClassAsset preset)
        {
            var go = _CreateGameObject("Resolver");
            var resolver = go.AddComponent<LiveClassBinding>();
            resolver.assets.Add(preset);
            return resolver;
        }

        [Test]
        public void Resolver_ResolvesAndRegistersInstance()
        {
            var preset = _CreatePreset();
            var definition = preset.GetOrAddTypeDefinition(typeof(Light));
            definition.members.Add(new LiveClassAssetMember { path = "intensity", control = new SliderControl { max = 8f } });
            definition.members.Add(new LiveClassAssetMember { path = "Reset", isFunction = true, label = "Reset Light" });

            var lightGo = _CreateGameObject("BindingLight");
            var light = lightGo.AddComponent<Light>();

            var resolver = _CreateResolver(preset);
            var entry = _AddBinding(preset, resolver, light);
            resolver.Reload();

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
        public void Resolver_UnboundKey_IsSkippedWithoutError()
        {
            var preset = _CreatePreset();
            var definition = preset.GetOrAddTypeDefinition(typeof(Light));
            definition.members.Add(new LiveClassAssetMember { path = "intensity" });

            var resolver = _CreateResolver(preset);
            var entry = new LiveClassAsset.InstanceBinding
            {
                key = System.Guid.NewGuid().ToString(),
                typeName = typeof(Light).AssemblyQualifiedName,
            };
            preset.bindings.Add(entry);
            resolver.Reload();

            Assert.That(LiveObjectRegistry.FindById(entry.key), Is.Null);
        }

        [Test]
        public void Resolver_Disable_UnregistersInstances()
        {
            var preset = _CreatePreset();
            preset.GetOrAddTypeDefinition(typeof(Light)).members.Add(new LiveClassAssetMember { path = "intensity" });

            var lightGo = _CreateGameObject("BindingLightDisable");
            var light = lightGo.AddComponent<Light>();

            var resolver = _CreateResolver(preset);
            var entry = _AddBinding(preset, resolver, light);
            resolver.Reload();

            Assert.That(LiveObjectRegistry.FindById(entry.key), Is.Not.Null);

            resolver.enabled = false;
            Assert.That(LiveObjectRegistry.FindById(entry.key), Is.Null);
        }

        [Test]
        public void Resolver_SameType_TwoInstances_ShareDefinition()
        {
            var preset = _CreatePreset();
            var definition = preset.GetOrAddTypeDefinition(typeof(Light));
            definition.members.Add(new LiveClassAssetMember { path = "intensity" });
            definition.members.Add(new LiveClassAssetMember { path = "range" });

            var lightA = _CreateGameObject("LightA").AddComponent<Light>();
            var lightB = _CreateGameObject("LightB").AddComponent<Light>();

            var resolver = _CreateResolver(preset);
            var entryA = _AddBinding(preset, resolver, lightA);
            var entryB = _AddBinding(preset, resolver, lightB);
            resolver.Reload();

            var liveClass = LiveClass.Find(typeof(Light));
            var handleA = LiveObjectRegistry.FindById(entryA.key);
            var handleB = LiveObjectRegistry.FindById(entryB.key);
            Assert.That(handleA, Is.Not.Null);
            Assert.That(handleB, Is.Not.Null);
            Assert.That(ReferenceEquals(handleA.Value.targetType, liveClass), Is.True);
            Assert.That(ReferenceEquals(handleB.Value.targetType, liveClass), Is.True);
        }

        [Test]
        public void Resolver_HostContainer_PersistenceRoundTrip()
        {
            var preset = _CreatePreset();
            var definition = preset.GetOrAddTypeDefinition(typeof(Light));
            definition.members.Add(new LiveClassAssetMember { path = "intensity", persistable = true });

            var lightGo = _CreateGameObject("BindingLightSave");
            var light = lightGo.AddComponent<Light>();

            // Host container on the same GameObject as the resolver: the resolver injects the
            // runtime wrappers into its object list, which the live-scene save enumerates.
            var hostGo = _CreateGameObject("BindingHost");
            var host = hostGo.AddComponent<RemoteControlContainer>();
            var resolver = hostGo.AddComponent<LiveClassBinding>();
            resolver.assets.Add(preset);
            var entry = _AddBinding(preset, resolver, light);
            resolver.Reload();

            var container = new LiveObjectContainer(hostGo.name, host._objects);
            container.Initialize();
            try
            {
                light.intensity = 7.25f;

                var resolved = LiveObjectGraph.ResolveLiveObjects(container.objects, container);
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
