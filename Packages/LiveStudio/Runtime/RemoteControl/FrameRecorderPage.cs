// Copyright (c) You-Ri, 2026

using Lilium.RemoteControl;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// The recorder page in the remote app: a single section holding both recording a take and
    /// playing one back, the way the Fusion page's recorder reads. The studio UI definition points
    /// a side menu entry at this class.
    ///
    /// Presentation only. Everything reaches through to the <see cref="FrameRecorderController"/> in
    /// the scene, through <see cref="FrameRecorderController.instance"/>.
    ///
    /// Declaring the page here rather than in the remote app is what keeps it out of the other
    /// applications: a static class is registered wherever this assembly is linked, but only a host
    /// whose UI definition lists this entry grows the menu item.
    /// </summary>
    [LiveClass(Icon = "fiber_manual_record", lane = FrameLane.None)]
    public static class FrameRecorderPage
    {
        // Order is explicit throughout. A member with no [Section] joins the section above it, and
        // functions are not always in the source generator's declaration table -- without an order
        // they can drift into the wrong section. Same reason Fusion's page spells its order out.
        //
        // Every member is a plain property reaching through to the component, and none is a
        // LivePropertyRef. A ref delegates the value, the dirty flag and the persistence to the
        // component, which a saved setting needs -- but nothing this page carries is saved (the
        // component marks all four as not persisted), and a ref also redirects the remote app's
        // lookup of a [StringSelector] source to the referenced object, where the source declared
        // here does not exist.

        // lane = None on the class: nothing on this page is of the world being recorded. It is the
        // recorder's own controls, and a take that held them would, on replay, start and stop
        // recordings on the machine playing it back. Said on the type so a member added later
        // inherits the answer -- which is what the two per-member declarations here used to say,
        // one member at a time, while the calls next to them said nothing at all.

        // === Recording / Playback ===
        // One section, not two, and only the transport in it. Recording a take and playing one back
        // are the same job from the operator's side, and splitting them put two cards on a page that
        // holds this much. The record members come first and the replay members follow, by order
        // alone -- a [Section] on replayFilename would start a second card again.
        //
        // What is not here is deliberate. The status readouts (isRecording, recordedFrames,
        // recordedMegabytes, isReplaying, replayFrame) are the component's own and are read from the
        // recorder object; the encoding settings (keyframeInterval, compress) are project settings
        // that live in the project's settings file and the inspector. Neither is something an
        // operator reaches for between takes, and both crowded out the five controls that are.

        [Section("fiber_manual_record", "SECTION_FRAME_RECORDER_TITLE", "SECTION_FRAME_RECORDER_SUBTITLE")]
        // excludeObjectIds でもこのページは外れるが、除外リストに頼らず宣言でも言う
        // (コンポーネント側の _take も None)。宣言はクラス属性側にある。
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

        // === Replay ===

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
        [Help("FRAME_RECORDER_LOOP")]
        public static bool loop
        {
            get => FrameRecorderController.instance?.loop ?? false;
            set
            {
                var recorder = FrameRecorderController.instance;
                if (recorder != null) recorder.loop = value;
            }
        }

        /// <summary>
        /// Position in the take being replayed, 0 at the first frame and 1 at the last, the way the
        /// Fusion page's replay reads.
        ///
        /// Normalized rather than a frame index because a slider is what the operator gets: the
        /// remote app has no way to learn the length of the take before it draws the control, so a
        /// range in frames would have to be a number field instead.
        ///
        /// Reads 0 with nothing being replayed, and with a take that carries no index to seek by --
        /// writing it then does nothing rather than reporting a position that cannot be reached.
        /// </summary>
        [LiveProperty(order = 23)]
        [Slider(0f, 1f, 0.001f)]
        [Help("FRAME_RECORDER_SEEKPROGRESS")]
        public static float seekProgress
        {
            get
            {
                var recorder = FrameRecorderController.instance;
                if (recorder == null) return 0f;

                var count = recorder.replayFrameCount;
                if (count <= 1) return 0f;

                var index = recorder.replayIndex;
                if (index < 0) return 0f;

                return Mathf.Clamp01((float)index / (count - 1));
            }
            set
            {
                var recorder = FrameRecorderController.instance;
                if (recorder == null) return;

                var count = recorder.replayFrameCount;
                if (count <= 0) return;

                // A jump, not a scrub: the replay lands and carries on from there. Holding it would
                // need a switch to let go again, and the transport here is the three buttons below.
                recorder.SeekReplay(Mathf.RoundToInt(Mathf.Clamp01(value) * (count - 1)));
            }
        }

        // === Transport ===

        [LiveFunction(label = "RECORD", icon = "fiber_manual_record", order = 30)]
        public static void Record() => FrameRecorderController.instance?.Record();

        [LiveFunction(label = "REPLAY", icon = "play_arrow", order = 31)]
        public static void Replay() => FrameRecorderController.instance?.Replay();

        /// <summary>
        /// Stops whichever of the two is running. One button rather than a stop each, because an
        /// operator stops what is happening and only the page knows which that was -- the component
        /// already tears down a replay before it stops a recording, so this is exactly its Stop.
        /// </summary>
        [LiveFunction(label = "STOP", icon = "stop", order = 32)]
        public static void Stop() => FrameRecorderController.instance?.Stop();
    }
}
