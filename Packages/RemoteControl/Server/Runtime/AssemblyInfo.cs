// Copyright (c) You-Ri, 2026

using System.Runtime.CompilerServices;
using Lilium.RemoteControl;

// テストアセンブリからハンドラ等の internal メンバーへのアクセスを許可。
[assembly: InternalsVisibleTo("Lilium.RemoteControl.Editor.Tests")]
[assembly: InternalsVisibleTo("Lilium.RemoteControl.Tests")]

// Declares the source recorded for anything arriving over REST. Static rather than registered on
// first use, so the set of sources is settled before a recording starts.
[assembly: FrameSource("rest")]
