// Copyright (c) You-Ri, 2026
using System;

namespace Lilium.RemoteControl.Editor.LiveDataViewer
{
    /// <summary>
    /// Where the viewer gets a frame from.
    ///
    /// Two of these exist and the window cannot tell them apart: the running gate, and a file. That
    /// is the point of the interface -- a recording has to be readable with the same eyes that
    /// watched it being made, or "the file is wrong" and "the viewer reads files differently" are
    /// indistinguishable.
    ///
    /// A live feed has one frame, always the latest. A file feed has all of them and a position,
    /// which is the only difference the window has to draw.
    /// </summary>
    internal interface ILiveDataFeed
    {
        /// <summary>Bumped whenever what this feed shows changes. A reader redraws when it moves.</summary>
        long version { get; }

        /// <summary>True once there is a frame to draw.</summary>
        bool hasFrame { get; }

        /// <summary>True while the feed can still produce frames.</summary>
        bool isAttached { get; }

        /// <summary>The frame being shown.</summary>
        LiveDataSnapshot snapshot { get; }

        /// <summary>Inputs available to list, oldest first.</summary>
        int inputCount { get; }

        InputRow GetInput(int index);

        /// <summary>Forgets the inputs listed so far, where that means anything.</summary>
        void ClearInputs();

        /// <summary>
        /// Names the one element whose value bytes are worth taking. Everything else is kept as
        /// metadata only, because a frame's values are far too much to copy at frame rate.
        /// </summary>
        void Select(string typeName, int ownerId);

        /// <summary>
        /// How many frames can be moved between, or 0 for a feed that only has "now".
        ///
        /// The window draws its transport from this alone, so a live feed needs no special case:
        /// zero frames to move between means no transport.
        /// </summary>
        int frameCount { get; }

        /// <summary>Position within <see cref="frameCount"/>, or -1.</summary>
        int frameIndex { get; }

        /// <summary>Moves to a position. Ignored by a feed that has no positions.</summary>
        void Seek(int index);

        /// <summary>What this feed is showing, for the title bar. Empty for the live gate.</summary>
        string label { get; }
    }

    /// <summary>
    /// The running gate as a feed.
    ///
    /// A wrapper rather than the tap implementing the interface itself: the tap is static so that a
    /// closed window cannot leave a dead observer behind, and a static class cannot implement an
    /// interface.
    /// </summary>
    internal sealed class LiveDataTapFeed : ILiveDataFeed
    {
        public static readonly LiveDataTapFeed instance = new LiveDataTapFeed();

        public long version => LiveDataTap.version;

        public bool hasFrame => LiveDataTap.hasFrame;

        public bool isAttached => LiveDataTap.isAttached;

        public LiveDataSnapshot snapshot => LiveDataTap.snapshot;

        public int inputCount => LiveDataTap.inputCount;

        public InputRow GetInput(int index) => LiveDataTap.GetInput(index);

        public void ClearInputs() => LiveDataTap.ClearInputs();

        public void Select(string typeName, int ownerId) => LiveDataTap.Select(typeName, ownerId);

        /// <summary>A live run has one frame: the one happening. There is nowhere to move to.</summary>
        public int frameCount => 0;

        public int frameIndex => -1;

        public void Seek(int index) { }

        public string label => string.Empty;
    }
}
