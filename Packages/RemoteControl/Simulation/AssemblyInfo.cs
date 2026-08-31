// Copyright (c) You-Ri, 2026

using System.Runtime.CompilerServices;

// The simulation side of live data: the frame structures, the state and structure blocks, and the
// recording format. The host assembly (Lilium.RemoteControl) drives them and bridges to the view.
//
// The boundary here is the dependency direction, not a ban on the engine. This assembly does not
// reference the host, so it cannot reach live objects, the registry or the bridges -- which is what
// keeps the simulation from reading back out of the view. It does keep engine references, because
// the native containers and the job system live there and this is the layer meant to run on them.
//
// What that leaves as a convention rather than a compile error: do not read the view's output here
// (an evaluated Transform is not reproducible from a recording), and read time from the live data
// rather than from UnityEngine.Time or the wall clock. See Documents/LiveDataCore.md, "Simulation
// and view".

// The host drives the frame lifecycle (FrameGate) and owns the bridges, so it needs the internal
// seams of the frame structures -- Reset/Add/Commit and the like -- which are deliberately not
// public API.
[assembly: InternalsVisibleTo("Lilium.RemoteControl")]

// Tests exercise the same internal seams.
[assembly: InternalsVisibleTo("Lilium.RemoteControl.Editor.Tests")]
