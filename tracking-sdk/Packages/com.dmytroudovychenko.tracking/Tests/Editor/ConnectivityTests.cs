using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class ConnectivityTests
    {
        [Test]
        public void UnityConnectivity_IsOnline_DoesNotThrow_FromWorkerThread()
        {
            // Snapshot is taken on the main (test) thread; the worker must only read the cached value.
            UnityConnectivity connectivity = new UnityConnectivity();
            Exception captured = null;
            Task.Run(() =>
            {
                try
                {
                    bool unused = connectivity.IsOnline;
                }
                catch (Exception e)
                {
                    captured = e;
                }
            }).GetAwaiter().GetResult();
            Assert.IsNull(captured, "IsOnline must read a cached snapshot, not a main-thread-only Unity API");
        }

        [Test]
        public void Events_AreHeld_WhileOffline_AndDelivered_WhenBackOnline()
        {
            FakeConnectivity connectivity = new FakeConnectivity { IsOnline = false };
            RecordingTransport transport = new RecordingTransport();
            FakeClock clock = new FakeClock(TestEvents.T0);
            TrackingConfig config = new TrackingConfig { BatchSize = 10, FlushInterval = TimeSpan.FromSeconds(1) };
            EventQueue queue = new EventQueue(TestConstants.DEFAULT_QUEUE_CAPACITY, config.OverflowPolicy);
            EventDispatcher dispatcher = new EventDispatcher(queue, transport, clock, config, connectivity: connectivity);

            for (int i = 0; i < 3; i++)
            {
                queue.TryEnqueue(new QueuedEvent { Event = TestEvents.Message("m" + i, clock.UtcNow) }, out _);
            }

            dispatcher.DrainAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0, transport.Events.Count, "nothing sent while offline");
            Assert.AreEqual(3, queue.Count, "events remain buffered");

            connectivity.IsOnline = true;
            dispatcher.DrainAsync().GetAwaiter().GetResult();
            Assert.AreEqual(3, transport.Events.Count, "flushed once back online");
        }
    }
}
