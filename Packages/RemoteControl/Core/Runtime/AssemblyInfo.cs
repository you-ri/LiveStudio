// Copyright (c) You-Ri, 2026

using System.Runtime.CompilerServices;

// Allow sibling RemoteControl assemblies and test assemblies to access internal members.
[assembly: InternalsVisibleTo("Lilium.RemoteControl")]
[assembly: InternalsVisibleTo("Lilium.RemoteControl.Editor.Tests")]
[assembly: InternalsVisibleTo("Lilium.RemoteControl.Tests")]
