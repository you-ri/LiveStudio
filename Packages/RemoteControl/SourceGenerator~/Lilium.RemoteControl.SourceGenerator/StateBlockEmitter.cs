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

        public StateMemberInfo(string name, string blockTypeName, int textCapacity = 0,
            bool isProperty = false, string appliedCallback = null)
        {
            Name = name;
            BlockTypeName = blockTypeName;
            TextCapacity = textCapacity;
            IsProperty = isProperty;
            AppliedCallback = appliedCallback;
        }

        public override bool Equals(object obj)
            => obj is StateMemberInfo other && Name == other.Name
               && BlockTypeName == other.BlockTypeName && TextCapacity == other.TextCapacity
               && IsProperty == other.IsProperty && AppliedCallback == other.AppliedCallback;

        public override int GetHashCode()
            => ((((Name?.GetHashCode() ?? 0) * 397 ^ (BlockTypeName?.GetHashCode() ?? 0)) * 397
                ^ TextCapacity) * 397 ^ (IsProperty ? 1 : 0)) * 397
                ^ (AppliedCallback?.GetHashCode() ?? 0);
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

        /// <summary>Fully qualified owner, for the registration call.</summary>
        public string FullyQualifiedName { get; }

        /// <summary>"class" or "struct", so the generated half matches the declared half.</summary>
        public string TypeKeyword { get; }

        public ImmutableArray<StateMemberInfo> Members { get; }

        /// <summary>Reasons this type carries nothing, reported as warnings rather than silence.</summary>
        public ImmutableArray<string> Problems { get; }

        public StateInfo(string ns, string typeName, string fullyQualifiedName, string typeKeyword,
            ImmutableArray<StateMemberInfo> members, ImmutableArray<string> problems)
        {
            Namespace = ns;
            TypeName = typeName;
            FullyQualifiedName = fullyQualifiedName;
            TypeKeyword = typeKeyword;
            Members = members;
            Problems = problems;
        }

        public override bool Equals(object obj)
            => obj is StateInfo other
               && FullyQualifiedName == other.FullyQualifiedName
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
    /// The block is emitted **inside the owner**, which is why the owner has to be declared
    /// <c>partial</c>: the convention in this codebase is a private field with the attribute on it,
    /// and a free function could not read one.
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
        public const string kCaptureMethodName = "CaptureLiveState";
        public const string kApplyMethodName = "ApplyLiveState";

        public static readonly DiagnosticDescriptor kNotPartial = new DiagnosticDescriptor(
            "LRC001",
            "State-lane type is not partial",
            "'{0}' declares members in the state lane but is not partial, so no state block was generated. Add 'partial' to the declaration.",
            "Lilium.RemoteControl",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor kUnsupportedMember = new DiagnosticDescriptor(
            "LRC002",
            "State-lane member cannot be carried",
            "'{0}' is in the state lane but its type '{1}' is not unmanaged, so it was left out of the state block. Use an unmanaged type, or leave the member in the input lane.",
            "Lilium.RemoteControl",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor kTextNeedsCapacity = new DiagnosticDescriptor(
            "LRC005",
            "State-lane text needs a width",
            "'{0}' is a string in the state lane but declares no textCapacity, so it was left out of the state block. The state lane holds a fixed width per member: set textCapacity to the longest value in UTF-8 bytes, or leave the member in the event lane.",
            "Lilium.RemoteControl",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "There is no default width. A bound is a claim about the values the member will hold, and only its author can make it.");

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

        public static readonly DiagnosticDescriptor kNestedType = new DiagnosticDescriptor(
            "LRC003",
            "State-lane type is nested",
            "'{0}' declares members in the state lane but is a nested type, which is not supported yet. Move it out, or leave the members in the input lane.",
            "Lilium.RemoteControl",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor kMissingSimulationReference = new DiagnosticDescriptor(
            "LRC004",
            "State lane needs a reference to Lilium.RemoteControl.Simulation",
            "'{0}' declares members in the state lane, but this assembly does not reference 'Lilium.RemoteControl.Simulation'. The generated state block names types that live there. Add the reference to the assembly definition (.asmdef) and rebuild.",
            "Lilium.RemoteControl",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Without this diagnostic the failure surfaces as CS0246 inside generated code the author never wrote.");

        /// <summary>
        /// Collects what a type puts in the state lane. Null when it puts nothing there, which is
        /// the common case and costs nothing downstream.
        /// </summary>
        public static StateInfo Collect(INamedTypeSymbol typeSymbol, TypeDeclarationSyntax node,
            IEnumerable<INamedTypeSymbol> chain)
        {
            var levels = chain as IList<INamedTypeSymbol> ?? chain.ToList();
            var members = ImmutableArray.CreateBuilder<StateMemberInfo>();
            var problems = ImmutableArray.CreateBuilder<string>();
            var seen = new HashSet<string>();
            var any = false;

            // The convention in this codebase is a hidden field holding the value and a property
            // giving it its behaviour. Both faces of such a pair are moved through the property:
            // the getter is what knows the current value (it may read something else entirely, as a
            // transform proxy reads the real Transform) and the setter is what makes a write land.
            // Taking the field instead would capture whatever was last stored and apply without the
            // effect, which for a proxy means a replay that writes values nothing acts on.
            var propertiesByLiveName = _LivePropertiesByLiveName(levels);
            var shadowedProperties = _ShadowedPropertyNames(levels, propertiesByLiveName);

            foreach (var level in levels)
            {
                foreach (var member in level.OriginalDefinition.GetMembers())
                {
                    if (!_TryReadStateMember(member, out var memberType, out var textCapacity,
                            out var appliedCallback)) continue;

                    var name = member.Name;
                    var throughProperty = member is IPropertySymbol;

                    if (member is IFieldSymbol field
                        && _TryFindShadowedProperty(field, propertiesByLiveName, out var behind)
                        && _IsReachableFrom(behind, typeSymbol))
                    {
                        name = behind.Name;
                        memberType = behind.Type;
                        throughProperty = true;
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

                    // Checked here rather than left to the generated code, where a name that does
                    // not resolve becomes a compile error inside something the author never wrote.
                    if (appliedCallback != null && !_HasAppliedCallback(typeSymbol, levels, appliedCallback))
                    {
                        problems.Add($"{level.Name}.{name}|applied-callback|{appliedCallback}");
                        appliedCallback = null;
                    }

                    // Text is the one member whose block type is not its own. It keeps its string
                    // face on the object -- that is what REST answers and what the scene file holds
                    // -- and travels as a fixed number of UTF-8 bytes, because a reference is the
                    // one thing a block cannot hold. The width is the author's claim about the
                    // values, so there is no default: without it the member stays where it was.
                    if (memberType.SpecialType == SpecialType.System_String)
                    {
                        if (textCapacity <= 0)
                        {
                            problems.Add($"{level.Name}.{name}|text-no-capacity|");
                            continue;
                        }

                        var width = _TextWidthFor(textCapacity);
                        if (width == 0)
                        {
                            problems.Add($"{level.Name}.{name}|text-too-wide|{textCapacity}");
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
                        problems.Add($"{level.Name}.{name}|unmanaged|{memberType.ToDisplayString()}");
                        continue;
                    }

                    members.Add(new StateMemberInfo(
                        name,
                        memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        textCapacity: 0,
                        isProperty: throughProperty,
                        appliedCallback: appliedCallback));
                }
            }

            if (!any) return null;

            var isPartial = node.Modifiers.Any(SyntaxKind.PartialKeyword);
            var isNested = typeSymbol.ContainingType != null;

            if (isNested) problems.Add("|nested|");
            else if (!isPartial) problems.Add("|not-partial|");

            var ns = typeSymbol.ContainingNamespace != null && !typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? typeSymbol.ContainingNamespace.ToDisplayString()
                : string.Empty;

            return new StateInfo(
                ns,
                typeSymbol.Name,
                typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                typeSymbol.IsValueType ? "struct" : "class",
                members.ToImmutable(),
                problems.ToImmutable());
        }

        /// <summary>
        /// Whether the type has a method the generated apply could call by this name.
        ///
        /// An instance method taking nothing and returning nothing, reachable from the owner. The
        /// generated half lives inside the owner, so a private method of a base class is not.
        /// </summary>
        static bool _HasAppliedCallback(INamedTypeSymbol owner, IList<INamedTypeSymbol> levels, string name)
        {
            foreach (var level in levels)
            {
                foreach (var member in level.OriginalDefinition.GetMembers(name))
                {
                    if (!(member is IMethodSymbol method)) continue;
                    if (method.IsStatic || method.Parameters.Length > 0) continue;
                    if (!method.ReturnsVoid) continue;
                    if (!_IsAccessible(method, owner)) continue;

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
            out string appliedCallback)
        {
            memberType = null;
            textCapacity = 0;
            appliedCallback = null;

            AttributeData attribute = null;
            switch (member)
            {
                case IPropertySymbol property when !property.IsIndexer:
                    attribute = _FindAttribute(property, kLivePropertyAttribute);
                    memberType = property.Type;
                    break;

                case IFieldSymbol field when !field.IsConst:
                    attribute = _FindAttribute(field, kLiveFieldAttribute);
                    memberType = field.Type;
                    break;
            }

            if (attribute == null)
            {
                memberType = null;
                return false;
            }

            // lane = FrameLane.State is 1. Anything else -- absent, Event, None -- is not our business.
            var isState = false;
            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key == "lane" && named.Value.Value is int value && value == 1) isState = true;
                else if (named.Key == "textCapacity" && named.Value.Value is int width) textCapacity = width;
                else if (named.Key == "onApplied" && named.Value.Value is string callback
                         && !string.IsNullOrEmpty(callback)) appliedCallback = callback;
            }

            if (isState) return true;

            memberType = null;
            textCapacity = 0;
            appliedCallback = null;
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
        static bool _IsReachableFrom(IPropertySymbol property, INamedTypeSymbol owner)
        {
            if (property.GetMethod == null || property.SetMethod == null) return false;

            return _IsAccessible(property, owner)
                   && _IsAccessible(property.GetMethod, owner)
                   && _IsAccessible(property.SetMethod, owner);
        }

        static bool _IsAccessible(ISymbol symbol, INamedTypeSymbol owner)
        {
            if (symbol.DeclaredAccessibility != Accessibility.Private) return true;

            return SymbolEqualityComparer.Default.Equals(
                symbol.ContainingType?.OriginalDefinition, owner.OriginalDefinition);
        }

        /// <summary>Reports what stopped a type from carrying its state.</summary>
        public static void ReportProblems(SourceProductionContext context, StateInfo info)
        {
            foreach (var problem in info.Problems)
            {
                // member|code|detail. The code is its own field rather than being read off the
                // detail, so a type name that happens to look like a code cannot be mistaken for one.
                var split = problem.Split('|');
                if (split.Length != 3) continue;

                switch (split[1])
                {
                    case "nested":
                        context.ReportDiagnostic(Diagnostic.Create(kNestedType, Location.None, info.FullyQualifiedName));
                        break;

                    case "not-partial":
                        context.ReportDiagnostic(Diagnostic.Create(kNotPartial, Location.None, info.FullyQualifiedName));
                        break;

                    case "text-no-capacity":
                        context.ReportDiagnostic(Diagnostic.Create(kTextNeedsCapacity, Location.None, split[0]));
                        break;

                    case "applied-callback":
                        context.ReportDiagnostic(Diagnostic.Create(kAppliedCallbackNotFound, Location.None,
                            split[0], split[2]));
                        break;

                    case "text-too-wide":
                        context.ReportDiagnostic(Diagnostic.Create(kTextTooWide, Location.None,
                            split[0], split[2], kTextCapacities[kTextCapacities.Length - 1]));
                        break;

                    default:
                        context.ReportDiagnostic(Diagnostic.Create(kUnsupportedMember, Location.None, split[0], split[2]));
                        break;
                }
            }
        }

        /// <summary>True when this type can actually have a block emitted for it.</summary>
        public static bool CanEmit(StateInfo info)
        {
            if (info.Members.Length == 0) return false;

            foreach (var problem in info.Problems)
            {
                if (problem.EndsWith("|nested|") || problem.EndsWith("|not-partial|")) return false;
            }

            return true;
        }

        /// <summary>Emits the block and the two movers, inside a second half of the owner.</summary>
        public static void EmitOwnerHalf(StringBuilder sb, StateInfo info)
        {
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

            sb.AppendLine($"{indent}/// <summary>This type's state-lane members, laid out for a frame to carry.</summary>");
            sb.AppendLine($"{indent}[global::System.CodeDom.Compiler.GeneratedCode(\"Lilium.RemoteControl.SourceGenerator\", \"1.0\")]");
            sb.AppendLine($"{indent}public struct {kBlockTypeName}");
            sb.AppendLine($"{indent}{{");
            foreach (var member in info.Members)
            {
                sb.AppendLine($"{indent}    public {member.BlockTypeName} {member.Name};");
            }
            sb.AppendLine($"{indent}}}");
            sb.AppendLine();

            sb.AppendLine($"{indent}[global::System.CodeDom.Compiler.GeneratedCode(\"Lilium.RemoteControl.SourceGenerator\", \"1.0\")]");
            sb.AppendLine($"{indent}internal static void {kCaptureMethodName}({info.TypeName} source, ref {kBlockTypeName} block)");
            sb.AppendLine($"{indent}{{");
            foreach (var member in info.Members)
            {
                if (member.TextCapacity > 0)
                {
                    sb.AppendLine($"{indent}    block.{member.Name} = {member.BlockTypeName}.From(source.{member.Name});");
                    continue;
                }

                sb.AppendLine($"{indent}    block.{member.Name} = source.{member.Name};");
            }
            sb.AppendLine($"{indent}}}");
            sb.AppendLine();

            sb.AppendLine($"{indent}[global::System.CodeDom.Compiler.GeneratedCode(\"Lilium.RemoteControl.SourceGenerator\", \"1.0\")]");
            sb.AppendLine($"{indent}internal static void {kApplyMethodName}(in {kBlockTypeName} block, {info.TypeName} target)");
            sb.AppendLine($"{indent}{{");
            foreach (var member in info.Members)
            {
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
                    continue;
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
                    continue;
                }

                sb.AppendLine($"{indent}    target.{member.Name} = block.{member.Name};");
            }
            sb.AppendLine($"{indent}}}");

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
            sb.Append(info.FullyQualifiedName);
            sb.Append('.');
            sb.Append(kBlockTypeName);
            sb.Append(">(");
            sb.Append(info.FullyQualifiedName);
            sb.Append('.');
            sb.Append(kCaptureMethodName);
            sb.Append(", ");
            sb.Append(info.FullyQualifiedName);
            sb.Append('.');
            sb.Append(kApplyMethodName);

            // The members that actually reached the block, so the runtime can tell "asked for the
            // state lane" from "carried by it". A member the generator turned away (no width for
            // its text, a type that is not unmanaged) is absent here, which is how the keyframe
            // restatement learns to keep carrying it.
            foreach (var member in info.Members)
            {
                sb.Append(", \"");
                sb.Append(member.Name);
                sb.Append('"');
            }

            sb.AppendLine(");");
        }
    }
}
