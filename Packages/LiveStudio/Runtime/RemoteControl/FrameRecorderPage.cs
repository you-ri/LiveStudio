// Copyright (c) You-Ri, 2026

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// The recorder page in the remote app: one section for recording a take, one for playing one
    /// back. The studio UI definition points a side menu entry at this class.
    ///
    /// Presentation only. Everything reaches through to the <see cref="FrameRecorderController"/> in
    /// the scene, through <see cref="FrameRecorderController.instance"/>.
    ///
    /// Declaring the page here rather than in the remote app is what keeps it out of the other
    /// applications: a static class is registered wherever this assembly is linked, but only a host
    /// whose UI definition lists this entry grows the menu item.
    /// </summary>
    [LiveClass(Icon = "fiber_manual_record")]
    public static class FrameRecorderPage
    {
        // Order is explicit throughout. A member with no [Section] joins the section above it, and
        // functions are not always in the source generator's declaration table -- without an order
        // they can drift into the wrong section. Same reason Fusion's page spells its order out.
        //
        // Only a value that has to be persisted is bound as a LivePropertyRef. A ref delegates the
        // value, the dirty flag and the persistence to the component, which is what a saved setting
        // needs -- but it also redirects the remote app's lookup of a [StringSelector] source to the
        // referenced object, where a source declared on this page does not exist. Everything else
        // goes through plain properties, which cost nothing here because the component marks them
        // as not persisted anyway.

        // === Record ===

        [Section("fiber_manual_record", "SECTION_FRAME_RECORD_TITLE", "SECTION_FRAME_RECORD_SUBTITLE")]
        [LiveProperty(order = 10)]
        public static int take
        {
            get => FrameRecorderController.instance?.take ?? 1;
            set
            {
                var recorder = FrameRecorderController.instance;
                if (recorder != null) recorder.take = value;
            }
        }

        [LiveProperty(order = 11)]
        public static bool isRecording => FrameRecorderController.instance?.isRecording ?? false;

        [LiveProperty(order = 12)]
        public static int recordedFrames => FrameRecorderController.instance?.recordedFrames ?? 0;

        [LiveProperty(order = 13)]
        public static float recordedMegabytes =>
            FrameRecorderController.instance?.recordedMegabytes ?? 0f;

        // Bound by name rather than by type: this runs at type initialization, which can happen
        // before the class registry is populated, and a lookup by type silently falls back to the
        // C# type name -- which is not what the component is exposed as.
        [LiveField(order = 14)]
        [Help("FRAME_RECORDER_KEYFRAMEINTERVAL")]
        public static readonly LivePropertyRef keyframeInterval = LivePropertyRef.Create(
            FrameRecorderController.kLiveClassName, "_keyframeInterval", typeof(int));

        [LiveFunction(label = "RECORD", icon = "fiber_manual_record", order = 15)]
        public static void Record() => FrameRecorderController.instance?.Record();

        [LiveFunction(label = "STOP", icon = "stop", order = 16)]
        public static void Stop() => FrameRecorderController.instance?.Stop();

        // === Replay ===

        [Section("play_circle", "SECTION_FRAME_REPLAY_TITLE", "SECTION_FRAME_REPLAY_SUBTITLE")]
        [LiveProperty(order = 20)]
        [StringSelector(nameof(availableRecordings))]
        public static string replayFilename
        {
            get => FrameRecorderController.instance?.replayFilename ?? string.Empty;
            set
            {
                var recorder = FrameRecorderController.instance;
                if (recorder != null) recorder.replayFilename = value;
            }
        }

        /// <summary>
        /// Source for the picker above. Not drawn itself, but ordered into the section anyway so it
        /// travels with the members the page displays rather than sitting outside every section.
        /// </summary>
        [LiveProperty(order = 21), Hide]
        public static string[] availableRecordings =>
            FrameRecorderController.GetAvailableRecordings();

        [LiveProperty(order = 22)]
        public static bool isReplaying => FrameRecorderController.instance?.isReplaying ?? false;

        [LiveProperty(order = 23)]
        public static long replayFrame => FrameRecorderController.instance?.replayFrame ?? -1;

        [LiveFunction(label = "REPLAY", icon = "play_arrow", order = 24)]
        public static void Replay() => FrameRecorderController.instance?.Replay();

        [LiveFunction(label = "STOP_REPLAY", icon = "stop_circle", order = 25)]
        public static void StopReplay() => FrameRecorderController.instance?.StopReplay();
    }
}
