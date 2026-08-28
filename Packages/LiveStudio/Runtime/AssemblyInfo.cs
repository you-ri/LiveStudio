// Copyright (c) You-Ri, 2026

using System.Runtime.CompilerServices;

// Expose internals to the edit-mode test assembly so white-box unit tests can reach internal helpers and
// runtime-only fields (e.g. OperationManager.TryGetFiringContext, OperationSet.lastValue) that are intentionally
// not part of the public or exposed surface.
[assembly: InternalsVisibleTo("Lilium.LiveStudio.Editor.Tests")]

// The operator's own controls: deck tiles, bound keys, gamepad axes. A separate producer from
// "rest" because a recording has to be able to tell what the operator did here from what came in
// over the network -- selecting or muting one of them is the whole point of tracks.
[assembly: Lilium.RemoteControl.FrameSource("operation")]
