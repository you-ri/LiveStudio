// Copyright (c) You-Ri, 2026
using System;
using System.IO;
using UnityEngine;

namespace Lilium.RemoteControl.Frames.Recording
{
    /// <summary>
    /// Writes every completed frame to a recording.
    ///
    /// Attaches as the gate's sink, so it sees frames at the head with the events already applied
    /// and the state blocks already written.
    ///
    /// What a crash costs depends on <see cref="compress"/>. Uncompressed, each frame is written as
    /// it happens and the loss is whatever the stream buffer had not yet put on disk. Compressed,
    /// frames are held until the next keyframe, so the loss is up to a keyframe interval.
    /// </summary>
    public sealed class FrameRecorder : IFrameSink, IDisposable
    {
        /// <summary>
        /// Frames between keyframes. 60 is one a second at sixty hertz.
        ///
        /// Setting this only decides how far a seek has to walk back for the shape of the world --
        /// the values are complete on every frame regardless. A keyframe costs the inventory and
        /// nothing else, so this can be tight.
        /// </summary>
        public const int kDefaultKeyframeInterval = 60;

        private FrameRecordWriter _writer;
        private FrameSymbolTable _symbols;
        private Stream _stream;
        private string _path;
        // The restated values of the frame being written. Kept and reused rather than built per
        // keyframe: it settles at the size of the exposed surface and stops allocating.
        private EventFrame _restateFrame;
        // Frame the last keyframe was written at, or -1 before the first. A frame number rather
        // than a counter: the interval is a distance between frames, and counting ticks instead
        // put the periodic keyframe one frame late.
        private long _lastKeyframeFrame = -1;

        /// <summary>True between <see cref="Start(string)"/> and <see cref="Stop"/>.</summary>
        public bool isRecording => _writer != null;

        /// <summary>Frames written so far, or zero when not recording.</summary>
        public int frameCount => _writer?.frameCount ?? 0;

        /// <summary>Bytes written so far, or zero when not recording.</summary>
        public long length => _writer?.length ?? 0;

        /// <summary>Where the current recording is being written, or null.</summary>
        public string path => _path;

        /// <summary>
        /// Frames between keyframes. Zero writes one only when the inventory actually changes, which
        /// leaves a seek walking back an unbounded distance for it.
        /// </summary>
        public int keyframeInterval { get; set; } = kDefaultKeyframeInterval;

        /// <summary>
        /// Compresses the recording, which measured about five times smaller over real takes
        /// (356 MB an hour down to roughly 70).
        ///
        /// The cost is what a crash takes with it. Uncompressed, entries reach the file as they
        /// happen; compressed, they are held until the next keyframe, so a process that dies loses
        /// up to a keyframe interval instead of whatever the stream buffer had not flushed. It also
        /// means the open chunk is not there for anything reading the file as it is written.
        ///
        /// Set before <see cref="Start(string)"/>; changing it mid-recording does nothing.
        /// </summary>
        public bool compress { get; set; }

        /// <summary>Frames that carried the inventory so far.</summary>
        public int keyframeCount => _writer?.keyframes.Count ?? 0;

        /// <summary>
        /// Writes the value of every event-lane member into each keyframe, so a seek has somewhere
        /// to read them from (see <see cref="LiveEventRestateSystem"/>).
        ///
        /// On by default, because without it the event lane holds only the changes made during the
        /// take: a value settled before recording started is in the file nowhere at all, and one set
        /// before the keyframe a seek lands on is behind the point it reads from. Both read as a
        /// replay that quietly keeps whatever the machine was already holding.
        ///
        /// Tied to keyframes rather than to an interval of its own, because a keyframe is where a
        /// seek starts reading. A restatement anywhere else would only be found by a player already
        /// walking through it, which is the case that did not need it.
        ///
        /// The cost is per keyframe rather than per frame: records are variable length on disk, so
        /// what this adds is the exposed surface written out about once a second.
        /// </summary>
        public bool restateValues { get; set; } = true;

        /// <summary>Members restated into the most recent keyframe.</summary>
        public int restatedMemberCount { get; private set; }

