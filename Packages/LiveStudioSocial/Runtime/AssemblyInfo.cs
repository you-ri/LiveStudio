// Copyright (c) You-Ri, 2026

using System.Runtime.CompilerServices;

// Expose internals to the edit-mode test assembly so white-box unit tests can reach the hub's reset and
// frame-source seams, which are deliberately kept off the public surface (see SocialEventHub).
[assembly: InternalsVisibleTo("Lilium.LiveStudio.Social.Editor.Tests")]
