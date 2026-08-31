// Copyright (c) You-Ri, 2026
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Lilium.RemoteControl.SourceGenerator
{
    /// <summary>
    /// Keeps the simulation assembly from reaching into the view, or into a clock of its own.
    ///
    /// The boundary between simulation and view is drawn by the dependency direction: the
    /// simulation assembly does not reference the host, so it cannot reach live objects, the
    /// registry or the bridges. Two things that direction does not stop are what this analyzer
    /// covers -- a scene object handed in from outside, and the wall clock, which is reachable
    /// from the BCL no matter how the assemblies are arranged.
    ///
    /// This replaces an earlier attempt to declare "noEngineReferences" on that assembly. The
    /// engine ban was both too wide and too narrow: it took away the job system and the native
    /// containers, which is the layer's whole point, while leaving <c>DateTime</c> untouched.
    /// Naming the rules directly costs a build step but says what is actually meant. See
    /// Documents/LiveDataCore.md, "Draw the boundary with an assembly".
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SimulationBoundaryAnalyzer : DiagnosticAnalyzer
    {
        const string kSimulationAssemblyName = "Lilium.RemoteControl.Simulation";

        public static readonly DiagnosticDescriptor kViewType = new DiagnosticDescriptor(
            "LRC010",
            "Simulation touches a view object",
            "'{0}' derives from UnityEngine.Object, so the simulation must not touch it. A value read from an evaluated scene object cannot be reproduced from a recording; take it as a registered input source on the state lane instead.",
            "Lilium.RemoteControl",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Value types such as Vector3, NativeArray and the job structs are unaffected -- only objects that live in a scene or an asset.");

        public static readonly DiagnosticDescriptor kWallClock = new DiagnosticDescriptor(
            "LRC011",
            "Simulation reads a clock of its own",
            "'{0}' is a clock outside the live data. Time that drives the simulation has to come from the frame, or replaying a recording will not reproduce it. Read the frame's own delta instead.",
            "Lilium.RemoteControl",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "The frame clock implementations are the intended exception: they are the input boundary where real time enters, and they suppress this rule explicitly.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(kViewType, kWallClock);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

            context.RegisterCompilationStartAction(start =>
            {
                // The analyzer ships in the same DLL as the generator, which Unity applies to every
                // assembly. Everywhere but the simulation these rules are none of our business.
                if (start.Compilation.Assembly.Name != kSimulationAssemblyName) return;

                var unityObject = start.Compilation.GetTypeByMetadataName("UnityEngine.Object");
                var clocks = _CollectClocks(start.Compilation);

                // Member access catches the uses: Time.deltaTime, DateTime.Now, transform.position.
                start.RegisterOperationAction(
                    ctx => _CheckMemberUse(ctx, unityObject, clocks),
                    OperationKind.PropertyReference,
                    OperationKind.FieldReference,
                    OperationKind.Invocation,
                    OperationKind.ObjectCreation);

                // Declarations catch what a use would miss: a field or parameter that merely holds
                // a scene object is already the mistake, whether or not anything reads it yet.
                start.RegisterSymbolAction(
                    ctx => _CheckDeclaredType(ctx, unityObject),
                    SymbolKind.Field, SymbolKind.Property, SymbolKind.Method);
            });
        }

        /// <summary>The types whose members hand out real time, as far as they can be resolved.</summary>
        static ImmutableArray<INamedTypeSymbol> _CollectClocks(Compilation compilation)
        {
            var names = new[]
            {
                "UnityEngine.Time",
                "System.DateTime",
                "System.DateTimeOffset",
                "System.Diagnostics.Stopwatch",
                "Lilium.RemoteControl.TimeUtility",
            };

            return names
                .Select(compilation.GetTypeByMetadataName)
                .Where(t => t != null)
                .ToImmutableArray();
        }

        static void _CheckMemberUse(OperationAnalysisContext context,
            INamedTypeSymbol unityObject, ImmutableArray<INamedTypeSymbol> clocks)
        {
            var member = _MemberOf(context.Operation);
            if (member == null) return;

            var owner = member.ContainingType;
            if (owner == null) return;

            if (_DerivesFrom(owner, unityObject))
            {
                context.ReportDiagnostic(Diagnostic.Create(kViewType,
                    context.Operation.Syntax.GetLocation(), owner.ToDisplayString()));
                return;
            }

            // Only the members that actually read the clock. DateTime arithmetic on a value that
            // arrived from outside is fine -- what must not happen is asking the machine for "now".
            if (!clocks.Any(c => SymbolEqualityComparer.Default.Equals(c, owner))) return;
            if (!_ReadsRealTime(member)) return;

            context.ReportDiagnostic(Diagnostic.Create(kWallClock,
                context.Operation.Syntax.GetLocation(),
                $"{owner.ToDisplayString()}.{member.Name}"));
        }

        static ISymbol _MemberOf(IOperation operation)
        {
            switch (operation)
            {
                case IPropertyReferenceOperation p: return p.Property;
                case IFieldReferenceOperation f: return f.Field;
                case IInvocationOperation i: return i.TargetMethod;
                case IObjectCreationOperation o: return o.Constructor;
                default: return null;
            }
        }

        /// <summary>
        /// Whether the member is one that reads the machine's clock, as opposed to one that merely
        /// works with a time value it was given.
        /// </summary>
        static bool _ReadsRealTime(ISymbol member)
        {
            switch (member.Name)
            {
                // System.DateTime / DateTimeOffset
                case "Now":
                case "UtcNow":
                case "Today":
                // System.Diagnostics.Stopwatch -- any of these means a clock is being run here
                case "StartNew":
                case "GetTimestamp":
                case "Start":
                case "Elapsed":
                case "ElapsedMilliseconds":
                case "ElapsedTicks":
                    return true;
                default:
                    // UnityEngine.Time and TimeUtility exist only to answer this question, so every
                    // member of theirs counts.
                    return member.ContainingType?.Name == "Time"
                        || member.ContainingType?.Name == "TimeUtility";
            }
        }

        static void _CheckDeclaredType(SymbolAnalysisContext context, INamedTypeSymbol unityObject)
        {
            if (unityObject == null) return;

            foreach (var (type, symbol) in _DeclaredTypes(context.Symbol))
            {
                if (!_DerivesFrom(type, unityObject)) continue;

                var location = symbol.Locations.FirstOrDefault();
                if (location == null || !location.IsInSource) continue;

                context.ReportDiagnostic(Diagnostic.Create(kViewType, location, type.ToDisplayString()));
            }
        }

        static System.Collections.Generic.IEnumerable<(ITypeSymbol, ISymbol)> _DeclaredTypes(ISymbol symbol)
        {
            switch (symbol)
            {
                case IFieldSymbol f:
                    yield return (f.Type, f);
                    break;
                case IPropertySymbol p:
                    yield return (p.Type, p);
                    break;
                case IMethodSymbol m:
                    yield return (m.ReturnType, m);
                    foreach (var parameter in m.Parameters) yield return (parameter.Type, parameter);
                    break;
            }
        }

        static bool _DerivesFrom(ITypeSymbol type, INamedTypeSymbol root)
        {
            if (type == null || root == null) return false;

            for (var current = type; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, root)) return true;
            }

            return false;
        }
    }
}