        /// <summary>
        /// Exposed objects whose events are left out of the recording. Set this to whatever is
        /// driving the recording -- both the page holding the controls and the component behind it.
        ///
        /// Their buttons are not part of the world being recorded, and keeping them means the replay
        /// presses them again: a recorded Record starts a second recording, and a recorded Stop tears
        /// down the replay that is running it. This is the design's "内部起点は記録から除外する"
        /// applied to the recorder itself.
        /// </summary>
        public string[] excludeObjectIds { get; set; }

        /// <summary>
        /// Fills in what a recording says about the run it came from. A recording does not replay
        /// against a different build, so this is what a reader checks rather than guesses.
        /// </summary>
        public static FrameRecordHeader DescribeRun(FrameRate frameRate, long startTicks)
            => new FrameRecordHeader
            {
                frameRate = frameRate,
                startTicks = startTicks,
                engineId = $"unity-{Application.unityVersion}",
                buildId = $"{Application.productName}-{Application.version}",
            };

        /// <summary>
        /// Starts recording to a file. The directory is created if it is missing, because failing
        /// after a take has already been performed is the worst possible moment to find out.
        /// </summary>
        public void Start(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("No path.", nameof(filePath));
            if (isRecording) throw new InvalidOperationException("[RemoteControl] Already recording.");

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            Start(stream, leaveOpen: false);
            _path = filePath;
        }

        /// <summary>Starts recording to a stream. For tests and for anything that is not a file.</summary>
        public void Start(Stream stream, bool leaveOpen = false)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (isRecording) throw new InvalidOperationException("[RemoteControl] Already recording.");

            _stream = stream;
            _symbols = null;
            _path = null;
            _lastKeyframeFrame = -1;

            var header = DescribeRun(FrameGate.clock.frameRate, DateTime.UtcNow.Ticks);
            _writer = new FrameRecordWriter(stream, header, leaveOpen, compress);
        }

        /// <summary>
        /// Finishes the recording: writes the tail and closes. Safe to call when not recording.
        ///
        /// Without this the file has no index and no complete mapping table, which costs seeking but
        /// not readability -- both can be rebuilt by walking the entries.
        /// </summary>
        public void Stop()
        {
            if (_writer == null) return;

            _writer.Close(_symbols);
            _writer.Dispose();

            _writer = null;
            _symbols = null;
            _stream = null;
            _path = null;

            _restateFrame?.Dispose();
            _restateFrame = null;
        }

        public void OnFrameCompleted(in Frame frame, FrameSymbolTable symbols)
        {
            if (_writer == null) return;

            // Kept so Stop can write the complete table even if the last frame carried no symbols.
            _symbols = symbols;

            _writer.BeginFrame(in frame, symbols);

            // A keyframe is a frame that carries the inventory again even though nothing about it
            // moved. Structural changes write it anyway, so this is only the periodic one.
            var periodic = keyframeInterval > 0 &&
                           (_lastKeyframeFrame < 0 ||
                            frame.frameNumber - _lastKeyframeFrame >= keyframeInterval);

            var before = _writer.keyframes.Count;
            _writer.WriteStructure(frame.structure, symbols, periodic);

            var isKeyframe = _writer.keyframes.Count != before;
            if (isKeyframe) _lastKeyframeFrame = frame.frameNumber;

            _writer.WriteState(frame.state, symbols);

            // Before the frame's own events, so a value that really was written on this frame stands
            // as the later of the two. The restatement says how things stood; the event says what
            // someone did, and what someone did is what a reader has to end up with.
            if (isKeyframe && restateValues) _WriteRestatedValues(in frame, symbols);

            _writer.WriteEvents(frame.events, symbols, excludeObjectIds);
            _writer.EndFrame();
        }

        /// <summary>
        /// Reads the world's event-lane values and writes them as this keyframe's leading records.
        ///
        /// Written from a frame of its own rather than into the one being recorded: what goes in
        /// here never happened, and the live frame is handed to observers and mirrors that would
        /// have no way to tell the difference.
        /// </summary>
        private void _WriteRestatedValues(in Frame frame, FrameSymbolTable symbols)
        {
            _restateFrame ??= new EventFrame();
            _restateFrame.Reset(frame.frameNumber, frame.frameRate);

            restatedMemberCount = LiveEventRestateSystem.RestateInto(_restateFrame, symbols);
            if (restatedMemberCount == 0) return;

            _writer.WriteEvents(_restateFrame, symbols, excludeObjectIds);
        }

        public void Dispose() => Stop();
    }
}
