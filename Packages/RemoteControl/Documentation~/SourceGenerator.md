# Source Generator — Lilium.RemoteControl.SourceGenerator

`Plugins/Lilium.RemoteControl.SourceGenerator.dll` is a Roslyn `IIncrementalGenerator` that extracts the **source-declaration order** of `[ExposedClass]` members from C# source and exposes it to the runtime. This makes the remote client's `DynamicObjectPane` lay `[ExposedProperty]`, `[ExposedField]`, and `[ExposedFunction]` members out **in the order they appear in source** when no explicit `order` is set, even when the kinds are interleaved.

Consumers do not need to do anything — the prebuilt DLL ships with the package. This document is for maintainers who edit or rebuild the generator.

---

## Layout

| Path | Role |
|---|---|
| `SourceGenerator~/Lilium.RemoteControl.SourceGenerator/*.cs` | Generator source. |
| `SourceGenerator~/Lilium.RemoteControl.SourceGenerator/*.csproj` | netstandard2.0 project, Roslyn 4.0. |
| `SourceGenerator~/build.ps1` | Builds with `dotnet build -c Release` and copies the DLL to `Plugins/`. |
| `Plugins/Lilium.RemoteControl.SourceGenerator.dll` | Distributed binary. **Commit alongside source changes** — pushing source without the DLL means other clones run an older generator. |

The trailing `~` on `SourceGenerator~/` keeps Unity from compiling the C# sources as game scripts (which would conflict with the prebuilt DLL).

Building requires the .NET SDK (6+). Consumers do not need it — they only consume the prebuilt DLL.

---

## Roslyn version constraint

The generator targets **Roslyn 4.0** so it works on the older Unity 2022.3 patch releases that some users still ship on.

If the Roslyn referenced by the generator is **newer** than Unity's bundled compiler, Unity raises `CS9057` and silently disables the generator. Keep `Microsoft.CodeAnalysis.CSharp` in `Lilium.RemoteControl.SourceGenerator.csproj` **at or below Unity's minimum supported compiler version**.

Practical consequences:

- Do **not** use APIs introduced after Roslyn 4.0. In particular, `ForAttributeWithMetadataName` (4.3+) is off-limits.
- Use the 4.0-compatible `CreateSyntaxProvider` pattern instead.

---

## Fallback behavior

When the generator is disabled (`CS9057`, missing DLL, unsupported runtime), `ExposedClassDeclarationOrderTable.Register` is never called. The runtime falls back to ordering members by `MemberInfo.MetadataToken` — which sorts members **by kind first** (all properties, then all fields, then all methods, in their declaration order within each kind), not by interleaved source order. The remote client still works, but pane layouts will look "blocked" instead of mirroring the source layout.
