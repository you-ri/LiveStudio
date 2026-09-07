// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.LiveStudio.Tests
{
    /// <summary>
    /// What the live scene saves, the frame carries; what it does not save, the frame leaves alone
    /// unless the member says otherwise (<see cref="FrameLaneRules"/>).
    ///
    /// Swept over every exposed type in the assemblies this package ships, rather than asserted on
    /// the fixtures that exercise the rule, because the thing that goes wrong is a real member: a
    /// project setting recorded because nothing beside it said so, a scene value declared off the
    /// frame and lost from every take that changed it. The rule is implemented once and this asks
    /// whether every registration went through it.
    /// </summary>
    public class PersistenceLaneInvariantTests
    {
        private static IEnumerable<Type> _ExposedTypes()
        {
            var assemblies = new[]
            {
                typeof(ExternalAssetManager).Assembly,      // Lilium.LiveStudio
                typeof(LiveClass).Assembly,                 // Lilium.RemoteControl
                typeof(FrameRecorderController).Assembly,   // Lilium.RemoteControl.Server
            };

            foreach (var assembly in assemblies)
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsGenericTypeDefinition || type.IsInterface) continue;
                    if (type.GetCustomAttribute<LiveClassAttribute>() == null) continue;
                    yield return type;
                }
            }
        }

        /// <summary>The lane a member wrote down, if it wrote one. A property over a shadow field
        /// inherits the field's declaration, as the registration does.</summary>
        private static FrameLane? _DeclaredLane(LivePropertyType member)
        {
            var field = member.shadowField ?? member.fieldInfo;
            if (field != null) return field.GetCustomAttribute<LiveFieldAttribute>()?.declaredLane;
            return member.properyInfo?.GetCustomAttribute<LivePropertyAttribute>()?.declaredLane;
        }

        private static bool _TypeIsOffFrame(Type type)
            => type.GetCustomAttribute<LiveClassAttribute>()?.lane == FrameLane.None;

        [Test]
        public void AMemberTheSceneSaves_IsCarried()
        {
            var offenders = new List<string>();

            foreach (var type in _ExposedTypes())
            {
                // A type declared off the frame as a whole absorbs its members by design; whether
                // such a type should have scene-saved members at all is asked elsewhere.
                if (_TypeIsOffFrame(type)) continue;

                var liveClass = LiveClass.Get(type);
                foreach (var member in liveClass.propertyTypes)
                {
                    if (member == null || member.isReadOnly || member.isStatic) continue;
                    if (!FrameLaneRules.IsSavedToScene(member.isPersistable, member.persistScope)) continue;
                    if (member.lane != FrameLane.None) continue;

                    offenders.Add($"{liveClass.typeName}.{member.name}");
                }
            }

            Assert.IsEmpty(offenders,
                "saved to the live scene but off the frame, so a take would disagree with the file: "
                + string.Join(", ", offenders));
        }

        [Test]
        public void AMemberTheSceneDoesNotSave_IsOffTheFrame_UnlessItSaysOtherwise()
        {
            var offenders = new List<string>();

            foreach (var type in _ExposedTypes())
            {
                var liveClass = LiveClass.Get(type);
                foreach (var member in liveClass.propertyTypes)
                {
                    if (member == null || member.isReadOnly || member.isStatic) continue;
                    if (FrameLaneRules.IsSavedToScene(member.isPersistable, member.persistScope)) continue;
                    if (_DeclaredLane(member).HasValue) continue;
                    // A view of another member's storage takes that member's lane, so its own
                    // persistence never had a say. Checked below instead.
                    if (!string.IsNullOrEmpty(member.carriedBy)) continue;
                    if (member.lane == FrameLane.None) continue;

                    offenders.Add($"{liveClass.typeName}.{member.name} ({member.lane})");
                }
            }

            Assert.IsEmpty(offenders,
                "not saved to the live scene and silent about its lane, yet on the frame: "
                + string.Join(", ", offenders));
        }

        /// <summary>
        /// The other half of the exception: a member that asked for a lane while nothing saves it
        /// is on that lane. This is how a pose, a weight, or which avatar is out reaches a take.
        /// </summary>
        [Test]
        public void AnUnsavedMemberThatAsksForALane_IsOnIt()
        {
            var offenders = new List<string>();

            foreach (var type in _ExposedTypes())
            {
                if (_TypeIsOffFrame(type)) continue;

                var liveClass = LiveClass.Get(type);
                foreach (var member in liveClass.propertyTypes)
                {
                    if (member == null || member.isReadOnly || member.isStatic) continue;
                    if (FrameLaneRules.IsSavedToScene(member.isPersistable, member.persistScope)) continue;

                    var declared = _DeclaredLane(member);
                    if (!declared.HasValue || declared.Value == FrameLane.None) continue;
                    if (member.lane == declared.Value) continue;

                    offenders.Add($"{liveClass.typeName}.{member.name} (asked {declared.Value}, got {member.lane})");
                }
            }

            Assert.IsEmpty(offenders, string.Join(", ", offenders));
        }

        /// <summary>
        /// A member that is a view of another member's storage answers with the carrier's lane.
        /// Its own persistence says nothing about the frame -- the carrier is what the frame holds --
        /// and reporting it as "not recorded" would be the opposite of the truth.
        /// </summary>
        [Test]
        public void AViewOfAnotherMembersStorage_TakesTheCarriersLane()
        {
            var offenders = new List<string>();

            foreach (var type in _ExposedTypes())
            {
                var liveClass = LiveClass.Get(type);
                foreach (var member in liveClass.propertyTypes)
                {
                    if (member == null || string.IsNullOrEmpty(member.carriedBy)) continue;

                    var carrier = Array.Find(liveClass.propertyTypes,
                        p => p != null && p.name == member.carriedBy);
                    if (carrier == null)
                    {
                        offenders.Add($"{liveClass.typeName}.{member.name} names no member '{member.carriedBy}'");
                        continue;
                    }

                    if (member.lane == carrier.lane) continue;

                    offenders.Add($"{liveClass.typeName}.{member.name} ({member.lane}) "
                        + $"is carried by {carrier.name} ({carrier.lane})");
                }
            }

            Assert.IsEmpty(offenders, string.Join(", ", offenders));
        }

        /// <summary>
        /// The half a declaration cannot promise: what the state block generator concluded.
        ///
        /// The runtime resolves the lane from what it can see of the persistence, and the generator
        /// re-derives it from the attribute arguments alone. Where the two see different things the
        /// member is off the frame as far as every write path is concerned -- its event records are
        /// dropped -- while a block copies it into every frame regardless, which is the whole of
        /// being recorded without anyone having said so.
        ///
        /// A bare <c>[LiveProperty]</c> is exactly such a member: nothing saves it (a property is
        /// persisted only through a shadow field or an [InlineReference]), so the runtime puts it
        /// off the frame, while the attribute says nothing the generator can read as "not saved".
        /// RenderQuality.quality reached the state lane that way -- a per-machine setting a replay
        /// would have written onto the machine playing it back.
        /// </summary>
        [Test]
        public void AMemberOffTheFrame_IsNotCarriedByABlock()
        {
            var offenders = new List<string>();

            foreach (var type in _ExposedTypes())
            {
                var bridge = StateBridgeRegistry.Find(type);
                if (bridge == null) continue;

                var liveClass = LiveClass.Get(type);
                foreach (var member in liveClass.propertyTypes)
                {
                    if (member == null || member.lane != FrameLane.None) continue;
                    if (!LiveStateCarriage.IsCarriedByState(member, bridge)) continue;

                    offenders.Add($"{liveClass.typeName}.{member.name}");
                }
            }

            Assert.IsEmpty(offenders,
                "off the frame for every write path, yet copied into every frame by a state block: "
                + string.Join(", ", offenders));
        }
    }
}
