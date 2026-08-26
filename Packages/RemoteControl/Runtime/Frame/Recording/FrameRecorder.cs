// Copyright (c) You-Ri, 2026
using System;
using System.IO;
using UnityEngine;

namespace Lilium.RemoteControl.Frames.Recording
{
    /// <summary>
    /// Writes every completed frame to a recording.
    ///
    /// Attaches as the gate's sink, so it sees frames at the head with the inputs already applied
    /// and the state blocks already written. Each frame is flushed as it happens rather than
    /// buffered, so a crash costs the frame in progress and nothing else.
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
        private InputSymbolTable _symbols;
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

        /// <summary>Frames that carried the inventory so far.</summary>
        public int keyframeCount => _writer?.keyframes.Count ?? 0;

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
            _writer = new FrameRecordWriter(stream, header, leaveOpen);
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

        public void OnFrameCompleted(in Frame frame, InputSymbolTable symbols)
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
            if (_writer.keyframes.Count != before) _lastKeyframeFrame = frame.frameNumber;

            _writer.WriteState(frame.state, symbols);
            _writer.WriteInputs(frame.inputs, symbols);
            _writer.EndFrame();
        }

        public void Dispose() => Stop();
    }
}
