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

            _writer.WriteEvents(frame.events, symbols, excludeObjectIds);
            _writer.EndFrame();
        }

        public void Dispose() => Stop();
    }
}
