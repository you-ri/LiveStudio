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
            var accessors = ImmutableArray.CreateBuilder<AccessorInfo>();
            var seen = new HashSet<string>();
            var compilation = ctx.SemanticModel.Compilation;

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
                    {
                        members.Add(name);

                        // メソッド (ExposedFunction) はアクセサ対象外。プロパティ/フィールドのみ高速化する。
                        var accessor = _MakeAccessor(member, compilation);
                        if (accessor != null) accessors.Add(accessor);
                    }
                }
            }

            if (members.Count == 0) return null;

            return new ClassInfo(
                typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                members.ToImmutable(),
                accessors.ToImmutable());
        }

        // メンバーから高速アクセサ情報を生成する。生成コードがコンパイルできる
        // (= 自由関数からアクセス可能な) メンバーのみ対象。不適格なら null を返し、
        // ランタイムは reflection にフォールバックする。
        static AccessorInfo _MakeAccessor(ISymbol member, Compilation compilation)
        {
            // メンバーが property/field でなければ対象外。
            var property = member as IPropertySymbol;
            var field = member as IFieldSymbol;
            if (property == null && field == null) return null;
            if (property != null && property.IsIndexer) return null;

            // 宣言型が非ジェネリックの参照型で、かつ現在のアセンブリからアクセス可能であること。
            // (struct はボックス化されたコピーへの set になるため対象外。ジェネリック基底は
            //  ランタイムの DeclaringType が構築済み型になりキーが一致しないため対象外。)
            var declaringType = member.ContainingType?.OriginalDefinition;
            if (declaringType == null) return null;
            if (declaringType.IsGenericType || declaringType.IsValueType) return null;
            if (declaringType.TypeKind != TypeKind.Class) return null;
            if (!compilation.IsSymbolAccessibleWithin(declaringType, compilation.Assembly)) return null;

            var memberType = property != null ? property.Type : field.Type;

            // getter: property は getter アクセサ、field はフィールド自体がアクセス可能なこと。
            bool hasGetter;
            if (property != null)
                hasGetter = property.GetMethod != null
                    && compilation.IsSymbolAccessibleWithin(property.GetMethod, compilation.Assembly);
            else
                hasGetter = compilation.IsSymbolAccessibleWithin(field, compilation.Assembly);

            // setter: 書き込み可能 (init-only/readonly/const でない) かつ
            //         setter アクセサ・メンバー型ともにアクセス可能なこと。
            bool hasSetter;
            if (property != null)
                hasSetter = property.SetMethod != null
                    && !property.SetMethod.IsInitOnly
                    && compilation.IsSymbolAccessibleWithin(property.SetMethod, compilation.Assembly)
                    && compilation.IsSymbolAccessibleWithin(memberType, compilation.Assembly);
            else
                hasSetter = !field.IsReadOnly && !field.IsConst
                    && compilation.IsSymbolAccessibleWithin(field, compilation.Assembly)
                    && compilation.IsSymbolAccessibleWithin(memberType, compilation.Assembly);

            if (!hasGetter && !hasSetter) return null;

            return new AccessorInfo(
                declaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                member.Name,
                memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                member.IsStatic,
                hasGetter,
                hasSetter);
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

            _EmitAccessors(sb, classes);

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

        // 高速アクセサ登録クラスを emit する。(DeclaringType, MemberName) で重複排除する
        // (同一の継承メンバーが複数の派生型経由で集まるため)。
        static void _EmitAccessors(StringBuilder sb, ImmutableArray<ClassInfo> classes)
        {
            var emitted = new HashSet<string>();

            sb.AppendLine();
            sb.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"Lilium.RemoteControl.SourceGenerator\", \"1.0\")]");
            sb.AppendLine("    internal static class ExposedMemberAccessors");
            sb.AppendLine("    {");
            sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
            sb.AppendLine("        internal static void Register()");
            sb.AppendLine("        {");

            foreach (var info in classes)
            {
                if (info == null) continue;
                foreach (var a in info.Accessors)
                {
                    if (a == null) continue;
                    if (!emitted.Add(a.DeclaringTypeFqn + "\0" + a.MemberName)) continue;

                    // static は型名直接、instance は宣言型にキャストしてアクセスする。
                    var target = a.IsStatic ? a.DeclaringTypeFqn : "((" + a.DeclaringTypeFqn + ")o)";
                    var getterExpr = a.HasGetter
                        ? "(object o) => " + target + "." + a.MemberName
                        : "null";
                    var setterExpr = a.HasSetter
                        ? "(object o, object v) => " + target + "." + a.MemberName + " = (" + a.MemberTypeFqn + ")v"
                        : "null";

                    sb.Append("            global::Lilium.RemoteControl.ExposedMemberAccessorTable.Register(typeof(");
                    sb.Append(a.DeclaringTypeFqn);
                    sb.Append("), \"");
                    sb.Append(a.MemberName);
                    sb.Append("\", ");
                    sb.Append(getterExpr);
                    sb.Append(", ");
                    sb.Append(setterExpr);
                    sb.AppendLine(");");
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
        }

        sealed class ClassInfo
        {
            public string FullyQualifiedName { get; }
            public ImmutableArray<string> MemberNames { get; }
            public ImmutableArray<AccessorInfo> Accessors { get; }

            public ClassInfo(string fullyQualifiedName, ImmutableArray<string> memberNames,
                ImmutableArray<AccessorInfo> accessors)
            {
                FullyQualifiedName = fullyQualifiedName;
                MemberNames = memberNames;
                Accessors = accessors;
            }

            public override bool Equals(object obj)
            {
                return obj is ClassInfo other
                    && FullyQualifiedName == other.FullyQualifiedName
                    && MemberNames.SequenceEqual(other.MemberNames)
                    && Accessors.SequenceEqual(other.Accessors);
            }

            public override int GetHashCode()
            {
                var hash = FullyQualifiedName?.GetHashCode() ?? 0;
                foreach (var n in MemberNames) hash = hash * 31 + (n?.GetHashCode() ?? 0);
                foreach (var a in Accessors) hash = hash * 31 + (a?.GetHashCode() ?? 0);
                return hash;
            }
        }

        // 高速アクセサ 1 件分の情報。キーは DeclaringTypeFqn + MemberName。
        sealed class AccessorInfo
        {
            public string DeclaringTypeFqn { get; }
            public string MemberName { get; }
            public string MemberTypeFqn { get; }
            public bool IsStatic { get; }
            public bool HasGetter { get; }
            public bool HasSetter { get; }

            public AccessorInfo(string declaringTypeFqn, string memberName, string memberTypeFqn,
                bool isStatic, bool hasGetter, bool hasSetter)
            {
                DeclaringTypeFqn = declaringTypeFqn;
                MemberName = memberName;
                MemberTypeFqn = memberTypeFqn;
                IsStatic = isStatic;
                HasGetter = hasGetter;
                HasSetter = hasSetter;
            }

            public override bool Equals(object obj)
            {
                return obj is AccessorInfo o
                    && DeclaringTypeFqn == o.DeclaringTypeFqn
                    && MemberName == o.MemberName
                    && MemberTypeFqn == o.MemberTypeFqn
                    && IsStatic == o.IsStatic
                    && HasGetter == o.HasGetter
                    && HasSetter == o.HasSetter;
            }

            public override int GetHashCode()
            {
                int hash = DeclaringTypeFqn?.GetHashCode() ?? 0;
                hash = hash * 31 + (MemberName?.GetHashCode() ?? 0);
                hash = hash * 31 + (MemberTypeFqn?.GetHashCode() ?? 0);
                hash = hash * 31 + (IsStatic ? 1 : 0);
                hash = hash * 31 + (HasGetter ? 1 : 0);
                hash = hash * 31 + (HasSetter ? 1 : 0);
                return hash;
            }
        }
    }
}
