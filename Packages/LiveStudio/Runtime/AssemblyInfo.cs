// Copyright (c) You-Ri, 2026

using System.Runtime.CompilerServices;

// Expose internals to the edit-mode test assembly so white-box unit tests can reach internal helpers and
// runtime-only fields (e.g. OperationManager.TryGetFiringContext, OperationSet.lastValue) that are intentionally
// not part of the public or exposed surface.
[assembly: InternalsVisibleTo("Lilium.LiveStudio.Editor.Tests")]
