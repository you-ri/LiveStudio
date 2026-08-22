// Copyright (c) You-Ri, 2026
using System.Runtime.CompilerServices;

// The UI Designer lives in its own editor assembly but draws from the same shared stylesheet
// and USS vocabulary (RemoteControlEditorStyles), which stays internal to keep the package's
// public API to what consumers actually script against.
[assembly: InternalsVisibleTo("Lilium.RemoteControl.UI.Editor")]
