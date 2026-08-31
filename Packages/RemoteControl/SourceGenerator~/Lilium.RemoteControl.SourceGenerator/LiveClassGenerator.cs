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
    public sealed class LiveClassGenerator : IIncrementalGenerator
    {
        const string kLiveClassAttributeName = "Lilium.RemoteControl.LiveClassAttribute";
        const string kLivePropertyAttributeName = "Lilium.RemoteControl.LivePropertyAttribute";
        const string kLiveFieldAttributeName = "Lilium.RemoteControl.LiveFieldAttribute";
        const string kLiveFunctionAttributeName = "Lilium.RemoteControl.LiveFunctionAttribute";

        // 状態ブロックが名指しする型 (StateBridgeRegistry / StateBlock) の居所。
        const string kSimulationAssemblyName = "Lilium.RemoteControl.Simulation";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Roslyn 4.3 で導入された ForAttributeWithMetadataName は使わない
            // (Unity 2022.3 の古いパッチが Roslyn 4.0 系の場合に動作させるため)
            // [LiveClass] は Inherited=true なので、属性を直接持たない派生型も
            // ランタイムでは LiveClass として登録される。型自身に属性が無いケースも
            // 拾うため、ここでは全 TypeDeclaration を対象にし、判定は _Transform 側で行う。
            var classes = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, _) => _Transform(ctx))
                .Where(static c => c != null)
                .Collect();

            // コンパイル 1 つにつき 1 度だけ調べれば足りる事実を、まとめて 1 つのプロバイダにする。
            var facts = context.CompilationProvider.Select(static (compilation, _) =>
            {
                // ModuleInitializerAttribute は .NET 5 以降にしか無いため netstandard 互換の polyfill を
                // emit するが、アクセス可能な定義が既に存在するアセンブリ (RemoteControl の internal polyfill が
                // InternalsVisibleTo で見えるアセンブリ等) では二重定義となり CS0436 が出る。
                // コンパイルに対しアクセス可能な定義があるかを調べ、無い場合のみ polyfill を emit する。
                //
                // 同名型が複数の参照アセンブリに存在すると単数版 GetTypeByMetadataName は
                // 曖昧として null を返す。参照アセンブリを個別に走査し、現在のアセンブリから
                // アクセス可能な ModuleInitializerAttribute が 1 つでもあれば polyfill を出力しない。
                var emitPolyfill = true;

                // 状態ブロックは Lilium.RemoteControl.Simulation の型を名指しする。参照が無いまま
                // 出力すると、書いた覚えのない生成コードの中で CS0246 になる (LRC004 で置き換える)。
                var hasSimulation = compilation.Assembly.Name == kSimulationAssemblyName;

                foreach (var reference in compilation.References)
                {
                    if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol asm)
                        continue;

                    if (asm.Name == kSimulationAssemblyName) hasSimulation = true;

                    var type = _FindModuleInitializerType(asm);
                    if (type != null && compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
                        emitPolyfill = false;
                }

                return (emitPolyfill, hasSimulation);
            });

            context.RegisterSourceOutput(classes.Combine(facts), static (spc, pair) =>
            {
                var (list, facts) = pair;
                var (polyfill, hasSimulation) = facts;
                if (list.IsDefaultOrEmpty) return;

                foreach (var info in list)
                {
                    if (info?.State == null) continue;

                    StateBlockEmitter.ReportProblems(spc, info.State);

                    // 参照が無いなら、出せないことを型ごとに名指しで言う。出力を止めるのは
                    // 生成コードの中で CS0246 を出さないため — 直す場所は生成コードではない。
                    if (!hasSimulation && StateBlockEmitter.CanEmit(info.State))
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            StateBlockEmitter.kMissingSimulationReference, Location.None,
                            info.State.FullyQualifiedName));
                    }
                }

                var source = _Emit(list, polyfill, hasSimulation);
                spc.AddSource("LiveClassDeclarationOrder.g.cs", source);
            });
        }

        static ClassInfo _Transform(GeneratorSyntaxContext ctx)
        {
            var typeNode = (TypeDeclarationSyntax)ctx.Node;
            if (ctx.SemanticModel.GetDeclaredSymbol(typeNode) is not INamedTypeSymbol typeSymbol) return null;
            if (typeSymbol.IsGenericType) return null;

            // 継承チェーンを最も基底 → 派生の順に並べる。
            // ランタイム (LiveClass) はリフレクションで継承メンバーも公開対象に含むため
            // (GetProperties/GetMethods は継承メンバーを返し、フィールドは BaseType を辿る)、
            // 宣言順テーブルにも基底クラスのメンバーを含めないと未登録扱いになり警告が出る。
            // 基底を先に並べることで、基底メンバーが派生メンバーより小さい宣言順 index になる。
            var chain = new List<INamedTypeSymbol>();
            for (var t = typeSymbol; t != null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
                chain.Add(t);
            chain.Reverse();

            // [LiveClass] は Inherited=true (デフォルト) なので、チェーン上のいずれかの型に
            // 直接付いていれば、この型はランタイムで LiveClass として登録される。
            bool isLiveClass = false;
            foreach (var level in chain)
            {
                if (_HasAttributeOnSymbol(level, kLiveClassAttributeName))
                {
                    isLiveClass = true;
                    break;
                }
            }
            if (!isLiveClass) return null;

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
                        case IPropertySymbol prop when _HasAttributeOnSymbol(prop, kLivePropertyAttributeName):
                            name = prop.Name;
                            break;
                        case IFieldSymbol field when _HasAttributeOnSymbol(field, kLiveFieldAttributeName):
                            name = field.Name;
                            break;
                        case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary
                            && _HasAttributeOnSymbol(method, kLiveFunctionAttributeName):
                            name = method.Name;
                            break;
                    }

                    // 同名メンバー (派生での override / new シャドウ) は基底側を先勝ちで採用する。
                    if (name != null && seen.Add(name))
                    {
                        members.Add(name);

                        // メソッド (LiveFunction) はアクセサ対象外。プロパティ/フィールドのみ高速化する。
                        var accessor = _MakeAccessor(member, compilation);
                        if (accessor != null) accessors.Add(accessor);
                    }
                }
            }

            if (members.Count == 0) return null;

            return new ClassInfo(
                typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                members.ToImmutable(),
                accessors.ToImmutable(),
                StateBlockEmitter.Collect(typeSymbol, typeNode, chain));
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

            // 値が有限な型 (bool / enum) は getter を正規箱経由にして boxing 割り当てを避ける。
            // nullable (Nullable<bool>/Nullable<enum>) は SpecialType/TypeKind が一致しないので対象外。
            GetterBoxKind getterBox = GetterBoxKind.None;
            if (hasGetter)
            {
                if (memberType.SpecialType == SpecialType.System_Boolean) getterBox = GetterBoxKind.Bool;
                else if (memberType.TypeKind == TypeKind.Enum) getterBox = GetterBoxKind.Enum;
            }

            // 値型メンバーは型付きアクセサ (Func<object,T>/Action<object,T>) を追加生成し、box を回避する。
            // 参照型は object 経路で元々 box しないので不要。ポインタ / ref-like (Span 等) は
            // generic 型引数にできないため除外。
            bool emitTyped = memberType.IsValueType
                && memberType.TypeKind != TypeKind.Pointer
                && !memberType.IsRefLikeType;

            return new AccessorInfo(
                declaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                member.Name,
                memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                member.IsStatic,
                hasGetter,
                hasSetter,
                getterBox,
                emitTyped);
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

        /// <param name="emitState">
        /// False when the compilation cannot see Lilium.RemoteControl.Simulation. The declaration
        /// order table is still worth emitting -- only the state block and its registration name
        /// types from there.
        /// </param>
        static string _Emit(ImmutableArray<ClassInfo> classes, bool emitPolyfill, bool emitState)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// Generated by Lilium.RemoteControl.SourceGenerator");
            sb.AppendLine("#nullable disable");
            sb.AppendLine();
            sb.AppendLine("namespace Lilium.RemoteControl.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"Lilium.RemoteControl.SourceGenerator\", \"1.0\")]");
            sb.AppendLine("    internal static class LiveClassDeclarationOrder");
            sb.AppendLine("    {");
            sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
            sb.AppendLine("        internal static void Register()");
            sb.AppendLine("        {");

            foreach (var info in classes)
            {
                if (info == null) continue;
                sb.Append("            global::Lilium.RemoteControl.LiveClassDeclarationOrderTable.Register(typeof(");
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

            if (emitState) _EmitStateRegistrations(sb, classes);

            sb.AppendLine("        }");
            sb.AppendLine("    }");

            _EmitAccessors(sb, classes);

            sb.AppendLine("}");
            sb.AppendLine();

            if (emitState) _EmitStateBlocks(sb, classes);
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

        // 状態レーンのブリッジ登録を宣言順テーブルと同じモジュール初期化子に相乗りさせる。
        // 初期化子を別に持つと polyfill の二重定義判定をもう一度やることになる。
        static void _EmitStateRegistrations(StringBuilder sb, ImmutableArray<ClassInfo> classes)
        {
            var emitted = new HashSet<string>();

            foreach (var info in classes)
            {
                if (info?.State == null) continue;
                if (!StateBlockEmitter.CanEmit(info.State)) continue;
                if (!emitted.Add(info.State.FullyQualifiedName)) continue;

                StateBlockEmitter.EmitRegistration(sb, info.State);
            }
        }

        // 所有者型の後半 (ブロック構造体と出し入れ 2 関数) を emit する。
        // private メンバーに届く必要があるため、型の内側に出す。
        static void _EmitStateBlocks(StringBuilder sb, ImmutableArray<ClassInfo> classes)
        {
            var emitted = new HashSet<string>();

            foreach (var info in classes)
            {
                if (info?.State == null) continue;
                if (!StateBlockEmitter.CanEmit(info.State)) continue;
                if (!emitted.Add(info.State.FullyQualifiedName)) continue;

                StateBlockEmitter.EmitOwnerHalf(sb, info.State);
            }
        }

        // 高速アクセサ登録クラスを emit する。(DeclaringType, MemberName) で重複排除する
        // (同一の継承メンバーが複数の派生型経由で集まるため)。
        static void _EmitAccessors(StringBuilder sb, ImmutableArray<ClassInfo> classes)
        {
            var emitted = new HashSet<string>();

            sb.AppendLine();
            sb.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"Lilium.RemoteControl.SourceGenerator\", \"1.0\")]");
            sb.AppendLine("    internal static class LiveMemberAccessors");
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
                    // bool / enum は値が有限なので正規箱 (BoxedValues) 経由で返し、読み取りごとの boxing を避ける。
                    string getterExpr;
                    if (!a.HasGetter)
                        getterExpr = "null";
                    else if (a.GetterBox == GetterBoxKind.Bool)
                        getterExpr = "(object o) => global::Lilium.RemoteControl.BoxedValues.Box(" + target + "." + a.MemberName + ")";
                    else if (a.GetterBox == GetterBoxKind.Enum)
                        getterExpr = "(object o) => global::Lilium.RemoteControl.BoxedValues.BoxEnum<" + a.MemberTypeFqn + ">(" + target + "." + a.MemberName + ")";
                    else
                        getterExpr = "(object o) => " + target + "." + a.MemberName;
                    var setterExpr = a.HasSetter
                        ? "(object o, object v) => " + target + "." + a.MemberName + " = (" + a.MemberTypeFqn + ")v"
                        : "null";

                    sb.Append("            global::Lilium.RemoteControl.LiveMemberAccessorTable.Register(typeof(");
                    sb.Append(a.DeclaringTypeFqn);
                    sb.Append("), \"");
                    sb.Append(a.MemberName);
                    sb.Append("\", ");
                    sb.Append(getterExpr);
                    sb.Append(", ");
                    sb.Append(setterExpr);

                    // 値型メンバーは型付きアクセサ (Func<object,T>/Action<object,T>) も渡し、box を回避可能にする。
                    if (a.EmitTyped)
                    {
                        var typedGetterExpr = a.HasGetter
                            ? "(global::System.Func<object, " + a.MemberTypeFqn + ">)((object o) => " + target + "." + a.MemberName + ")"
                            : "null";
                        var typedSetterExpr = a.HasSetter
                            ? "(global::System.Action<object, " + a.MemberTypeFqn + ">)((object o, " + a.MemberTypeFqn + " v) => " + target + "." + a.MemberName + " = v)"
                            : "null";
                        sb.Append(", ");
                        sb.Append(typedGetterExpr);
                        sb.Append(", ");
                        sb.Append(typedSetterExpr);
                    }

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

            /// <summary>What this type puts in the state lane, or null when it puts nothing there.</summary>
            public StateInfo State { get; }

            public ClassInfo(string fullyQualifiedName, ImmutableArray<string> memberNames,
                ImmutableArray<AccessorInfo> accessors, StateInfo state)
            {
                FullyQualifiedName = fullyQualifiedName;
                MemberNames = memberNames;
                Accessors = accessors;
                State = state;
            }

            public override bool Equals(object obj)
            {
                return obj is ClassInfo other
                    && FullyQualifiedName == other.FullyQualifiedName
                    && MemberNames.SequenceEqual(other.MemberNames)
                    && Accessors.SequenceEqual(other.Accessors)
                    && Equals(State, other.State);
            }

            public override int GetHashCode()
            {
                var hash = FullyQualifiedName?.GetHashCode() ?? 0;
                foreach (var n in MemberNames) hash = hash * 31 + (n?.GetHashCode() ?? 0);
                foreach (var a in Accessors) hash = hash * 31 + (a?.GetHashCode() ?? 0);
                return hash;
            }
        }

        // getter が返す値の boxing 戦略。bool/enum は正規箱で割り当てを避ける。
        enum GetterBoxKind { None, Bool, Enum }

        // 高速アクセサ 1 件分の情報。キーは DeclaringTypeFqn + MemberName。
        sealed class AccessorInfo
        {
            public string DeclaringTypeFqn { get; }
            public string MemberName { get; }
            public string MemberTypeFqn { get; }
            public bool IsStatic { get; }
            public bool HasGetter { get; }
            public bool HasSetter { get; }
            public GetterBoxKind GetterBox { get; }
            public bool EmitTyped { get; }

            public AccessorInfo(string declaringTypeFqn, string memberName, string memberTypeFqn,
                bool isStatic, bool hasGetter, bool hasSetter, GetterBoxKind getterBox, bool emitTyped)
            {
                DeclaringTypeFqn = declaringTypeFqn;
                MemberName = memberName;
                MemberTypeFqn = memberTypeFqn;
                IsStatic = isStatic;
                HasGetter = hasGetter;
                HasSetter = hasSetter;
                GetterBox = getterBox;
                EmitTyped = emitTyped;
            }

            public override bool Equals(object obj)
            {
                return obj is AccessorInfo o
                    && DeclaringTypeFqn == o.DeclaringTypeFqn
                    && MemberName == o.MemberName
                    && MemberTypeFqn == o.MemberTypeFqn
                    && IsStatic == o.IsStatic
                    && HasGetter == o.HasGetter
                    && HasSetter == o.HasSetter
                    && GetterBox == o.GetterBox
                    && EmitTyped == o.EmitTyped;
            }

            public override int GetHashCode()
            {
                int hash = DeclaringTypeFqn?.GetHashCode() ?? 0;
                hash = hash * 31 + (MemberName?.GetHashCode() ?? 0);
                hash = hash * 31 + (MemberTypeFqn?.GetHashCode() ?? 0);
                hash = hash * 31 + (IsStatic ? 1 : 0);
                hash = hash * 31 + (HasGetter ? 1 : 0);
                hash = hash * 31 + (HasSetter ? 1 : 0);
                hash = hash * 31 + (int)GetterBox;
                hash = hash * 31 + (EmitTyped ? 1 : 0);
                return hash;
            }
        }
    }
}
