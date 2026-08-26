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
        public string TypeName { get; }

        public StateMemberInfo(string name, string typeName)
        {
            Name = name;
            TypeName = typeName;
        }

        public override bool Equals(object obj)
            => obj is StateMemberInfo other && Name == other.Name && TypeName == other.TypeName;

        public override int GetHashCode() => (Name?.GetHashCode() ?? 0) * 397 ^ (TypeName?.GetHashCode() ?? 0);
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

        public static readonly DiagnosticDescriptor kNestedType = new DiagnosticDescriptor(
            "LRC003",
            "State-lane type is nested",
            "'{0}' declares members in the state lane but is a nested type, which is not supported yet. Move it out, or leave the members in the input lane.",
            "Lilium.RemoteControl",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// Collects what a type puts in the state lane. Null when it puts nothing there, which is
        /// the common case and costs nothing downstream.
        /// </summary>
        public static StateInfo Collect(INamedTypeSymbol typeSymbol, TypeDeclarationSyntax node,
            IEnumerable<INamedTypeSymbol> chain)
        {
            var members = ImmutableArray.CreateBuilder<StateMemberInfo>();
            var problems = ImmutableArray.CreateBuilder<string>();
            var seen = new HashSet<string>();
            var any = false;

            foreach (var level in chain)
            {
                foreach (var member in level.OriginalDefinition.GetMembers())
                {
                    if (!_TryReadStateMember(member, out var memberType)) continue;
                    if (!seen.Add(member.Name)) continue;

                    any = true;

                    // The one rule for what can be carried. Asking the compiler rather than keeping a
                    // list of blessed types means enums, Vector3, Color and anyone's own struct all
                    // work without being named here, and string, arrays and Nullable are refused for
                    // the same reason -- they are not something a block can hold.
                    if (!memberType.IsUnmanagedType)
                    {
                        problems.Add($"{level.Name}.{member.Name}|{memberType.ToDisplayString()}");
                        continue;
                    }

                    members.Add(new StateMemberInfo(
                        member.Name,
                        memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                }
            }

            if (!any) return null;

            var isPartial = node.Modifiers.Any(SyntaxKind.PartialKeyword);
            var isNested = typeSymbol.ContainingType != null;

            if (isNested) problems.Add("|nested");
            else if (!isPartial) problems.Add("|not-partial");

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

        /// <summary>True when a member is declared in the state lane; hands back its type.</summary>
        static bool _TryReadStateMember(ISymbol member, out ITypeSymbol memberType)
        {
            memberType = null;

            AttributeData attribute = null;
            switch (member)
            {
                case IPropertySymbol property when !property.IsIndexer:
                    attribute = _FindLaneAttribute(property, "Lilium.RemoteControl.LivePropertyAttribute");
                    memberType = property.Type;
                    break;

                case IFieldSymbol field when !field.IsConst:
                    attribute = _FindLaneAttribute(field, "Lilium.RemoteControl.LiveFieldAttribute");
                    memberType = field.Type;
                    break;
            }

            if (attribute == null)
            {
                memberType = null;
                return false;
            }

            // lane = FrameLane.State is 1. Anything else -- absent, Input, None -- is not our business.
            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key != "lane") continue;
                if (named.Value.Value is int value && value == 1) return true;
            }

            memberType = null;
            return false;
        }

        static AttributeData _FindLaneAttribute(ISymbol member, string attributeName)
        {
            foreach (var attr in member.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == attributeName) return attr;
            }

            return null;
        }

        /// <summary>Reports what stopped a type from carrying its state.</summary>
        public static void ReportProblems(SourceProductionContext context, StateInfo info)
        {
            foreach (var problem in info.Problems)
            {
                var split = problem.Split('|');
                if (split.Length != 2) continue;

                if (split[1] == "nested")
                {
                    context.ReportDiagnostic(Diagnostic.Create(kNestedType, Location.None, info.FullyQualifiedName));
                }
                else if (split[1] == "not-partial")
                {
                    context.ReportDiagnostic(Diagnostic.Create(kNotPartial, Location.None, info.FullyQualifiedName));
                }
                else
                {
                    context.ReportDiagnostic(Diagnostic.Create(kUnsupportedMember, Location.None, split[0], split[1]));
                }
            }
        }

        /// <summary>True when this type can actually have a block emitted for it.</summary>
        public static bool CanEmit(StateInfo info)
        {
            if (info.Members.Length == 0) return false;

            foreach (var problem in info.Problems)
            {
                if (problem.EndsWith("|nested") || problem.EndsWith("|not-partial")) return false;
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
                sb.AppendLine($"{indent}    public {member.TypeName} {member.Name};");
            }
            sb.AppendLine($"{indent}}}");
            sb.AppendLine();

            sb.AppendLine($"{indent}[global::System.CodeDom.Compiler.GeneratedCode(\"Lilium.RemoteControl.SourceGenerator\", \"1.0\")]");
            sb.AppendLine($"{indent}internal static void {kCaptureMethodName}({info.TypeName} source, ref {kBlockTypeName} block)");
            sb.AppendLine($"{indent}{{");
            foreach (var member in info.Members)
            {
                sb.AppendLine($"{indent}    block.{member.Name} = source.{member.Name};");
            }
            sb.AppendLine($"{indent}}}");
            sb.AppendLine();

            sb.AppendLine($"{indent}[global::System.CodeDom.Compiler.GeneratedCode(\"Lilium.RemoteControl.SourceGenerator\", \"1.0\")]");
            sb.AppendLine($"{indent}internal static void {kApplyMethodName}(in {kBlockTypeName} block, {info.TypeName} target)");
            sb.AppendLine($"{indent}{{");
            foreach (var member in info.Members)
            {
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
            sb.AppendLine(");");
        }
    }
}
