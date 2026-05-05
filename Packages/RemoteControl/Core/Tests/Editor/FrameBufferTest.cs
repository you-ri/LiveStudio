// Copyright (c) You-Ri, 2026
// Consolidated from Lilium.Virgo.Tests.FrameBufferTest into Lilium.RemoteControl.Tests.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Lilium.RemoteControl.Tests
{
    public class FrameBufferTest
    {
        [Test]
        public void FrameBuffer_SetAndGetData()
        {
            var frameBuffer = new FrameBuffer<int>(10);

            int data1 = 42;
            int data2 = 84;

            Assert.IsTrue(frameBuffer.Set(0, data1));
            Assert.IsTrue(frameBuffer.Set(1, data2));

            int retrievedData1 = 0;
            int retrievedData2 = 0;

            Assert.IsTrue(frameBuffer.TryGet(0, out retrievedData1));
            Assert.IsTrue(frameBuffer.TryGet(1, out retrievedData2));

            Assert.AreEqual(data1, retrievedData1);
            Assert.AreEqual(data2, retrievedData2);
        }

        [Test]
        public void FrameBuffer_SequentialFrames()
        {
            var frameBuffer = new FrameBuffer<float>(5);

            // Write a value to each consecutive frame.
            for (int frame = 0; frame < 5; frame++)
            {
                float data = frame * 1.5f;
                Assert.IsTrue(frameBuffer.Set(frame, data));
                Assert.AreEqual(frame + 1, frameBuffer.frameCount);
            }

            // Read back and verify each value.
            for (int frame = 0; frame < 5; frame++)
            {
                float retrievedData = 0;
                Assert.IsTrue(frameBuffer.TryGet(frame, out retrievedData));
                Assert.AreEqual(frame * 1.5f, retrievedData, 0.001f);
            }
        }

        [Test]
        public void FrameBuffer_CircularBuffer()
        {
            var frameBuffer = new FrameBuffer<int>(3);

            // Write more frames than the buffer can hold.
            for (int frame = 0; frame < 6; frame++)
            {
                int data = frame + 100;
                Assert.IsTrue(frameBuffer.Set(frame, data));
            }

            // Only the most recent 3 frames should be retrievable.
            for (int frame = 3; frame < 6; frame++)
            {
                int retrievedData = 0;
                Assert.IsTrue(frameBuffer.TryGet(frame, out retrievedData));
                Assert.AreEqual(frame + 100, retrievedData);
            }

            // Older frames must no longer be accessible.
            int oldData = 0;
            Assert.IsFalse(frameBuffer.TryGet(0, out oldData));
            Assert.IsFalse(frameBuffer.TryGet(1, out oldData));
            Assert.IsFalse(frameBuffer.TryGet(2, out oldData));
        }

        [Test]
        public void FrameBuffer_SingleChannel()
        {
            var frameBuffer = new FrameBuffer<Vector3>(4);

            Vector3 pos = new Vector3(1, 2, 3);

            Assert.IsTrue(frameBuffer.Set(0, pos));

            Vector3 retrievedPos = Vector3.zero;

            Assert.IsTrue(frameBuffer.TryGet(0, out retrievedPos));

            Assert.AreEqual(pos, retrievedPos);
        }

        [Test]
        public void FrameBuffer_InvalidFrameAccess()
        {
            var frameBuffer = new FrameBuffer<int>(5);

            int data = 42;
            frameBuffer.Set(2, data);

            int retrievedData = 0;
            // Frames not yet written must not be accessible.
            Assert.IsFalse(frameBuffer.TryGet(3, out retrievedData));
            Assert.IsFalse(frameBuffer.TryGet(1, out retrievedData));

            // Negative frame numbers must be rejected.
            Assert.IsFalse(frameBuffer.TryGet(-1, out retrievedData));
        }

        [Test]
        public void FrameBuffer_MaxFrameCount()
        {
            var frameBuffer = new FrameBuffer<int>(5);

            Assert.AreEqual(0, frameBuffer.frameCount);

            int data = 10;
            frameBuffer.Set(3, data);
            Assert.AreEqual(4, frameBuffer.frameCount);

            frameBuffer.Set(1, data);
            Assert.AreEqual(4, frameBuffer.frameCount);
        }

        [Test]
        public void FrameBuffer_Properties()
        {
            var frameBuffer = new FrameBuffer<int>(10);

            Assert.AreEqual(10, frameBuffer.size);
            Assert.AreEqual(1, frameBuffer.channels);
        }

        [Test]
        public void FrameBuffer_Dispose()
        {
            var frameBuffer = new FrameBuffer<int>(5);
            int data = 42;
            frameBuffer.Set(0, data);

            // Dispose must not throw.
            Assert.DoesNotThrow(() => frameBuffer.Dispose());
        }

        [Test]
        public void FrameBuffer_OverwriteData()
        {
            var frameBuffer = new FrameBuffer<int>(5);

            int originalData = 100;
            int newData = 200;

            // Writing to the same frame twice keeps the latest value.
            Assert.IsTrue(frameBuffer.Set(2, originalData));
            Assert.IsTrue(frameBuffer.Set(2, newData));

            int retrievedData = 0;
            Assert.IsTrue(frameBuffer.TryGet(2, out retrievedData));
            Assert.AreEqual(newData, retrievedData);
        }

        [Test]
        public void FrameBuffer_NonSequentialFrameAccess()
        {
            var frameBuffer = new FrameBuffer<int>(10);

            // Write to non-contiguous frame numbers.
            int data1 = 10, data5 = 50, data8 = 80;
            frameBuffer.Set(1, data1);
            frameBuffer.Set(5, data5);
            frameBuffer.Set(8, data8);

            Assert.AreEqual(9, frameBuffer.frameCount);

            int retrieved = 0;
            Assert.IsTrue(frameBuffer.TryGet(1, out retrieved));
            Assert.AreEqual(10, retrieved);

            Assert.IsTrue(frameBuffer.TryGet(5, out retrieved));
            Assert.AreEqual(50, retrieved);

            Assert.IsTrue(frameBuffer.TryGet(8, out retrieved));
            Assert.AreEqual(80, retrieved);

            // Untouched frames must remain inaccessible.
            Assert.IsFalse(frameBuffer.TryGet(0, out retrieved));
            Assert.IsFalse(frameBuffer.TryGet(3, out retrieved));
            Assert.IsFalse(frameBuffer.TryGet(7, out retrieved));
        }

        [Test]
        public void FrameBuffer_ThreadSafety_ConcurrentReadWrite()
        {
            const int bufferSize = 100;
            const int totalFrames = 1000;
            const int readerThreads = 4;

            var frameBuffer = new FrameBuffer<int>(bufferSize);
            var writerComplete = false;
            var exceptions = new List<System.Exception>();
            var exceptionsLock = new object();

            // Writer thread.
            var writerTask = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < totalFrames; i++)
                    {
                        frameBuffer.Set(i, i * 10);
                    }
                }
                catch (System.Exception ex)
                {
                    lock (exceptionsLock)
                    {
                        exceptions.Add(ex);
                    }
                }
                finally
                {
                    writerComplete = true;
                }
            });

            // Reader threads.
            var readerTasks = new Task[readerThreads];
            for (int r = 0; r < readerThreads; r++)
            {
                readerTasks[r] = Task.Run(() =>
                {
                    try
                    {
                        while (!writerComplete || frameBuffer.frameCount < totalFrames)
                        {
                            var currentFrameCount = frameBuffer.frameCount;
                            if (currentFrameCount > 0)
                            {
                                long frameToRead = (currentFrameCount - 1) % bufferSize;
                                if (frameToRead >= 0 && frameToRead < currentFrameCount)
                                {
                                    frameBuffer.TryGet(frameToRead, out int data);
                                }

                                frameBuffer.TryGetLastFrameData(out int lastData);

                                frameBuffer.IsExistFrame(frameToRead);
                            }
                            Thread.Yield();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        lock (exceptionsLock)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
            }

            writerTask.Wait();
            Task.WaitAll(readerTasks);

            Assert.IsEmpty(exceptions, $"Exceptions occurred: {string.Join(", ", exceptions)}");

            Assert.AreEqual(totalFrames, frameBuffer.frameCount);

            // Only the last bufferSize frames are guaranteed to remain in the ring.
            for (long i = totalFrames - bufferSize; i < totalFrames; i++)
            {
                int data;
                Assert.IsTrue(frameBuffer.TryGet(i, out data), $"Frame {i} should exist");
                Assert.AreEqual((int)(i * 10), data, $"Frame {i} data mismatch");
            }

            frameBuffer.Dispose();
        }

        [Test]
        public void FrameBuffer_ThreadSafety_ConcurrentWrites()
        {
            const int bufferSize = 50;
            const int framesPerThread = 100;
            const int writerThreads = 4;

            var frameBuffer = new FrameBuffer<long>(bufferSize);
            var exceptions = new List<System.Exception>();
            var exceptionsLock = new object();

            var writerTasks = new Task[writerThreads];
            for (int w = 0; w < writerThreads; w++)
            {
                int writerId = w;
                writerTasks[w] = Task.Run(() =>
                {
                    try
                    {
                        int startFrame = writerId * framesPerThread;
                        for (int i = 0; i < framesPerThread; i++)
                        {
                            long frameNo = startFrame + i;
                            frameBuffer.Set(frameNo, frameNo * 100 + writerId);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        lock (exceptionsLock)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
            }

            Task.WaitAll(writerTasks);

            Assert.IsEmpty(exceptions, $"Exceptions occurred: {string.Join(", ", exceptions)}");

            long expectedMaxFrameCount = writerThreads * framesPerThread;
            Assert.AreEqual(expectedMaxFrameCount, frameBuffer.frameCount);

            frameBuffer.Dispose();
        }

        [Test]
        public void FrameBuffer_ThreadSafety_ResetDuringAccess()
        {
            const int bufferSize = 20;
            var frameBuffer = new FrameBuffer<int>(bufferSize);
            var exceptions = new List<System.Exception>();
            var exceptionsLock = new object();
            var stopSignal = false;

            for (int i = 0; i < bufferSize; i++)
            {
                frameBuffer.Set(i, i);
            }

            var readerTask = Task.Run(() =>
            {
                try
                {
                    while (!stopSignal)
                    {
                        frameBuffer.TryGet(0, out int data);
                        frameBuffer.TryGetLastFrameData(out int lastData);
                        frameBuffer.IsExistFrame(5);
                        Thread.Yield();
                    }
                }
                catch (System.Exception ex)
                {
                    lock (exceptionsLock)
                    {
                        exceptions.Add(ex);
                    }
                }
            });

            // Reset the buffer multiple times while a reader is active.
            for (int i = 0; i < 10; i++)
            {
                frameBuffer.Reset();
                Thread.Sleep(1);

                for (int j = 0; j < bufferSize; j++)
                {
                    frameBuffer.Set(j, j + i * 100);
                }
            }

            stopSignal = true;
            readerTask.Wait();

            Assert.IsEmpty(exceptions, $"Exceptions occurred: {string.Join(", ", exceptions)}");

            frameBuffer.Dispose();
        }
    }
}
