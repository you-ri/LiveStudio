// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lilium.RemoteControl.SourceGenerator
{
    /// <summary>
    /// One member carried in the state lane, and how to move it.
    /// </summary>
    sealed class StateMemberInfo
    {
        public string Name { get; }

        /// <summary>
        /// The type the block holds this member as, which is the member's own type for everything
        /// except text: a <c>string</c> keeps its face on the object and travels as a fixed number
        /// of bytes in the block.
        /// </summary>
        public string BlockTypeName { get; }

        /// <summary>Width of the fixed text this member travels as, or zero when it is not text.</summary>
        public int TextCapacity { get; }

        /// <summary>
        /// Whether writing this member runs a setter, which decides whether applying it is worth
        /// guarding: a field costs a store either way, a property costs whatever it was written to
        /// do.
        /// </summary>
        public bool IsProperty { get; }

        /// <summary>
        /// Method called after applying a recording changes this member, or null.
        ///
        /// The state lane carries values rather than the calls that produced them, which is what
        /// lets any frame stand on its own. A value that means "load this" still needs its effect
        /// produced somewhere, and a plain field has no setter to produce it in.
        /// </summary>
        public string AppliedCallback { get; }

        /// <summary>
        /// Whether this member travels as a symbol id rather than as text in the block.
        ///
        /// Text drawn from a set repeats unchanged on every frame, so the table holds it once for
        /// the whole recording and the block holds four bytes -- with no width for a value to
        /// outgrow, which is the part that matters.
        /// </summary>
        public bool IsTextTable { get; }

        /// <summary>
        /// The shape of this member's own fields, or zero when its type name already pins them.
        ///
        /// A recording matches its members to a build's by name and type, and for a primitive that
        /// is the whole story. For a struct declared elsewhere it is not: <c>TransformValue</c> can
        /// have its fields reordered and keep its name, its size and every appearance of being the
        /// same forty bytes, and copying it then puts a rotation where a position goes. Carried
        /// alongside the member so a reader can tell the two apart instead of reading one as the
        /// other -- which is the failure worth refusing, because it looks like values.
        /// </summary>
        public ulong Layout { get; }

        public StateMemberInfo(string name, string blockTypeName, int textCapacity = 0,
            bool isProperty = false, string appliedCallback = null, bool isTextTable = false,
            ulong layout = 0UL)
        {
            Name = name;
            BlockTypeName = blockTypeName;
            TextCapacity = textCapacity;
            IsProperty = isProperty;
            AppliedCallback = appliedCallback;
            IsTextTable = isTextTable;
            Layout = layout;
        }

        public override bool Equals(object obj)
            => obj is StateMemberInfo other && Name == other.Name
               && BlockTypeName == other.BlockTypeName && TextCapacity == other.TextCapacity
               && IsProperty == other.IsProperty && AppliedCallback == other.AppliedCallback
               && IsTextTable == other.IsTextTable && Layout == other.Layout;

        public override int GetHashCode()
            => ((((((Name?.GetHashCode() ?? 0) * 397 ^ (BlockTypeName?.GetHashCode() ?? 0)) * 397
                ^ TextCapacity) * 397 ^ (IsProperty ? 1 : 0)) * 397
                ^ (AppliedCallback?.GetHashCode() ?? 0)) * 397 ^ (IsTextTable ? 1 : 0)) * 397
                ^ Layout.GetHashCode();
    }

    /// <summary>
    /// What one type contributes to the state lane, plus anything that stopped it contributing.
    /// </summary>
    sealed class StateInfo
    {
        /// <summary>Namespace of the owner, or empty for the global namespace.</summary>
        public string Namespace { get; }

        /// <summary>Owner's own name, as it is declared.</summary>
        public string TypeName { get; }

        /// <summary>
        /// Owner's name with any types it is nested in, joined the way the runtime joins them
        /// (<c>Outer+Inner</c>). Namespace not included.
        /// </summary>
        public string NestedName { get; }

        /// <summary>Fully qualified owner, for the registration call.</summary>
        public string FullyQualifiedName { get; }

        /// <summary>"class" or "struct", so the generated half matches the declared half.</summary>
        public string TypeKeyword { get; }

        /// <summary>
        /// Whether the block goes inside the owner as a second half of it, or beside it.
        ///
        /// Inside reaches the owner's private members and needs the owner to be <c>partial</c>.
        /// Beside needs nothing of the owner and reaches only what the assembly can see.
        /// </summary>
        public bool InsideOwner { get; }

        public ImmutableArray<StateMemberInfo> Members { get; }

        /// <summary>Reasons this type carries nothing, reported as warnings rather than silence.</summary>
        public ImmutableArray<string> Problems { get; }

        /// <summary>
        /// Whether any member of this type said <c>lane = FrameLane.State</c> out loud.
        ///
        /// Decides who a diagnostic about the whole type is addressed to. With the lane defaulted,
        /// every exposed type is on the state lane whether or not anyone wanted it there, so a
        /// message that reads "your declaration is not being carried" has no one to be about unless
        /// someone declared.
        /// </summary>
        public bool AnyDeclared { get; }

        public StateInfo(string ns, string typeName, string nestedName, string fullyQualifiedName,
            string typeKeyword, bool insideOwner, bool anyDeclared,
            ImmutableArray<StateMemberInfo> members, ImmutableArray<string> problems)
        {
            Namespace = ns;
            TypeName = typeName;
            NestedName = nestedName;
            FullyQualifiedName = fullyQualifiedName;
            TypeKeyword = typeKeyword;
            InsideOwner = insideOwner;
            AnyDeclared = anyDeclared;
            Members = members;
            Problems = problems;
        }

        /// <summary>
        /// Name of the block type as the registration has to spell it: nested in the owner when the
        /// block is inside it, and a free type beside the owner when it is not.
        /// </summary>
        public string BlockReference => InsideOwner
            ? FullyQualifiedName + "." + StateBlockEmitter.kBlockTypeName
            : GeneratedNamespacePrefix + LocalName + StateBlockEmitter.kBlockTypeName;

        /// <summary>Where the two movers live, by the same rule.</summary>
        public string MoverReference => InsideOwner
            ? FullyQualifiedName
            : GeneratedNamespacePrefix + LocalName + "StateMover";

        /// <summary>
        /// The owner's runtime <c>Type.FullName</c>: what a recording calls this type's state.
        ///
        /// The owner rather than the block, so that where the block had to go -- inside the owner or
        /// beside it -- does not reach the recording. It used to: adding or removing <c>partial</c>
        /// renamed the state and dropped the whole type out of takes made by the other build.
        /// </summary>
        public string RuntimeFullName => string.IsNullOrEmpty(Namespace)
            ? NestedName
            : Namespace + "." + NestedName;

        /// <summary>
        /// The name a free type beside the owner is declared under.
        ///
        /// The owner's namespace carries the qualification, so only the nesting is flattened -- and
        /// only because C# has no way to spell a nested type as a namespace. Two types of the same
        /// name in two namespaces stay apart because their namespaces do.
        /// </summary>
        public string LocalName => NestedName.Replace('+', '_');

        /// <summary>Where a free type is addressed from, spelled for the registration line.</summary>
        public string GeneratedNamespacePrefix => string.IsNullOrEmpty(Namespace)
            ? "global::"
            : "global::" + Namespace + ".";

        public override bool Equals(object obj)
            => obj is StateInfo other
               && FullyQualifiedName == other.FullyQualifiedName
               && InsideOwner == other.InsideOwner
               && AnyDeclared == other.AnyDeclared
               && Members.SequenceEqual(other.Members)
               && Problems.SequenceEqual(other.Problems);

        public override int GetHashCode() => FullyQualifiedName?.GetHashCode() ?? 0;
    }

    /// <summary>
    /// Turns members declared <c>FrameLane.State</c> into a blittable block and the two functions
    /// that move an object in and out of it.
    ///
    /// Reading a few members off every exposed object sixty times a second is the case reflection
    /// handles worst, and it is exactly what the state lane asks for. What comes out of here is
    /// field assignments.
    ///
    /// The block goes **inside the owner** when the owner is <c>partial</c>, and **beside it** --
    /// a free type in the owner's own namespace -- when it is not. Inside is worth having because the
    /// convention in this codebase is a private field with the attribute on it, which only the
    /// inside can read; beside is what keeps <c>partial</c> from being a condition of appearing on
    /// the lane at all, and costs only the members an outsider cannot name (<c>LRC009</c>).
    /// </summary>
    static class StateBlockEmitter
    {
        public const string kBlockTypeName = "LiveStateBlock";

        /// <summary>
        /// Text widths a block can hold, smallest first. A declaration asking for something in
        /// between takes the next one up -- the alternative is refusing a width that would have
        /// worked, which teaches authors to pick from a list they should not have to know.
        /// </summary>
        static readonly int[] kTextCapacities = { 32, 64, 128, 256 };

        const string kFixedStringNamespace = "global::Lilium.RemoteControl.Frames.LiveFixedString";
        const string kTextIdType = "global::Lilium.RemoteControl.Frames.LiveTextId";
        const string kSymbolTableType = "global::Lilium.RemoteControl.Frames.FrameSymbolTable";
        public const string kCaptureMethodName = "CaptureLiveState";
        public const string kApplyMethodName = "ApplyLiveState";

        public static readonly DiagnosticDescriptor kMemberOutOfReach = new DiagnosticDescriptor(
            "LRC009",
            "State-lane member cannot be reached by the generated movers",
            "'{0}' is in the state lane but the generated movers cannot reach it: {1}. It was left out of the state block. Expose it through a property they can see, or leave the member in the event lane. Where the type is not nested, declaring it 'partial' puts the block inside it and widens what the movers reach.",
            "Lilium.RemoteControl",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Said rather than worked around. Carrying a shadow field in place of the property that gives it its value records whatever was last stored rather than what the object reports, which is how a recording came to hold a camera that never moved.");

        public static readonly DiagnosticDescriptor kValueTypeOwner = new DiagnosticDescriptor(
            "LRC012",
            "State-lane type is a value type",
            "'{0}' declares members in the state lane but is a {1}. The lane carries state for objects with an identity, and the bridge that moves it is declared for reference types only, so no state block was generated. Make it a class, or leave the members in the event lane.",
            "Lilium.RemoteControl",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Without this diagnostic the failure surfaces as CS0452 inside generated code the author never wrote.");

        public static readonly DiagnosticDescriptor kUnsupportedMember = new DiagnosticDescriptor(
            "LRC002",
            "State-lane member cannot be carried",
            "'{0}' is in the state lane but its type '{1}' is not unmanaged, so it was left out of the state block. Use an unmanaged type, or leave the member in the event lane.",
            "Lilium.RemoteControl",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor kTextTooWide = new DiagnosticDescriptor(
            "LRC006",
            "State-lane text is too wide",
            "'{0}' asks for {1} bytes of text in the state lane, which is more than the widest block text ({2}). It was left out of the state block. A value this long is better carried by the event lane, which pays for it only when it changes.",
            "Lilium.RemoteControl",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor kAppliedCallbackNotFound = new DiagnosticDescriptor(
            "LRC007",
            "onApplied names no method that can be called",
            "'{0}' declares onApplied = \"{1}\", but the type has no instance method by that name taking no arguments, returning void, and reachable from the type itself. The value is still carried; nothing is called.",
            "Lilium.RemoteControl",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "The generated half is emitted inside the owner, so a private method of a base class cannot be reached from it.");

        public static readonly DiagnosticDescriptor kMemberNotMovable = new DiagnosticDescriptor(
            "LRC008",
            "State-lane member cannot be both read and written",
            "'{0}' is in the state lane but {1}, so it was left out of the state block. The lane reads a value out of the object every frame and writes it back on replay, which needs both halves. Give it the missing half, or leave the member in the event lane.",
            "Lilium.RemoteControl",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Without this diagnostic the failure surfaces as CS0176/CS0191/CS0200 inside generated code the author never wrote.");

        public static readonly DiagnosticDescriptor kOwnerOutOfReach = new DiagnosticDescriptor(
            "LRC003",
            "State-lane type cannot be named where its block is generated",
            "'{0}' declares members in the state lane, but the block generated beside it cannot name it: {1}. Declare the type 'partial', widen its accessibility, or leave the members in the event lane.",
            "Lilium.RemoteControl",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Being nested is no longer a reason on its own -- a nested type is named through the types that contain it, and each of those has to be nameable too.");

        public static readonly DiagnosticDescriptor kMissingSimulationReference = new DiagnosticDescriptor(
            "LRC004",
            "State lane needs a reference to Lilium.RemoteControl.Simulation",
            "'{0}' declares members in the state lane, but this assembly does not reference 'Lilium.RemoteControl.Simulation'. The generated state block names types that live there. Add the reference to the assembly definition (.asmdef) and rebuild.",
            "Lilium.RemoteControl",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "The block is not emitted when the reference is missing, so nothing fails to compile -- stopping the output is what keeps CS0246 out of generated code the author never wrote. That leaves a declaration that silently does nothing, and this is the only thing that says so, which is why it is an error rather than a warning.");

        /// <summary>
        /// Collects what a type puts in the state lane. Null when it puts nothing there, which is
        /// the common case and costs nothing downstream.
        /// </summary>
        public static StateInfo Collect(INamedTypeSymbol typeSymbol, TypeDeclarationSyntax node,
            IEnumerable<INamedTypeSymbol> chain)
        {
            var levels = chain as IList<INamedTypeSymbol> ?? chain.ToList();

            // The whole type is off the frame, so there is nothing here to carry. Asked before the
            // members rather than after, because the answer is about the type: an undeclared field
            // would otherwise land in a block that the runtime has already decided is off the lane,
            // and the block and the declaration disagreeing is the failure this area keeps having.
            if (_DeclaresTypeOffFrame(typeSymbol)) return null;

            var members = ImmutableArray.CreateBuilder<StateMemberInfo>();
            var problems = ImmutableArray.CreateBuilder<string>();
            var seen = new HashSet<string>();
            var any = false;
            var anyDeclared = false;

            // The convention in this codebase is a hidden field holding the value and a property
            // giving it its behaviour. Both faces of such a pair are moved through the property:
            // the getter is what knows the current value (it may read something else entirely, as a
            // transform proxy reads the real Transform) and the setter is what makes a write land.
            // Taking the field instead would capture whatever was last stored and apply without the
            // effect, which for a proxy means a replay that writes values nothing acts on.
            var propertiesByLiveName = _LivePropertiesByLiveName(levels);
            var shadowedProperties = _ShadowedPropertyNames(levels, propertiesByLiveName);

            // Where the block will be put, which decides what the movers can touch.
            //
            // A partial type gets its half inside itself and reaches everything the type reaches --
            // including the private field the convention puts the attribute on. Everything else gets
            // its block beside it, and then only what the assembly can see is in range. That trade
            // is the whole of why the lane no longer demands 'partial' of every exposed type: 89
            // types declare one, 7 are partial, and requiring it of the rest would have made being
            // in the simulation a condition of being exposed at all.
            var isPartial = node.Modifiers.Any(SyntaxKind.PartialKeyword);
            var isNested = typeSymbol.ContainingType != null;
            var insideOwner = isPartial && !isNested;

            foreach (var level in levels)
            {
                foreach (var member in level.OriginalDefinition.GetMembers())
                {
                    if (!_TryReadStateMember(member, out var memberType, out var textCapacity,
                            out var appliedCallback, out var laneWasDeclared)) continue;

                    // Any member of this type having said "state" out loud makes the type's own
                    // problems (it is a struct, it cannot be named) addressed to someone.
                    if (laneWasDeclared) anyDeclared = true;

                    var name = member.Name;
                    var throughProperty = member is IPropertySymbol;

                    // What the generated movers will actually touch. The pair below redirects a
                    // field's declaration onto its property, and it is the property that has to
                    // stand up to being read and written -- checking the field would clear a
                    // getter-only property for a write the generated code cannot make.
                    var moved = member;

                    if (member is IFieldSymbol field
                        && _TryFindShadowedProperty(field, propertiesByLiveName, out var behind))
                    {
                        // The pair travels through the property or not at all. Falling back to the
                        // field when the property cannot be reached is what this used to do, and it
                        // is worse than carrying nothing: the field holds whatever was last stored
                        // in it, which for a shadow value is whatever the last save put there.
                        if (!_IsReachableFrom(behind, typeSymbol, insideOwner, out var behindOutOfReach))
                        {
                            problems.Add($"{level.Name}.{behind.Name}|not-reachable|"
                                + $"it is the value behind '{field.Name}', and {behindOutOfReach}|{laneWasDeclared}");
                            continue;
                        }

                        name = behind.Name;
                        memberType = behind.Type;
                        throughProperty = true;
                        moved = behind;
                    }
                    else if (member is IPropertySymbol && shadowedProperties.Contains(member.Name))
                    {
                        // Its field declares the lane -- the runtime reads it from there, and the
                        // two faces of one value must not end up in different lanes. Carrying the
                        // property as well would put the same value in the block twice.
                        continue;
                    }

                    if (!seen.Add(name)) continue;

                    any = true;

                    // Same reason as the callback below: the generated movers assign in both
                    // directions with no ceremony, so a member that cannot take one of them turns
                    // into a compile error inside code the author never wrote.
                    if (!_CanMoveBothWays(moved, out var obstacle))
                    {
                        problems.Add($"{level.Name}.{name}|not-movable|{obstacle}|{laneWasDeclared}");
                        continue;
                    }

                    // Out of reach of wherever the movers are written. Both vantage points have
                    // members they cannot name -- a base type's privates from inside, anything but
                    // public and internal from outside -- and silence here is the shape of the bug
                    // this whole area keeps having: a member that quietly reaches neither lane.
                    if (!_IsReachableByMovers(moved, typeSymbol, insideOwner, out var outOfReach))
                    {
                        problems.Add($"{level.Name}.{name}|not-reachable|{outOfReach}|{laneWasDeclared}");
                        continue;
                    }

                    // Checked here rather than left to the generated code, where a name that does
                    // not resolve becomes a compile error inside something the author never wrote.
                    if (appliedCallback != null
                        && !_HasAppliedCallback(typeSymbol, levels, appliedCallback, insideOwner))
                    {
                        problems.Add($"{level.Name}.{name}|applied-callback|{appliedCallback}|{laneWasDeclared}");
                        appliedCallback = null;
                    }

                    // Text is the one member whose block type is not its own. It keeps its string
                    // face on the object -- that is what REST answers and what the scene file holds
                    // -- and travels either as a symbol id or as a fixed number of UTF-8 bytes,
                    // because a reference is the one thing a block cannot hold.
                    if (memberType.SpecialType == SpecialType.System_String)
                    {
                        // The table is where text goes unless the declaration says otherwise. It
                        // holds each distinct value once for the whole recording and has no width a
                        // value can outgrow, so it is the only form that carries every string it is
                        // given -- which is what a default has to do. A [StringSelector] says the
                        // value comes from a set, which used to be what put it here; the default
                        // says the same thing for every string, so nothing has to. The opt-out is
                        // textCapacity, which buys a fixed slot in the block and with it a ceiling.
                        //
                        // What this costs: a member whose value keeps taking new shapes grows the
                        // table for as long as the session lasts, because it is never emptied within
                        // a run. Text like that belongs in the event lane, and saying so is now the
                        // declaration that has to be written -- an undecorated field is on the state
                        // lane (see the default a few screens down), and text no longer falls out of
                        // the block for want of a width.
                        if (textCapacity <= 0)
                        {
                            members.Add(new StateMemberInfo(
                                name, kTextIdType, textCapacity: 0, isProperty: throughProperty,
                                appliedCallback: appliedCallback, isTextTable: true));
                            continue;
                        }

                        var width = _TextWidthFor(textCapacity);
                        if (width == 0)
                        {
                            problems.Add($"{level.Name}.{name}|text-too-wide|{textCapacity}|{laneWasDeclared}");
                            continue;
                        }

                        members.Add(new StateMemberInfo(
                            name, kFixedStringNamespace + width, width, throughProperty, appliedCallback));
                        continue;
                    }

                    // The one rule for everything else. Asking the compiler rather than keeping a
                    // list of blessed types means enums, Vector3, Color and anyone's own struct all
                    // work without being named here, and arrays and Nullable are refused for the
                    // same reason -- they are not something a block can hold.
                    if (!memberType.IsUnmanagedType)
                    {
                        problems.Add($"{level.Name}.{name}|unmanaged|{memberType.ToDisplayString()}|{laneWasDeclared}");
                        continue;
                    }

                    members.Add(new StateMemberInfo(
                        name,
                        memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        textCapacity: 0,
                        isProperty: throughProperty,
                        appliedCallback: appliedCallback,
                        layout: _StructLayoutHash(memberType)));
                }
            }

            if (!any) return null;

            // A block beside the owner has to be able to say the owner's name, which for a nested
            // type means saying every name it is nested inside. Being nested is not the question --
            // being nameable is.
            if (!insideOwner && !_IsTypeNameable(typeSymbol, out var ownerOutOfReach))
            {
                problems.Add($"|owner-out-of-reach|{ownerOutOfReach}|{anyDeclared}");
            }

            // The bridge that carries a block is declared for reference types, so a struct owner
            // would fail at the registration line rather than here. Refused with the others so the
            // author is told in their own terms.
            if (typeSymbol.IsValueType) problems.Add($"|value-type|{typeSymbol.TypeKind.ToString().ToLowerInvariant()}|{anyDeclared}");

            var ns = typeSymbol.ContainingNamespace != null && !typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? typeSymbol.ContainingNamespace.ToDisplayString()
                : string.Empty;

            // The nesting chain, spelled the way the runtime spells it. A recording names this
            // type's state by its owner's Type.FullName, and reflection joins nested types with a
            // '+' where the compiler joins them with a '.'.
            var nested = typeSymbol.Name;
            for (var outer = typeSymbol.ContainingType; outer != null; outer = outer.ContainingType)
            {
                nested = outer.Name + "+" + nested;
            }

            return new StateInfo(
                ns,
                typeSymbol.Name,
                nested,
                typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                typeSymbol.IsValueType ? "struct" : "class",
                insideOwner,
                anyDeclared,
                members.ToImmutable(),
                problems.ToImmutable());
        }

        /// <summary>
        /// Whether the type has a method the generated apply could call by this name.
        ///
        /// An instance method taking nothing and returning nothing, reachable from the owner. The
        /// generated half lives inside the owner, so a private method of a base class is not.
        /// </summary>
        static bool _HasAppliedCallback(INamedTypeSymbol owner, IList<INamedTypeSymbol> levels, string name,
            bool insideOwner)
        {
            foreach (var level in levels)
            {
                foreach (var member in level.OriginalDefinition.GetMembers(name))
                {
                    if (!(member is IMethodSymbol method)) continue;
                    if (method.IsStatic || method.Parameters.Length > 0) continue;
                    if (!method.ReturnsVoid) continue;

                    // The reaction is called from wherever the apply was written, so it has to be
                    // in range from there -- which is a narrower question when that is beside the
                    // owner rather than inside it.
                    if (!_IsReachableByMovers(method, owner, insideOwner, out _)) continue;

                    return true;
                }
            }

            return false;
        }

        /// <summary>The block width that holds this many bytes of text, or zero when none does.</summary>
        static int _TextWidthFor(int requested)
        {
            foreach (var capacity in kTextCapacities)
            {
                if (requested <= capacity) return capacity;
            }

            return 0;
        }

        /// <summary>
        /// True when a member is declared in the state lane; hands back its type and, for text, the
        /// width its declaration asked for.
        /// </summary>
        static bool _TryReadStateMember(ISymbol member, out ITypeSymbol memberType, out int textCapacity,
            out string appliedCallback, out bool laneWasDeclared)
        {
            memberType = null;
            textCapacity = 0;
            appliedCallback = null;
            laneWasDeclared = false;

            AttributeData attribute = null;
            var isField = false;

            switch (member)
            {
                case IPropertySymbol property when !property.IsIndexer:
                    attribute = _FindAttribute(property, kLivePropertyAttribute);
                    memberType = property.Type;
                    break;

                case IFieldSymbol field when !field.IsConst:
                    attribute = _FindAttribute(field, kLiveFieldAttribute);
                    memberType = field.Type;
                    isField = true;
                    break;
            }

            if (attribute == null)
            {
                memberType = null;
                return false;
            }

            // A field with nothing said about its lane goes on the state lane: a field usually holds
            // a value something else drives, which is what the lane is for, and it is the same
            // default the asset-declared path has had since it was built. A property is usually
            // written from outside and stays where it was.
            //
            // ⚠ Whether the lane was said out loud is carried out of here, because it decides who a
            // diagnostic is addressed to. "Your declaration is not being carried" is the right thing
            // to tell someone who declared; to everyone else it is noise about a request they never
            // made, and every exposed member would make one.
            var isState = isField;

            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key == "lane" && named.Value.Value is int value)
                {
                    laneWasDeclared = true;
                    isState = value == 1;
                }
                else if (named.Key == "textCapacity" && named.Value.Value is int width) textCapacity = width;
                else if (named.Key == "onApplied" && named.Value.Value is string callback
                         && !string.IsNullOrEmpty(callback)) appliedCallback = callback;
            }

            if (isState) return true;

            memberType = null;
            textCapacity = 0;
            appliedCallback = null;
            laneWasDeclared = false;
            return false;
        }

        /// <summary>
        /// Whether the type's own <c>[LiveClass]</c> says the whole type is off the frame.
        ///
        /// Read from the type and its bases: a base that is a setting does not stop being one in a
        /// derived type. The runtime reads the same declaration the same way
        /// (<c>LiveClass.RegisterProperties</c>), which is what keeps the block and the lane
        /// agreeing about what is carried.
        /// </summary>
        static bool _DeclaresTypeOffFrame(INamedTypeSymbol typeSymbol)
        {
            for (var level = typeSymbol; level != null; level = level.BaseType)
            {
                var attribute = _FindAttribute(level.OriginalDefinition, kLiveClassAttribute);
                if (attribute == null) continue;

                foreach (var named in attribute.NamedArguments)
                {
                    // FrameLane.None == 2. Compared as the number the attribute stores, because a
                    // generator sees the constant's value rather than the name it was written with.
                    if (named.Key == "lane" && named.Value.Value is int lane && lane == 2) return true;
                }
            }

            return false;
        }

        static AttributeData _FindAttribute(ISymbol member, string attributeName)
        {
            foreach (var attr in member.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == attributeName) return attr;
            }

            return null;
        }

        const string kLivePropertyAttribute = "Lilium.RemoteControl.LivePropertyAttribute";
        const string kLiveFieldAttribute = "Lilium.RemoteControl.LiveFieldAttribute";
        const string kLiveClassAttribute = "Lilium.RemoteControl.LiveClassAttribute";
        const string kHideAttribute = "Lilium.RemoteControl.HideAttribute";
        const string kFormerlyNamedAsAttribute = "Lilium.RemoteControl.FormerlyNamedAsAttribute";

        /// <summary>
        /// Exposed properties of the whole chain, by the name they are exposed under.
        ///
        /// The exposed name rather than the member name, because that is what a shadow field names
        /// in its <c>[FormerlyNamedAs]</c> -- the same pairing rule the runtime applies when it
        /// decides which field stands behind which property.
        /// </summary>
        static Dictionary<string, IPropertySymbol> _LivePropertiesByLiveName(IList<INamedTypeSymbol> levels)
        {
            var result = new Dictionary<string, IPropertySymbol>();

            foreach (var level in levels)
            {
                foreach (var member in level.OriginalDefinition.GetMembers())
                {
                    if (!(member is IPropertySymbol property) || property.IsIndexer) continue;

                    var attribute = _FindAttribute(property, kLivePropertyAttribute);
                    if (attribute == null) continue;

                    result[_ExposedName(attribute, property.Name)] = property;
                }
            }

            return result;
        }

        /// <summary>Names of the properties some hidden field stands behind.</summary>
        static HashSet<string> _ShadowedPropertyNames(
            IList<INamedTypeSymbol> levels, Dictionary<string, IPropertySymbol> propertiesByLiveName)
        {
            var result = new HashSet<string>();

            foreach (var level in levels)
            {
                foreach (var member in level.OriginalDefinition.GetMembers())
                {
                    if (!(member is IFieldSymbol field) || field.IsConst) continue;
                    if (!_TryFindShadowedProperty(field, propertiesByLiveName, out var behind)) continue;

                    result.Add(behind.Name);
                }
            }

            return result;
        }

        /// <summary>
        /// The property a field is the hidden storage for, if it is one.
        ///
        /// Three things make a shadow field, all of them the runtime's rules: it is exposed, it is
        /// hidden from the UI, and it names an exposed property in <c>[FormerlyNamedAs]</c>.
        /// </summary>
        static bool _TryFindShadowedProperty(IFieldSymbol field,
            Dictionary<string, IPropertySymbol> propertiesByLiveName, out IPropertySymbol behind)
        {
            behind = null;

            if (_FindAttribute(field, kLiveFieldAttribute) == null) return false;
            if (_FindAttribute(field, kHideAttribute) == null) return false;

            foreach (var attribute in field.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != kFormerlyNamedAsAttribute) continue;
                if (attribute.ConstructorArguments.Length == 0) continue;
                if (!(attribute.ConstructorArguments[0].Value is string alias)) continue;

                if (propertiesByLiveName.TryGetValue(alias, out behind)) return true;
            }

            return false;
        }

        /// <summary>The name a member is exposed under: the one it was given, or its own.</summary>
        static string _ExposedName(AttributeData attribute, string memberName)
        {
            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key == "name" && named.Value.Value is string given) return given;
            }

            foreach (var argument in attribute.ConstructorArguments)
            {
                if (argument.Value is string given) return given;
            }

            return memberName;
        }

        /// <summary>
        /// Whether the generated half of <paramref name="owner"/> can name this property.
        ///
        /// The half is emitted inside the owner, so a private member of the owner itself is
        /// reachable but a private member of a base class is not. Getting this wrong shows up as a
        /// compile error inside code the author never wrote, so a property that cannot be reached
        /// is left to its field instead.
        /// </summary>
        static bool _IsReachableFrom(IPropertySymbol property, INamedTypeSymbol owner, bool insideOwner,
            out string reason)
        {
            reason = null;

            if (property.GetMethod == null || property.SetMethod == null)
            {
                reason = "it does not have both a getter and a setter";
                return false;
            }

            return _IsReachableByMovers(property, owner, insideOwner, out reason)
                   && _IsReachableByMovers(property.GetMethod, owner, insideOwner, out reason)
                   && _IsReachableByMovers(property.SetMethod, owner, insideOwner, out reason);
        }

        /// <summary>
        /// Whether a free type in another namespace can name this type at all.
        ///
        /// A nested type is named through the types that contain it, so each of those has to be
        /// nameable too -- one private outer class puts everything inside it out of reach. This is
        /// what used to be refused wholesale as "nested types are not supported": with the block
        /// generated beside the owner rather than inside it, nesting on its own costs nothing.
        /// </summary>
        static bool _IsTypeNameable(INamedTypeSymbol type, out string reason)
        {
            for (var level = type; level != null; level = level.ContainingType)
            {
                if (_IsReachableByMovers(level, level, insideOwner: false, out var why)) continue;

                reason = ReferenceEquals(level, type)
                    ? why
                    : $"the type it is nested in, '{level.Name}', is out of reach because {why}";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// Whether the generated movers can name this symbol, and why not when they cannot.
        ///
        /// Two vantage points, because the movers are written in two places. Inside the owner they
        /// see what the owner sees -- its own privates, its bases' protecteds -- and outside they
        /// see only what any other code in the assembly sees. The answer is not "is it private":
        /// a member inherited from a base type in another assembly answers by that assembly's
        /// rules, and a field with no modifier at all is private whether it looks it or not.
        ///
        /// Asked of the member that will actually be touched, and answered out loud. The silent
        /// version of this was a real defect: a shadow pair whose property could not be reached
        /// fell back to the field, which holds whatever was last written rather than what the
        /// object reports -- the shape of the recording that held a camera that never moved.
        /// </summary>
        static bool _IsReachableByMovers(ISymbol symbol, INamedTypeSymbol owner, bool insideOwner,
            out string reason)
        {
            reason = null;

            var sameAssembly = SymbolEqualityComparer.Default.Equals(
                symbol.ContainingAssembly, owner.ContainingAssembly);

            switch (symbol.DeclaredAccessibility)
            {
                case Accessibility.Public:
                    return true;

                case Accessibility.Internal:
                    if (sameAssembly) return true;
                    reason = "it is internal to another assembly";
                    return false;

                case Accessibility.ProtectedOrInternal:
                    // Either half is enough, and inside the owner the protected half applies.
                    if (sameAssembly || insideOwner) return true;
                    reason = "it is protected internal in another assembly, and the movers are not written inside a derived type";
                    return false;

                case Accessibility.Protected:
                    if (insideOwner) return true;
                    reason = "it is protected, and the movers are written beside the type rather than inside it";
                    return false;

                case Accessibility.ProtectedAndInternal:
                    if (insideOwner && sameAssembly) return true;
                    reason = insideOwner
                        ? "it is private protected in another assembly"
                        : "it is private protected, and the movers are written beside the type rather than inside it";
                    return false;

                case Accessibility.Private:
                    if (insideOwner && SymbolEqualityComparer.Default.Equals(
                            symbol.ContainingType?.OriginalDefinition, owner.OriginalDefinition)) return true;
                    reason = insideOwner
                        ? "it is private to a base type"
                        : "it is private, and the movers are written beside the type because the type is not partial";
                    return false;

                default:
                    reason = "it is not visible from there";
                    return false;
            }
        }

        /// <summary>
        /// Whether the generated movers can read this member out and write it back.
        ///
        /// The state lane is a round trip: capture reads the member into the block every frame, and
        /// apply writes it back on replay. A member that can only do one half cannot be on the lane,
        /// and until this was asked the answer arrived as a compile error inside generated code --
        /// CS0176 for a static reached through an instance, CS0191 for a readonly field, CS0200 for
        /// a property with no setter. Those name a line the author did not write.
        ///
        /// Read-only is also refused by the design rather than only by the compiler: a value the
        /// application computes and never takes back is a result, and replaying an application's own
        /// result then comparing against it agrees with itself (see "the boundary of an event").
        /// </summary>
        static bool _CanMoveBothWays(ISymbol member, out string obstacle)
        {
            obstacle = null;

            // One value for every instance, but the block has an element per object. Beyond the
            // compile error, carrying it would write the same value into every element and let any
            // one of them write it back.
            if (member.IsStatic)
            {
                obstacle = "is static, and the state lane carries a value per object";
                return false;
            }

            switch (member)
            {
                case IFieldSymbol field when field.IsReadOnly:
                    obstacle = "is a readonly field, so a replay has no way to write it back";
                    return false;

                case IPropertySymbol property:
                    if (property.GetMethod == null)
                    {
                        obstacle = "has no getter, so there is nothing to copy into the frame";
                        return false;
                    }

                    if (property.SetMethod == null)
                    {
                        obstacle = "has no setter, so a replay has no way to write it back";
                        return false;
                    }

                    // Assignable only inside an object initializer, which is not where a replay is.
                    if (property.SetMethod.IsInitOnly)
                    {
                        obstacle = "has an init-only setter, which a replay cannot assign through";
                        return false;
                    }

                    break;
            }

            return true;
        }

        /// <summary>Reports what stopped a type from carrying its state.</summary>
        public static void ReportProblems(SourceProductionContext context, StateInfo info)
        {
            foreach (var problem in info.Problems)
            {
                // member|code|detail|declared. The code is its own field rather than being read off
                // the detail, so a type name that happens to look like a code cannot be mistaken for
                // one, and the last field says who the message is addressed to.
                var split = problem.Split('|');
                if (split.Length != 4) continue;

                var severity = _SeverityFor(split[3]);

                switch (split[1])
                {
                    case "owner-out-of-reach":
                        context.ReportDiagnostic(Diagnostic.Create(kOwnerOutOfReach, Location.None, severity, null, null,
                            info.FullyQualifiedName, split[2]));
                        break;

                    case "value-type":
                        context.ReportDiagnostic(Diagnostic.Create(kValueTypeOwner, Location.None, severity, null, null,
                            info.FullyQualifiedName, split[2]));
                        break;

                    case "not-reachable":
                        context.ReportDiagnostic(Diagnostic.Create(kMemberOutOfReach, Location.None, severity, null, null,
                            split[0], split[2], info.FullyQualifiedName));
                        break;

                    case "applied-callback":
                        context.ReportDiagnostic(Diagnostic.Create(kAppliedCallbackNotFound, Location.None, severity, null, null,
                            split[0], split[2]));
                        break;

                    case "text-too-wide":
                        context.ReportDiagnostic(Diagnostic.Create(kTextTooWide, Location.None, severity, null, null,
                            split[0], split[2], kTextCapacities[kTextCapacities.Length - 1]));
                        break;

                    case "not-movable":
                        context.ReportDiagnostic(Diagnostic.Create(kMemberNotMovable, Location.None, severity, null, null,
                            split[0], split[2]));
                        break;

                    default:
                        context.ReportDiagnostic(Diagnostic.Create(kUnsupportedMember, Location.None, severity, null, null, split[0], split[2]));
                        break;
                }
            }
        }

        /// <summary>
        /// How loudly to say a member was left out, which is a question about who is being told.
        ///
        /// "Your declaration is not being carried" is the right thing to say to someone who wrote
        /// the declaration. To everyone else it is noise about a request they never made -- and
        /// with the lane defaulted, every exposed member makes one. So a member that said "state"
        /// out loud keeps the full warning, and one that merely fell into it is recorded quietly:
        /// still there for anyone who goes looking, absent from the build log.
        ///
        /// Kept rather than dropped, because it is the answer to "why is this not in my take".
        /// </summary>
        static DiagnosticSeverity _SeverityFor(string declaredFlag)
            => SeverityFor(string.Equals(declaredFlag, "True", System.StringComparison.OrdinalIgnoreCase),
                DiagnosticSeverity.Warning);

        /// <summary>
        /// The same rule where the caller already knows the answer, and for a diagnostic whose
        /// declared-severity is not Warning.
        ///
        /// <c>LRC004</c> is the one reported from outside this file, and leaving it out of the rule
        /// is what made it the only diagnostic that broke a build over a request nobody made: it
        /// stops no compile (the block is simply not emitted), so as an error it exists purely to
        /// say a declaration did nothing. With the lane defaulted there is no declaration to speak
        /// of, and the member falls to the event lane exactly as it does today.
        /// </summary>
        public static DiagnosticSeverity SeverityFor(bool declared, DiagnosticSeverity whenDeclared)
            => declared ? whenDeclared : DiagnosticSeverity.Info;

        /// <summary>
        /// True when this type can actually have a block emitted for it.
        ///
        /// Not being <c>partial</c> is no longer among the reasons it cannot: such a type gets its
        /// block beside itself instead, having already given up the members that only the inside
        /// could reach (<c>LRC009</c>).
        /// </summary>
        public static bool CanEmit(StateInfo info)
        {
            if (info.Members.Length == 0) return false;

            foreach (var problem in info.Problems)
            {
                if (problem.Contains("|owner-out-of-reach|")) return false;
                if (problem.Contains("|value-type|")) return false;
            }

            return true;
        }

        /// <summary>Emits the block and the two movers, inside a second half of the owner.</summary>
        public static void EmitOwnerHalf(StringBuilder sb, StateInfo info)
        {
            if (!info.InsideOwner)
            {
                _EmitBesideOwner(sb, info);
                return;
            }

            var indent = string.IsNullOrEmpty(info.Namespace) ? "    " : "        ";

            if (!string.IsNullOrEmpty(info.Namespace))
            {
                sb.AppendLine($"namespace {info.Namespace}");
                sb.AppendLine("{");
                sb.AppendLine($"    partial {info.TypeKeyword} {info.TypeName}");
                sb.AppendLine("    {");
            }
            else
            {
                sb.AppendLine($"partial {info.TypeKeyword} {info.TypeName}");
                sb.AppendLine("{");
            }

            _EmitBlockStruct(sb, info, indent, kBlockTypeName);
            sb.AppendLine();
            _EmitCapture(sb, info, indent, info.TypeName, kBlockTypeName, kCaptureMethodName);
            sb.AppendLine();
            _EmitApply(sb, info, indent, info.TypeName, kBlockTypeName, kApplyMethodName);

            if (!string.IsNullOrEmpty(info.Namespace))
            {
                sb.AppendLine("    }");
                sb.AppendLine("}");
            }
            else
            {
                sb.AppendLine("}");
            }

            sb.AppendLine();
        }

        /// <summary>
        /// Emits the call that turns a changed value into whatever it is supposed to mean.
        ///
        /// After the write rather than before, so the reaction reads the value it is reacting to
        /// straight off the member -- which is also where a live write would have left it, making
        /// the two paths reach the reaction the same way.
        /// </summary>
        /// <summary>
        /// Emits the block and the two movers beside the owner instead of inside it.
        ///
        /// Same three pieces, same bodies -- only the address changes, from members of the owner to
        /// free types in the owner's own namespace. What this buys is that the owner needs no second
        /// half, and so needs not be <c>partial</c>. What it costs is reach: everything here is
        /// written as an outsider, so the members that got this far are the ones an outsider can
        /// touch (see <c>LRC009</c>).
        ///
        /// The owner's namespace rather than a generated one of our own, so the names read as the
        /// owner's (<c>Lilium.LiveStudio.MeshStateLiveStateBlock</c>) instead of carrying the whole
        /// namespace flattened into one identifier. Two types of the same name in two namespaces
        /// stay apart because their namespaces do.
        /// </summary>
        static void _EmitBesideOwner(StringBuilder sb, StateInfo info)
        {
            var hasNamespace = !string.IsNullOrEmpty(info.Namespace);
            var indent = hasNamespace ? "    " : string.Empty;
            var block = info.LocalName + kBlockTypeName;
            var mover = info.LocalName + "StateMover";

            if (hasNamespace)
            {
                sb.AppendLine($"namespace {info.Namespace}");
                sb.AppendLine("{");
            }

            _EmitBlockStruct(sb, info, indent, block);
            sb.AppendLine();

            sb.AppendLine($"{indent}/// <summary>Moves <see cref=\"{info.FullyQualifiedName}\"/> in and out of its block.</summary>");
            sb.AppendLine($"{indent}[global::System.CodeDom.Compiler.GeneratedCode(\"Lilium.RemoteControl.SourceGenerator\", \"1.0\")]");
            sb.AppendLine($"{indent}internal static class {mover}");
            sb.AppendLine($"{indent}{{");

            _EmitCapture(sb, info, indent + "    ", info.FullyQualifiedName, block, kCaptureMethodName);
            sb.AppendLine();
            _EmitApply(sb, info, indent + "    ", info.FullyQualifiedName, block, kApplyMethodName);

            sb.AppendLine($"{indent}}}");
            if (hasNamespace) sb.AppendLine("}");
            sb.AppendLine();
        }

        /// <summary>The block: one field per member that reached it, in declaration order.</summary>
        static void _EmitBlockStruct(StringBuilder sb, StateInfo info, string indent, string blockTypeName)
        {
            sb.AppendLine($"{indent}/// <summary>This type's state-lane members, laid out for a frame to carry.</summary>");
            sb.AppendLine($"{indent}[global::System.CodeDom.Compiler.GeneratedCode(\"Lilium.RemoteControl.SourceGenerator\", \"1.0\")]");
            sb.AppendLine($"{indent}public struct {blockTypeName}");
            sb.AppendLine($"{indent}{{");
            foreach (var member in info.Members)
            {
                sb.AppendLine($"{indent}    public {member.BlockTypeName} {member.Name};");
            }
            sb.AppendLine($"{indent}}}");
        }

        /// <summary>Reading the object into its block. Straight assignment, which is the point.</summary>
        static void _EmitCapture(StringBuilder sb, StateInfo info, string indent, string ownerRef,
            string blockRef, string methodName)
        {
            sb.AppendLine($"{indent}[global::System.CodeDom.Compiler.GeneratedCode(\"Lilium.RemoteControl.SourceGenerator\", \"1.0\")]");
            sb.AppendLine($"{indent}internal static void {methodName}({ownerRef} source, ref {blockRef} block, {kSymbolTableType} symbols)");
            sb.AppendLine($"{indent}{{");
            foreach (var member in info.Members)
            {
                if (member.IsTextTable)
                {
                    sb.AppendLine($"{indent}    block.{member.Name} = {kTextIdType}.From(source.{member.Name}, symbols);");
                    continue;
                }

                if (member.TextCapacity > 0)
                {
                    sb.AppendLine($"{indent}    block.{member.Name} = {member.BlockTypeName}.From(source.{member.Name});");
                    continue;
                }

                sb.AppendLine($"{indent}    block.{member.Name} = source.{member.Name};");
            }
            sb.AppendLine($"{indent}}}");
        }

        /// <summary>Writing the block back onto the object, guarded where a write costs something.</summary>
        static void _EmitApply(StringBuilder sb, StateInfo info, string indent, string ownerRef,
            string blockRef, string methodName)
        {
            sb.AppendLine($"{indent}[global::System.CodeDom.Compiler.GeneratedCode(\"Lilium.RemoteControl.SourceGenerator\", \"1.0\")]");

            // The mask says which members the block was actually filled for. Every one of them,
            // unless a recording made before this type changed filled it -- there, a member the take
            // never carried has nothing but zero sitting in its place, and zero is a value like any
            // other. Writing it would empty a name, put a field of view at zero, and turn an
            // override whose "none" is minus one into an override with zero. Not writing it is the
            // one rule that gets all of those right at once.
            sb.AppendLine($"{indent}internal static void {methodName}(in {blockRef} block, {ownerRef} target, {kSymbolTableType} symbols, ulong mask)");
            sb.AppendLine($"{indent}{{");

            // Sixty-four is what one mask can speak for, and no type in practice comes near it. A
            // member past that is written unconditionally, which stays correct because the reader
            // refuses to build a partial plan for a type this wide and falls back to all-or-nothing.
            if (info.Members.Length > StateReadPlanMaskWidth)
            {
                sb.AppendLine($"{indent}#warning This type has more than {StateReadPlanMaskWidth} state-lane members. A recording made from a different build is refused whole rather than read member by member.");
            }

            var memberIndex = -1;
            foreach (var member in info.Members)
            {
                memberIndex++;

                var masked = memberIndex < StateReadPlanMaskWidth;
                var indent2 = masked ? indent + "    " : indent;

                if (masked)
                {
                    sb.AppendLine($"{indent}    if ((mask & 0x{(1UL << memberIndex):x}UL) != 0)");
                    sb.AppendLine($"{indent}    {{");
                }

                _EmitApplyMember(sb, member, indent2);

                if (masked) sb.AppendLine($"{indent}    }}");
            }

            sb.AppendLine($"{indent}}}");
        }

        /// <summary>Most members one mask can speak for. Matches StateReadPlan.kMaxMaskedMembers.</summary>
        const int StateReadPlanMaskWidth = 64;

        static void _EmitApplyMember(StringBuilder sb, StateMemberInfo member, string indent)
        {
            if (member.IsTextTable)
            {
                // Asked rather than assigned, for the same two reasons as a fixed width, plus
                // one of its own: an id the table cannot resolve says nothing, and writing the
                // nothing would clear a member that a short file merely failed to mention.
                var idLocal = $"__liveState{member.Name}";
                sb.AppendLine($"{indent}    if (block.{member.Name}.TryGetValue(target.{member.Name}, symbols, out var {idLocal}))");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        target.{member.Name} = {idLocal};");
                _EmitAppliedCallback(sb, indent, member);
                sb.AppendLine($"{indent}    }}");
                return;
            }

            if (member.TextCapacity > 0)
            {
                // Asked rather than assigned, for two reasons that happen to want the same
                // call: a value that outgrew its width says nothing and must not overwrite what
                // is there, and a value that has not changed must not run the setter again --
                // sixty times a second, a setter behind an asset reference answers by loading.
                var local = $"__liveState{member.Name}";
                sb.AppendLine($"{indent}    if (block.{member.Name}.TryGetValue(target.{member.Name}, out var {local}))");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        target.{member.Name} = {local};");
                _EmitAppliedCallback(sb, indent, member);
                sb.AppendLine($"{indent}    }}");
                return;
            }

            if (member.IsProperty || member.AppliedCallback != null)
            {
                // Guarded for two overlapping reasons. A setter can do anything -- pair a
                // device, load an asset, tell whatever watches -- and the state lane says this
                // value every frame whether or not it moved, so asking first is what keeps a
                // replay from running all of that sixty times a second for a value standing
                // still. A declared reaction wants the same question answered: it is the frame
                // the value moved on that it is interested in, not every frame after.
                sb.AppendLine($"{indent}    if (!global::Lilium.RemoteControl.Frames.LiveStateValue.SameBytes(target.{member.Name}, block.{member.Name}))");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        target.{member.Name} = block.{member.Name};");
                _EmitAppliedCallback(sb, indent, member);
                sb.AppendLine($"{indent}    }}");
                return;
            }

            sb.AppendLine($"{indent}    target.{member.Name} = block.{member.Name};");
        }

        static void _EmitAppliedCallback(StringBuilder sb, string indent, StateMemberInfo member)
        {
            if (member.AppliedCallback == null) return;

            sb.AppendLine($"{indent}        target.{member.AppliedCallback}();");
        }

        /// <summary>Emits the registration line for one type.</summary>
        public static void EmitRegistration(StringBuilder sb, StateInfo info)
        {
            sb.Append("            global::Lilium.RemoteControl.Frames.StateBridgeRegistry.Register<");
            sb.Append(info.FullyQualifiedName);
            sb.Append(", ");
            sb.Append(info.BlockReference);
            sb.Append(">(");
            sb.Append(info.MoverReference);
            sb.Append('.');
            sb.Append(kCaptureMethodName);
            sb.Append(", ");
            sb.Append(info.MoverReference);
            sb.Append('.');
            sb.Append(kApplyMethodName);

            // The members that actually reached the block, so the runtime can tell "asked for the
            // state lane" from "carried by it". A member the generator turned away (no width for
            // its text, a type that is not unmanaged) is absent here, which is how the write path
            // learns to keep recording it as an event.
            foreach (var member in info.Members)
            {
                sb.Append(", \"");
                sb.Append(member.Name);
                sb.Append('"');
            }

            sb.AppendLine(");");

            // What the block holds, member by member, so a recording made before this type changed
            // can still be read: the members both builds have are put back where they now live, and
            // the ones only one build has are left out rather than taking the whole type down.
            //
            // Only the names and the shapes go out from here. Where each member sits and how wide it
            // is are read off the block struct at load (see StateSchemaBuilder), because the struct
            // decides those and asking it cannot disagree with it -- and because working them out
            // here would mean emitting pointer arithmetic into assemblies that do not all allow it.
            sb.Append("            global::Lilium.RemoteControl.Frames.StateSchemaRegistry.Declare(\"");
            sb.Append(info.RuntimeFullName);
            sb.AppendLine("\",");
            sb.Append("                global::Lilium.RemoteControl.Frames.StateSchemaBuilder.For<");
            sb.Append(info.BlockReference);
            sb.AppendLine(">(");
            sb.AppendLine("                    new global::Lilium.RemoteControl.Frames.StateSchemaMemberSpec[]");
            sb.AppendLine("                    {");

            foreach (var member in info.Members)
            {
                sb.Append("                        new global::Lilium.RemoteControl.Frames.StateSchemaMemberSpec(\"");
                sb.Append(member.Name);
                sb.Append("\", 0x");
                sb.Append(member.Layout.ToString("x"));
                sb.AppendLine("UL),");
            }

            sb.AppendLine("                    }));");
        }

        /// <summary>
        /// The shape of a struct's own fields, or zero when its name already pins them.
        ///
        /// Zero for the types whose layout cannot move without their name moving too: the
        /// primitives, and an enum, which is its underlying integer however many names it has. For
        /// anything else the fields are walked in order -- recursively, because a struct of structs
        /// hides the same change one level further down.
        ///
        /// This exists because the match a recording makes is by name and type, and for these types
        /// that is not enough to be safe. Two builds that disagree about the order of two fields
        /// inside <c>TransformValue</c> produce a member of the same name, the same type name and
        /// the same size; copying one as the other lands each field on its neighbour and looks like
        /// values rather than like an error.
        /// </summary>
        static ulong _StructLayoutHash(ITypeSymbol type)
        {
            if (type == null) return 0UL;
            if (type.TypeKind == TypeKind.Enum) return 0UL;
            if (type.SpecialType != SpecialType.None) return 0UL;
            if (type.TypeKind != TypeKind.Struct) return 0UL;

            var hash = 14695981039346656037UL;
            _MixStruct(ref hash, type, depth: 0);
            return hash;
        }

        static void _MixStruct(ref ulong hash, ITypeSymbol type, int depth)
        {
            // A struct cannot contain itself, so this cannot run away on a well-formed type. The
            // bound is for a symbol graph that is not well formed -- a half-typed edit, a reference
            // that did not resolve -- where the generator still has to finish.
            if (depth > 8) return;

            foreach (var member in type.GetMembers())
            {
                if (!(member is IFieldSymbol field) || field.IsStatic || field.IsConst) continue;

                hash = _Mix(hash, field.Name);
                hash = _Mix(hash, field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

                if (field.Type.TypeKind == TypeKind.Struct && field.Type.SpecialType == SpecialType.None)
                {
                    _MixStruct(ref hash, field.Type, depth + 1);
                }
            }
        }

        static ulong _Mix(ulong hash, string text)
        {
            if (text == null) return hash;

            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 1099511628211UL;
            }

            // Kept apart from the next field, so "ab" + "c" and "a" + "bc" do not agree.
            hash ^= 0xFF;
            hash *= 1099511628211UL;
            return hash;
        }
    }
}
