// Copyright (c) You-Ri, 2026
using NUnit.Framework;

using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// A type on the state lane that is **not** partial.
    ///
    /// That this file compiles and the type below gets a bridge at all is the change under test.
    /// The lane used to require every owner to be declared <c>partial</c>, because its block was
    /// emitted as a second half of the owner; 89 exposed types declare one and 7 are partial, so
    /// the requirement would have made being in the simulation a condition of being exposed.
    /// </summary>
    [LiveClass("BesideOwnerProbe")]
    public class BesideOwnerProbe
    {
        /// <summary>Reachable from anywhere. The plain case.</summary>
        [LiveField(lane = FrameLane.State)] public float visible;

        /// <summary>Reachable from the assembly the generated half is compiled into.</summary>
        [LiveField(lane = FrameLane.State)] internal float alsoVisible;

        /// <summary>
        /// Carried under its own name -- there is no <c>[FormerlyNamedAs]</c> pairing it with the
        /// property below -- so a block outside the owner cannot touch it. Left out with LRC009.
        /// </summary>
        [LiveField(lane = FrameLane.State)] private float _hidden;

        public float hidden { get => _hidden; set => _hidden = value; }

        /// <summary>
        /// Protected means "a derived type may". The movers written beside this one are not a
        /// derived type, so it is as out of reach as the private field above.
        /// </summary>
        [LiveField(lane = FrameLane.State)] protected float guarded;

        /// <summary>
        /// The shape most of the exposed surface has, and the reason the trade is affordable: the
        /// field declares the lane but the value travels through the property, which is public. A
        /// private field is only out of reach when nothing public stands in front of it.
        /// </summary>
        [LiveField(lane = FrameLane.State), Hide]
        [FormerlyNamedAs("label")]
        private float _label;

        [LiveProperty]
        public float label { get => _label; set => _label = value; }
    }

    /// <summary>
    /// The same members on a type that <em>is</em> partial, so its block goes inside it.
    ///
    /// Protected is the member that answers differently from the two vantage points: a block
    /// written inside the type reaches it, one written beside the type does not.
    /// </summary>
    [LiveClass("InsideOwnerProbe")]
    public partial class InsideOwnerProbe
    {
        [LiveField(lane = FrameLane.State)] public float visible;

        [LiveField(lane = FrameLane.State)] protected float guarded;

        [LiveField(lane = FrameLane.State)] private float _hidden;
    }

    /// <summary>
    /// A base type that keeps its storage to itself, exposed all the same.
    ///
    /// Reflection reaches a base type's private field only by walking the hierarchy and asking each
    /// level for what it declares -- <c>Type.GetField</c> on the derived type does not return it.
    /// The lookup does that walk now, so this shape registers; before, it was a "Member not found".
    /// </summary>
    [LiveClass("PrivateBaseProbe")]
    public partial class PrivateBaseProbe
    {
        [LiveField(lane = FrameLane.State)] public float shared;

        /// <summary>Private to this type, and this type's block is inside it.</summary>
        [LiveField(lane = FrameLane.State)] private float _ownStorage;
    }

    /// <summary>
    /// Partial, so its block is inside it -- and it inherits a member that even the inside cannot
    /// name, because the inside of a derived type is not the inside of its base.
    /// </summary>
    [LiveClass("PrivateBaseDerivedProbe")]
    public partial class PrivateBaseDerivedProbe : PrivateBaseProbe
    {
        [LiveField(lane = FrameLane.State)] private float _ofItsOwn;
    }

    /// <summary>
    /// A type nested inside another, which used to be refused outright.
    ///
    /// Nesting stopped being a reason once the block moved beside the owner: a nested type is named
    /// through the types that contain it, and this one and its container are both nameable.
    /// </summary>
    public static class NestingProbe
    {
        [LiveClass("NestedStateProbe")]
        public class Inner
        {
            [LiveField(lane = FrameLane.State)] public float value;
        }
    }

    /// <summary>
    /// Where a type's state block is put, and what that costs it.
    ///
    /// Inside the owner it reaches everything the owner reaches and needs the owner to be partial.
    /// Beside the owner it needs nothing of the owner and reaches what the assembly reaches. The
    /// second is now the fallback rather than a refusal, which is what took the partial requirement
    /// off the exposed surface.
    /// </summary>
    [TestFixture]
    public class StateBlockPlacementTests
    {
        private static StateBridge Bridge<T>()
        {
            var bridge = StateBridgeRegistry.Find(typeof(T));
            Assert.IsNotNull(bridge, $"{typeof(T).Name} got no state bridge");
            return bridge;
        }

        [Test]
        public void ATypeThatIsNotPartial_StillGetsABridge()
        {
            Assert.IsFalse(typeof(BesideOwnerProbe).IsSealed && typeof(BesideOwnerProbe).IsAbstract,
                "the probe must stay an ordinary class for this to mean anything");

            Bridge<BesideOwnerProbe>();
        }

        [Test]
        public void APublicMemberOfANonPartialType_IsCarried()
        {
            Assert.IsTrue(Bridge<BesideOwnerProbe>().Carries(nameof(BesideOwnerProbe.visible)));
        }

        /// <summary>
        /// The generated half is compiled into the same assembly as the owner, so internal is in
        /// range. Only what crosses an assembly boundary is not.
        /// </summary>
        [Test]
        public void AnInternalMemberOfANonPartialType_IsCarried()
        {
            Assert.IsTrue(Bridge<BesideOwnerProbe>().Carries("alsoVisible"));
        }

        /// <summary>
        /// The cost of the trade, stated. A private field with nothing public in front of it is the
        /// one thing a block outside the owner cannot reach -- said with LRC009 rather than left to
        /// be noticed, because a member that quietly reaches neither lane is the failure this whole
        /// area keeps having.
        /// </summary>
        [Test]
        public void APrivateMemberOfANonPartialType_IsLeftOut()
        {
            Assert.IsFalse(Bridge<BesideOwnerProbe>().Carries("_hidden"),
                "a block beside the owner cannot touch its private members");
        }

        /// <summary>
        /// Why the cost is affordable. The convention in this codebase is a hidden field declaring
        /// the lane and a public property giving the value its behaviour, and the block already
        /// travels through the property -- so the usual shape survives the move outside.
        /// </summary>
        [Test]
        public void AShadowedFieldBehindAPublicProperty_IsStillCarried()
        {
            Assert.IsTrue(Bridge<BesideOwnerProbe>().Carries(nameof(BesideOwnerProbe.label)),
                "the pair travels through the property, which is public");
        }

        /// <summary>
        /// The regression guard. A partial owner keeps its block inside itself and keeps reaching
        /// its own private members -- the move outside is a fallback for types that gave nothing up
        /// to get it, not a change to what partial types can do.
        /// </summary>
        [Test]
        public void APartialType_StillCarriesItsPrivateMembers()
        {
            Assert.IsTrue(Bridge<FixedTextProbe>().Carries("_title"),
                "a partial owner still gets its block inside itself");
            Assert.IsTrue(Bridge<FixedTextProbe>().Carries("_weight"));
        }

        /// <summary>
        /// The member that tells the two vantage points apart. Inside the owner, protected is in
        /// reach like anything else the type can name.
        /// </summary>
        [Test]
        public void AProtectedMemberOfAPartialType_IsCarried()
        {
            Assert.IsTrue(Bridge<InsideOwnerProbe>().Carries("guarded"));
        }

        /// <summary>
        /// And beside the owner it is not: the movers are free functions in another namespace, not
        /// a derived type, so protected means nothing to them.
        /// </summary>
        [Test]
        public void AProtectedMemberOfANonPartialType_IsLeftOut()
        {
            Assert.IsFalse(Bridge<BesideOwnerProbe>().Carries("guarded"),
                "the movers are not a derived type, so protected is out of reach");
        }

        [Test]
        public void APrivateMemberOfAPartialType_IsCarried()
        {
            Assert.IsTrue(Bridge<InsideOwnerProbe>().Carries("_hidden"),
                "the block is inside the type, so its own privates are in reach");
        }

        /// <summary>
        /// Inside the owner is not unlimited reach either. A block generated inside a derived type
        /// is not the base type, so a private field of the base is out of reach there -- and it
        /// used to arrive as CS0122 in code nobody wrote.
        /// </summary>
        [Test]
        public void APrivateMemberOfABaseType_IsLeftOut()
        {
            Assert.IsFalse(Bridge<PrivateBaseDerivedProbe>().Carries("_ownStorage"),
                "the inside of a derived type is not the inside of its base");
        }

        [Test]
        public void TheBaseTypeCarriesThatSameMemberItself()
        {
            Assert.IsTrue(Bridge<PrivateBaseProbe>().Carries("_ownStorage"),
                "from inside itself the field is in reach; bridges are keyed on the exact type");
        }

        [Test]
        public void ADerivedTypesOwnPrivateMember_IsCarried()
        {
            Assert.IsTrue(Bridge<PrivateBaseDerivedProbe>().Carries("_ofItsOwn"));
            Assert.IsTrue(Bridge<PrivateBaseDerivedProbe>().Carries(nameof(PrivateBaseProbe.shared)),
                "and what it inherits publicly comes along");
        }

        /// <summary>
        /// Nesting used to be refused outright, because the block was a second half of the owner and
        /// the emitter had no way to reproduce the nesting around it. Beside the owner there is
        /// nothing to reproduce.
        /// </summary>
        [Test]
        public void ANestedType_GetsABridge()
        {
            Assert.IsTrue(Bridge<NestingProbe.Inner>().Carries(nameof(NestingProbe.Inner.value)));
        }
    }
}
