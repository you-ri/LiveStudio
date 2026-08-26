# Source Generator — Lilium.RemoteControl.SourceGenerator

`Plugins/Lilium.RemoteControl.SourceGenerator.dll` is a Roslyn `IIncrementalGenerator` that extracts the **source-declaration order** of `[LiveClass]` members from C# source and exposes it to the runtime. This makes the remote client's `DynamicObjectPane` lay `[LiveProperty]`, `[LiveField]`, and `[LiveFunction]` members out **in the order they appear in source** when no explicit `order` is set, even when the kinds are interleaved.

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

## State blocks

The generator also emits the **state-lane blocks** the deterministic frame carries.

A member declared `lane = FrameLane.State` is read every frame, for every object that has one. That
is the case reflection handles worst, so the generator turns it into field assignments: for each type
with such members it emits a blittable struct and the two functions that move an object in and out of
it, then registers them with `StateBridgeRegistry` from the same module initializer as the
declaration-order table.

```csharp
[LiveClass]
public partial class Lamp
{
    [LiveField(lane = FrameLane.State)] private float _intensity;
    [LiveField(lane = FrameLane.State)] private Vector3 _position;
    [LiveField]                         private string _label;   // input lane, not carried here
}
```

emits, inside `Lamp`:

```csharp
public struct LiveStateBlock { public float _intensity; public UnityEngine.Vector3 _position; }
internal static void CaptureLiveState(Lamp source, ref LiveStateBlock block) { ... }
internal static void ApplyLiveState(in LiveStateBlock block, Lamp target) { ... }
```

### Rules

| | |
|---|---|
| The owner must be `partial` | The block is emitted **inside** the type. The convention here is a private field with the attribute on it, and a free function could not read one. Not partial → `LRC001`, and nothing is emitted for that type |
| The owner must not be nested | Not supported yet → `LRC003` |
| A member's type must be unmanaged | Asked of the compiler rather than kept as a list of blessed types, so enums, `Vector3`, `Color` and anyone's own struct all work without being named. `string`, arrays, classes and `Nullable<T>` are refused → `LRC002`, and the member is left out |

A refused member is a warning rather than an error: leaving it in the input lane is a legitimate
answer, and the recording is still correct — it just carries that member when it changes instead of
every frame.

### Why the block is not shared with the scene file

A scene snapshot holds the members that are **persisted**. A frame holds every member declared
`State` whether it is persisted or not, because a member that changes the world without being saved
is exactly the one that makes a resynchronised machine drift straight back out of step. The block is
therefore a superset of what a snapshot carries, not the same set in a different encoding.

## Fallback behavior

When the generator is disabled (`CS9057`, missing DLL, unsupported runtime), `LiveClassDeclarationOrderTable.Register` is never called and `GetDeclarationOrderIndex` returns `-1` for every member of the type. Explicit `order` values on `[LiveProperty]`/`[LiveField]`/`[LiveFunction]` still decide the ordering, but members sharing an `order` all tie on the second key, so their relative order is left to `List<T>.Sort` — an unstable introsort whose output is implementation-defined. It is **not** source order, and it is not grouped by member kind either. `LiveClass` logs a one-time warning per type in this state (see `RegisterProperties`). The remote client still works, but pane layouts are arbitrary wherever explicit `order` does not pin them down.
