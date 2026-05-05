// Copyright (c) You-Ri, 2026
// Consolidated from Lilium.Virgo.FrameBuffer into Lilium.RemoteControl.

using System;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Thread-safe ring buffer that stores time-indexed frame data of an unmanaged type.
    /// </summary>
    public class FrameBuffer<FrameData> : IDisposable
        where FrameData : unmanaged
    {
        public bool isValid
        {
            get { lock (_lock) { return _frameCount != 0; } }
        }

        public int size => _frameBufferSize;

        public long frameCount
        {
            get { lock (_lock) { return _frameCount; } }
        }

        public int channels => 1;

        readonly int _frameBufferSize;

        long _frameCount = 0;

        FrameData[] _data;

        long[] _frames;

        readonly object _lock = new object();

        public FrameBuffer(int frameCapacity)
        {
            _frameBufferSize = frameCapacity;
            _data = new FrameData[_frameBufferSize];
            _frames = new long[_frameBufferSize];
            _frames.AsSpan().Fill(-1);
        }

        public void Dispose()
        {
            _data = null;
            _frames = null;
        }

        public void Reset()
        {
            lock (_lock)
            {
                _data = new FrameData[_frameBufferSize];
                _frames = new long[_frameBufferSize];
                _frames.AsSpan().Fill(-1);
                _frameCount = 0;
            }
        }

        public bool Set(long frameNo, in FrameData data)
        {
            lock (_lock)
            {
                var frameIndex = frameNo % _frameBufferSize;
                _data[frameIndex] = data;
                _frames[frameIndex] = frameNo;

                if (frameNo + 1 > _frameCount)
                {
                    _frameCount = frameNo + 1;
                }
                return true;
            }
        }

        public bool TryGet(long frameNo, out FrameData data)
        {
            lock (_lock)
            {
                data = default;
                if (_IsExistFrameInternal(frameNo) == false)
                {
                    return false;
                }
                var frameIndex = frameNo % _frameBufferSize;
                data = _data[frameIndex];
                return true;
            }
        }

        public bool IsExistFrame(long frameNo)
        {
            lock (_lock)
            {
                return _IsExistFrameInternal(frameNo);
            }
        }

        private bool _IsExistFrameInternal(long frameNo)
        {
            if (frameNo < 0 || frameNo >= _frameCount)
            {
                return false;
            }
            var frameIndex = frameNo % _frameBufferSize;
            return _frames[frameIndex] == frameNo;
        }

        public bool TryGetLastFrameData(out FrameData data)
        {
            lock (_lock)
            {
                data = default;
                if (_frameCount == 0)
                {
                    return false;
                }

                long lastFrameNo = _frameCount - 1;
                var frameIndex = lastFrameNo % _frameBufferSize;

                if (_frames[frameIndex] != lastFrameNo)
                {
                    return false;
                }

                data = _data[frameIndex];
                return true;
            }
        }
    }
}
