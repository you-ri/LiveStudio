// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lilium.RemoteControl.SourceGenerator
{
    [Generator(LanguageNames.CSharp)]
    public sealed class ExposedClassGenerator : IIncrementalGenerator
    {
        const string kExposedClassAttributeName = "Lilium.RemoteControl.ExposedClassAttribute";
        const string kExposedPropertyAttributeName = "Lilium.RemoteControl.ExposedPropertyAttribute";
        const string kExposedFieldAttributeName = "Lilium.RemoteControl.ExposedFieldAttribute";
        const string kExposedFunctionAttributeName = "Lilium.RemoteControl.ExposedFunctionAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Roslyn 4.3 で導入された ForAttributeWithMetadataName は使わない
            // (Unity 2022.3 の古いパッチが Roslyn 4.0 系の場合に動作させるため)
            // [ExposedClass] は Inherited=true なので、属性を直接持たない派生型も
            // ランタイムでは ExposedClass として登録される。型自身に属性が無いケースも
            // 拾うため、ここでは全 TypeDeclaration を対象にし、判定は _Transform 側で行う。
            var classes = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, _) => _Transform(ctx))
                .Where(static c => c != null)
                .Collect();

            // ModuleInitializerAttribute は .NET 5 以降にしか無いため netstandard 互換の polyfill を
            // emit するが、アクセス可能な定義が既に存在するアセンブリ (RemoteControl の internal polyfill が
            // InternalsVisibleTo で見えるアセンブリ等) では二重定義となり CS0436 が出る。
            // コンパイルに対しアクセス可能な定義があるかを調べ、無い場合のみ polyfill を emit する。
            var emitPolyfill = context.CompilationProvider.Select(static (compilation, _) =>
            {
                // 同名型が複数の参照アセンブリに存在すると単数版 GetTypeByMetadataName は
                // 曖昧として null を返す。参照アセンブリを個別に走査し、現在のアセンブリから
                // アクセス可能な ModuleInitializerAttribute が 1 つでもあれば polyfill を出力しない。
                foreach (var reference in compilation.References)
                {
                    if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol asm)
                        continue;
                    var type = _FindModuleInitializerType(asm);
                    if (type != null && compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
                        return false;
                }
                return true;
            });

            context.RegisterSourceOutput(classes.Combine(emitPolyfill), static (spc, pair) =>
            {
                var (list, polyfill) = pair;
                if (list.IsDefaultOrEmpty) return;
                var source = _Emit(list, polyfill);
                spc.AddSource("ExposedClassDeclarationOrder.g.cs", source);
            });
        }

        static ClassInfo _Transform(GeneratorSyntaxContext ctx)
        {
            var typeNode = (TypeDeclarationSyntax)ctx.Node;
            if (ctx.SemanticModel.GetDeclaredSymbol(typeNode) is not INamedTypeSymbol typeSymbol) return null;
            if (typeSymbol.IsGenericType) return null;

            // 継承チェーンを最も基底 → 派生の順に並べる。
            // ランタイム (ExposedClass) はリフレクションで継承メンバーも公開対象に含むため
            // (GetProperties/GetMethods は継承メンバーを返し、フィールドは BaseType を辿る)、
            // 宣言順テーブルにも基底クラスのメンバーを含めないと未登録扱いになり警告が出る。
            // 基底を先に並べることで、基底メンバーが派生メンバーより小さい宣言順 index になる。
            var chain = new List<INamedTypeSymbol>();
            for (var t = typeSymbol; t != null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
                chain.Add(t);
            chain.Reverse();

            // [ExposedClass] は Inherited=true (デフォルト) なので、チェーン上のいずれかの型に
            // 直接付いていれば、この型はランタイムで ExposedClass として登録される。
            bool isExposedClass = false;
            foreach (var level in chain)
            {
                if (_HasAttributeOnSymbol(level, kExposedClassAttributeName))
                {
                    isExposedClass = true;
                    break;
                }
            }
            if (!isExposedClass) return null;

            var members = ImmutableArray.CreateBuilder<string>();
            var seen = new HashSet<string>();

            foreach (var level in chain)
            {
                // 構築済みジェネリック基底 (Base<Foo>) でも、メンバー名はソース定義と同一なので
                // OriginalDefinition のソース宣言順で列挙する。
                foreach (var member in level.OriginalDefinition.GetMembers())
                {
                    string name = null;
                    switch (member)
                    {
                        case IPropertySymbol prop when _HasAttributeOnSymbol(prop, kExposedPropertyAttributeName):
                            name = prop.Name;
                            break;
                        case IFieldSymbol field when _HasAttributeOnSymbol(field, kExposedFieldAttributeName):
                            name = field.Name;
                            break;
                        case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary
                            && _HasAttributeOnSymbol(method, kExposedFunctionAttributeName):
                            name = method.Name;
                            break;
                    }

                    // 同名メンバー (派生での override / new シャドウ) は基底側を先勝ちで採用する。
                    if (name != null && seen.Add(name))
                        members.Add(name);
                }
            }

            if (members.Count == 0) return null;

            return new ClassInfo(
                typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                members.ToImmutable());
        }

        // 指定アセンブリ内の System.Runtime.CompilerServices.ModuleInitializerAttribute を返す (無ければ null)。
        // GetTypeByMetadataName は曖昧時に null を返すため、名前空間を手動で辿る。
        static INamedTypeSymbol _FindModuleInitializerType(IAssemblySymbol assembly)
        {
            var ns = assembly.GlobalNamespace;
            foreach (var part in new[] { "System", "Runtime", "CompilerServices" })
            {
                ns = ns.GetNamespaceMembers().FirstOrDefault(n => n.Name == part);
                if (ns == null) return null;
            }
            return ns.GetTypeMembers("ModuleInitializerAttribute").FirstOrDefault();
        }

        static bool _HasAttributeOnSymbol(ISymbol symbol, string fullName)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == fullName) return true;
            }
            return false;
        }

        static string _Emit(ImmutableArray<ClassInfo> classes, bool emitPolyfill)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// Generated by Lilium.RemoteControl.SourceGenerator");
            sb.AppendLine("#nullable disable");
            sb.AppendLine();
            sb.AppendLine("namespace Lilium.RemoteControl.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"Lilium.RemoteControl.SourceGenerator\", \"1.0\")]");
            sb.AppendLine("    internal static class ExposedClassDeclarationOrder");
            sb.AppendLine("    {");
            sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
            sb.AppendLine("        internal static void Register()");
            sb.AppendLine("        {");

            foreach (var info in classes)
            {
                if (info == null) continue;
                sb.Append("            global::Lilium.RemoteControl.ExposedClassDeclarationOrderTable.Register(typeof(");
                sb.Append(info.FullyQualifiedName);
                sb.Append("), new string[] {");
                for (int i = 0; i < info.MemberNames.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(' ');
                    sb.Append('"');
                    sb.Append(info.MemberNames[i]);
                    sb.Append('"');
                }
                sb.AppendLine(" });");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            // ModuleInitializerAttribute polyfill (netstandard2.0 互換)。
            // アクセス可能な定義が既に存在するアセンブリでは emitPolyfill=false となり、
            // 二重定義による CS0436 を避けるため出力しない。
            if (emitPolyfill)
            {
                sb.AppendLine("#if !NET5_0_OR_GREATER");
                sb.AppendLine("namespace System.Runtime.CompilerServices");
                sb.AppendLine("{");
                sb.AppendLine("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]");
                sb.AppendLine("    internal sealed class ModuleInitializerAttribute : global::System.Attribute { }");
                sb.AppendLine("}");
                sb.AppendLine("#endif");
            }

            return sb.ToString();
        }

        sealed class ClassInfo
        {
            public string FullyQualifiedName { get; }
            public ImmutableArray<string> MemberNames { get; }

            public ClassInfo(string fullyQualifiedName, ImmutableArray<string> memberNames)
            {
                FullyQualifiedName = fullyQualifiedName;
                MemberNames = memberNames;
            }

            public override bool Equals(object obj)
            {
                return obj is ClassInfo other
                    && FullyQualifiedName == other.FullyQualifiedName
                    && MemberNames.SequenceEqual(other.MemberNames);
            }

            public override int GetHashCode()
            {
                var hash = FullyQualifiedName?.GetHashCode() ?? 0;
                foreach (var n in MemberNames) hash = hash * 31 + (n?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
