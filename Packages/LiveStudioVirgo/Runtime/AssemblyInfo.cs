// Copyright (c) You-Ri, 2026
using Lilium.RemoteControl;

// The capture stream arriving from Fusion. Declared rather than named at the call site so the set
// of sources is settled before a recording starts, and so a misspelling fails when it is resolved.
[assembly: FrameSource("fusion")]

// The reference-point offsets are internal: recorded, but not part of anyone's API. The tests assert
// they survive a capture and come back on a replay, which means reading them.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Lilium.LiveStudio.Virgo.Editor.Tests")]
