// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;

using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// What the live scene saves, the frame carries.
    ///
    /// Containment, not equality, and the direction is the whole point. A keyframe is already a
    /// superset of a scene snapshot (see the summary on <see cref="LiveStateSystem"/>): a snapshot
    /// holds the persisted members, the frame holds every member declared
    /// <see cref="FrameLane.State"/> whether it is persisted or not. The frame also carries objects
    /// a snapshot has no room for -- an exposed scene component nothing references, an object a
    /// replay's recipe stood up. Asserting equality would take those away, so the rule is one-way.
    ///
    /// The rule: an object the live scene writes, whose type declares anything on the state lane,
    /// has to be somewhere in the frame. An object that is saved but not carried is the failure
    /// that reads as "the world never changed" rather than as "the recording says nothing about
    /// this" -- the same failure the roster was added to fix, for a kind of object it does not
    /// reach.
    /// </summary>
    public class LiveSceneFrameParityTests
    {
        private readonly List<GameObject> _gameObjects = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();
        private LiveObjectContainer _container;

        [SetUp]
        public void StartClean()
        {
            LiveObjectRoster.Clear();
        }

        [TearDown]
        public void Finish()
        {
            _container?.Shutdown();
            _container = null;

            // Destroying the container fires OnDisable, which unapplies the asset.
            for (int i = 0; i < _gameObjects.Count; i++)
            {
                if (_gameObjects[i] != null) Object.DestroyImmediate(_gameObjects[i]);
            }
            _gameObjects.Clear();

            for (int i = 0; i < _assets.Count; i++)
            {
                if (_assets[i] != null) Object.DestroyImmediate(_assets[i]);
            }
            _assets.Clear();

            LiveObjectRoster.Clear();
        }

        private GameObject _CreateGameObject(string name)
        {
            var go = new GameObject(name);
            _gameObjects.Add(go);
            return go;
        }

        /// <summary>
        /// The shape the Live Class Asset window produces: a type declared by an asset, and a
        /// GameObject the container exposes, with the component reached through its components
        /// list. Deliberately no instance binding -- that is the opt-in that gives a component an
        /// id of its own, and the case being covered here is the one without it.
        ///
        /// The Light is declared by an asset rather than by an attribute because that is the case
        /// with no other way in: the roster only ever considers types carrying [LiveClass].
        /// </summary>
        private LiveGameObject _PlaceExposedLight(out Light light) => _PlaceExposedLight(out light, out _);

        /// <inheritdoc cref="_PlaceExposedLight(out Light)"/>
        /// <param name="bindingKey">
        /// When non-null on return, the id an instance binding gave the component. Pass
        /// <paramref name="bind"/> to ask for one.
        /// </param>
        private LiveGameObject _PlaceExposedLight(out Light light, out string bindingKey, bool bind = false)
        {
            var asset = ScriptableObject.CreateInstance<LiveClassAsset>();
            _assets.Add(asset);
            var definition = asset.GetOrAddTypeDefinition(typeof(Light));
            definition.members.Add(new LiveClassAssetMember
            {
                path = "intensity",
                persistable = true,
                lane = LiveClassAssetLane.State,
            });

            var lightGo = _CreateGameObject("ParityLight");
            light = lightGo.AddComponent<Light>();
            light.intensity = 3.5f;

            var hostGo = _CreateGameObject("ParityHost");
            var host = hostGo.AddComponent<RemoteControlContainer>();
            host.assets.Add(asset);

            bindingKey = null;
            if (bind)
            {
                // The opt-in: an instance binding gives the component an id of its own, which the
                // save path prefers over the composed key and the frame has to prefer with it.
                bindingKey = System.Guid.NewGuid().ToString();
                asset.bindings.Add(new LiveClassAsset.InstanceBinding
                {
                    key = bindingKey,
                    typeName = typeof(Light).AssemblyQualifiedName,
                });
                host.SetReferenceValue(new PropertyName(bindingKey), light);
            }

            host.Reload();

            Assert.That(LiveClass.Has(typeof(Light)), Is.True,
                "the declaration has to be registered before either side can see the component");

            var proxy = new LiveGameObject(lightGo);
            host._objects.Add(proxy);

            _container = new LiveObjectContainer(hostGo.name, host._objects);
            _container.AddSource(host.bindingObjects, host.bindingObjects);
            _container.Initialize();

            return proxy;
        }

        /// <summary>The address the live scene gives the Light, and the whole document it came from.</summary>
        private string _SavedAddressOfLight(out string json)
        {
            var all = new List<ILiveObject>(_container.EnumerateAllObjects());
            var resolved = LiveObjectGraph.ResolveLiveObjects(all, _container);
            json = LiveSceneSerializer.LiveSceneToJson(resolved, _container, SerializeMode.Snapshot);

            var objects = (JArray)JObject.Parse(json)["objects"];
            if (objects == null) return null;

            foreach (var token in objects)
            {
                if (token["@type"]?.Value<string>() != "Light") continue;
                return token["@source"]?.Value<string>() ?? token["@id"]?.Value<string>();
            }
            return null;
        }

        /// <summary>
        /// The address the frame would use for what the live scene calls
        /// <paramref name="sourceKey"/>.
        ///
        /// The two compose the same parts in the same order and differ only in the separator --
        /// the live scene joins with '.', the frame with '/' (which is how the event lane addresses
        /// a write to a nested object). Translating here rather than asserting one spelling keeps
        /// the test about which objects are carried, not about which punctuation they are carried
        /// under.
        /// </summary>
        private static string _AsFrameId(string sourceKey)
        {
            return string.IsNullOrEmpty(sourceKey) ? sourceKey : sourceKey.Replace('.', '/');
        }

        /// <summary>Every owner address in the set, whichever kind of block holds it.</summary>
        private static HashSet<string> _CarriedIds(StateBlockSet state)
        {
            var ids = new HashSet<string>();
            var blocks = state.blocks;
            for (int b = 0; b < blocks.Count; b++)
            {
                var block = blocks[b];
                for (int i = 0; i < block.count; i++)
                {
                    ids.Add(FrameGate.symbols.Resolve(block.OwnerIdAt(i)));
                }
            }
            return ids;
        }

        [Test]
        public void TheLiveScene_KeysAComponentByItsExposedTypeName()
        {
            var proxy = _PlaceExposedLight(out _);

            var address = _SavedAddressOfLight(out var json);

            // Frozen deliberately. This is the address a saved scene already carries, so it is not
            // ours to change: a live.json written before any of this work has to keep loading
            // afterwards. Everything the frame side gains has to be built to this spelling rather
            // than the spelling changed to suit it.
            //
            // The element key is the exposed type name and not the position, because the position
            // is whatever order the components happen to sit in -- re-export a bundle and
            // "components[0]" is a different component, silently.
            Assert.That(address, Is.EqualTo(proxy.id + ".components[Light]"),
                "the live scene's address for an exposed component changed. Actual document: " + json);
        }

        [Test]
        public void AComponentTheLiveSceneSaves_IsCarriedByTheFrame()
        {
            _PlaceExposedLight(out _);

            var savedAddress = _SavedAddressOfLight(out var json);
            Assert.That(savedAddress, Is.Not.Null,
                "the live scene did not save the component, so there is no parity to test and this "
                + "fixture no longer covers what it was written for. Actual: " + json);

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            var carried = _CarriedIds(state);

            Assert.That(carried, Contains.Item(_AsFrameId(savedAddress)),
                "the live scene saves this component but the frame does not carry it. A recording "
                + "made now says the light never changed rather than saying nothing about it, and "
                + "a machine resynchronised from it lights the scene differently. Saved as '"
                + savedAddress + "'; carried: [" + string.Join(", ", carried) + "]");
        }

        [Test]
        public void AComponentsValue_ComesBackWhereItWasTakenFrom()
        {
            _PlaceExposedLight(out var light);

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            // Carrying an address is not the same as carrying a value, and a replay that puts the
            // value back somewhere else is the failure this catches: the two halves compose the
            // address separately, so they can agree on the spelling and still miss each other.
            light.intensity = 99f;
            LiveStateSystem.ApplyFrom(state);

            Assert.That(light.intensity, Is.EqualTo(3.5f).Within(0.001f),
                "the frame carried the component but a replay did not write it back");
        }

        [Test]
        public void TheStructureLane_StillLeavesComponentsOut()
        {
            var proxy = _PlaceExposedLight(out _);

            using var structure = new StructureBlock();
            LiveStructureSystem.CaptureInto(structure, FrameGate.symbols);

            // The ledger says what a replay has to stand up or tear down. A component arrives with
            // the GameObject that holds it, so a replay neither makes nor destroys one -- an entry
            // for it would be an entry nothing ever acts on, in every keyframe.
            //
            // Carrying its state under the owner's address is what makes that stay true: existence
            // is implied by the owner's, so there is nothing left for the ledger to say.
            var componentId = FrameGate.symbols.Intern(proxy.id + "/components[Light]");

            Assert.That(structure.Contains(componentId), Is.False,
                "an exposed component turned up in the structure ledger. Its state belongs in the "
                + "frame; its existence is the owner's to declare");
            Assert.That(structure.Contains(FrameGate.symbols.Intern(proxy.id)), Is.True,
                "the owner is what the ledger is supposed to carry, and it is missing");
        }

        [Test]
        public void ABoundComponent_IsCarriedUnderItsOwnId_AndNotAlsoUnderTheComposedOne()
        {
            var proxy = _PlaceExposedLight(out _, out var bindingKey, bind: true);

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            var carried = _CarriedIds(state);

            Assert.That(carried, Contains.Item(bindingKey),
                "a binding gives the component an id of its own, and that is the address it should "
                + "be carried under. Carried: [" + string.Join(", ", carried) + "]");

            // The composed address is what the component has *instead of* an id, not as well as
            // one. Carrying both would put the same state in the frame twice, and a replay would
            // then write it twice from two elements that can disagree.
            Assert.That(carried, Does.Not.Contain(proxy.id + "/components[Light]"),
                "the component was carried twice: once under its binding id and once under the "
                + "address composed from its owner");
        }
    }
}
