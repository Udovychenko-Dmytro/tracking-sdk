using System;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class BatchingTests
    {
        private sealed class Pipeline
        {
            public EventDispatcher Dispatcher;
            public EventQueue Queue;
            public RecordingTransport Transport;
            public FakeClock Clock;

            public void Enqueue(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    Queue.TryEnqueue(new QueuedEvent { Event = TestEvents.Message("m" + i, Clock.UtcNow) }, out _);
                }
            }

            public void PumpOnce() => Dispatcher.PumpOnceAsync().GetAwaiter().GetResult();
            public void Drain() => Dispatcher.DrainAsync().GetAwaiter().GetResult();
        }

        private static Pipeline NewPipeline(int batchSize, TimeSpan flushInterval, int capacity = 1000)
        {
            FakeClock clock = new FakeClock(TestEvents.T0);
            RecordingTransport transport = new RecordingTransport();
            TrackingConfig config = new TrackingConfig
            {
                BatchSize = batchSize,
                FlushInterval = flushInterval,
                MaxQueueCapacity = capacity,
            };
            EventQueue queue = new EventQueue(capacity, config.OverflowPolicy);
            EventDispatcher dispatcher = new EventDispatcher(queue, transport, clock, config);
            return new Pipeline { Dispatcher = dispatcher, Queue = queue, Transport = transport, Clock = clock };
        }

        [Test]
        public void Pump_SendsExactlyOneBatch_PerBatchSizeEvents()
        {
            Pipeline p = NewPipeline(batchSize: 5, flushInterval: TimeSpan.FromSeconds(10));
            p.Enqueue(5);

            p.PumpOnce();

            Assert.AreEqual(1, p.Transport.Batches.Count, "5 events / batch size 5 => one request");
            Assert.AreEqual(5, p.Transport.Batches[0].Count);
        }

        [Test]
        public void Pump_DoesNotSendPartialBatch_BeforeFlushInterval()
        {
            Pipeline p = NewPipeline(batchSize: 10, flushInterval: TimeSpan.FromSeconds(5));
            p.Enqueue(3); // below batch size

            p.PumpOnce();

            Assert.AreEqual(0, p.Transport.Batches.Count, "a partial batch waits for the flush interval");
        }

        [Test]
        public void Pump_FlushesPartialBatch_AfterFlushInterval_OnVirtualClock()
        {
            Pipeline p = NewPipeline(batchSize: 10, flushInterval: TimeSpan.FromSeconds(5));
            p.Enqueue(3);

            p.PumpOnce();
            Assert.AreEqual(0, p.Transport.Batches.Count);

            p.Clock.Advance(TimeSpan.FromSeconds(5)); // interval elapses — no real delay
            p.PumpOnce();

            Assert.AreEqual(1, p.Transport.Batches.Count);
            Assert.AreEqual(3, p.Transport.Batches[0].Count);
        }

        [Test]
        public void Drain_SendsAllEvents_InFifoOrder_AcrossMultipleBatches()
        {
            Pipeline p = NewPipeline(batchSize: 5, flushInterval: TimeSpan.FromSeconds(10));
            p.Enqueue(12);

            p.Drain();

            Assert.AreEqual(12, p.Transport.Events.Count);
            Assert.AreEqual(3, p.Transport.Batches.Count, "12 / 5 => batches of 5, 5, 2");
            for (int i = 0; i < 12; i++)
            {
                Assert.AreEqual("m" + i, p.Transport.Events[i].Payload["message"], "FIFO order preserved");
            }
        }
    }
}
