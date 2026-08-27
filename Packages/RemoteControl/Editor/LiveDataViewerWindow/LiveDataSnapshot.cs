// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Editor.LiveDataViewer
{
    /// <summary>One element of one state block, as much of it as the viewer keeps every frame.</summary>
    internal struct ElementRow
    {
        /// <summary>Interned id of the owner, in the id space of whoever supplied the frame.</summary>
        public int ownerId;

        /// <summary>The owner's id resolved to text, or empty when the table does not know it.</summary>
        public string owner;

        /// <summary>Which producer put it there.</summary>
        public string source;

        /// <summary>
        /// The producer's own stamp. Not comparable across types -- a capture source writes the
        /// sender's frame number and the state system writes this frame's -- so it is shown as it
        /// is and never turned into an age.
        /// </summary>
        public long time;

        /// <summary>
        /// Session frame in which <see cref="time"/> last moved. This is the freshness signal: a
        /// block keeps its last value forever once a producer stops writing, so "still being
        /// written" cannot be read off the element itself.
        /// </summary>
        public long lastChangedFrame;
    }

    /// <summary>One state block: a type, and the elements it holds.</summary>
    internal sealed class TypeRow
    {
        public string typeName;
        public Type elementType;
        public int elementSize;
        public readonly List<ElementRow> elements = new List<ElementRow>();
    }

    /// <summary>One entry of the inventory.</summary>
    internal struct StructureRow
    {
        public int objectId;
        public string objectName;
        public int typeId;
        public string typeName;
        public int parentId;
        public string parentName;
    }

    /// <summary>One recorded input, kept in the viewer's own ring.</summary>
    internal struct InputRow
    {
        public long frameNumber;
        public long sequence;
        public InputKind kind;
        public string source;
        public string verb;
        public string target;

        /// <summary>Name of the type <see cref="payload"/> holds, or null when it holds nothing.</summary>
        public string payloadTypeName;

        /// <summary>
        /// The value as bytes. Copied out of the record because the record's slot is reused by the
        /// next frame, and the viewer keeps a ring of these to look back through.
        /// </summary>
        public byte[] payload;

        public bool faulted;
        public bool truncated;
    }

    /// <summary>
    /// What the viewer keeps of one frame.
    ///
    /// Filled inside the gate's notification, which is the point every input is waiting on, so it
    /// holds only what is cheap to take: the header, the shape of each block, and the metadata of
    /// each element. The values themselves stay where they are -- 1.4 KB per element per frame is
    /// not something to copy for a window that redraws twenty times a second -- and only the one
    /// element the viewer is looking at is taken.
    ///
    /// The lists are reused between frames. Clearing keeps their capacity, so a steady run stops
    /// allocating after the first few frames.
    /// </summary>
    internal sealed class LiveDataSnapshot
    {
        /// <summary>Before any frame has been taken. Not zero, which is a real frame number.</summary>
        public long frameNumber = -1;
        public FrameRate frameRate;
        public bool isSupplied;
        public long structureEpoch;
        public bool hasSink;
        public bool hasSource;

        /// <summary>Blocks present on this frame, in the order the lane holds them.</summary>
        public readonly List<TypeRow> types = new List<TypeRow>();

        /// <summary>The inventory as of this frame.</summary>
        public readonly List<StructureRow> structure = new List<StructureRow>();

        /// <summary>Bytes of the element the viewer is looking at, or empty.</summary>
        public byte[] selectedValue = Array.Empty<byte>();

        public int selectedValueLength;
        public string selectedType;
        public int selectedOwnerId;

        /// <summary>Reused so a steady run stops allocating rows after the first frames.</summary>
        public TypeRow GetOrAddType(int index)
        {
            while (types.Count <= index) types.Add(new TypeRow());
            return types[index];
        }

        public void TrimTypes(int count)
        {
            if (types.Count > count) types.RemoveRange(count, types.Count - count);
        }
    }
}
