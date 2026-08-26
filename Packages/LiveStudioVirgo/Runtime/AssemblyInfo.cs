// Copyright (c) You-Ri, 2026
using Lilium.RemoteControl;

// The capture stream arriving from Fusion. Declared rather than named at the call site so the set
// of sources is settled before a recording starts, and so a misspelling fails when it is resolved.
[assembly: FrameSource("fusion")]
