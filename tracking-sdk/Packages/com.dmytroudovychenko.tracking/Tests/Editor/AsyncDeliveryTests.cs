using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class AsyncDeliveryTests
    {
        private static TrackingSystem NewTracker(out RecordingTransport transport, TrackingConfig config = null)
        {
            transport = new RecordingTransport();
            return new TrackingSystem(
                config ?? new TrackingConfig(),
                transport,
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo(TestConstants.TEST_PLATFORM, TestConstants.TEST_APP_VERSION),
                startWorker: false,                  // deterministic: delivery is pumped via FlushAsync
                delayer: new RecordingDelayer(),     // retries (if any) incur no real delay
                logger: NullTrackingLogger.Instance); // silence expected give-up warnings
        }

        [Test]
        public void SendMapAsync_DoesNotComplete_UntilBatchIsDelivered()
        {
            TrackingSystem tracker = NewTracker(out _);

            Task<bool> task = tracker.SendMapAsync(new Dictionary<string, object> { ["k"] = "v" });
            Assert.IsFalse(task.IsCompleted, "no delivery yet (worker not running)");

            tracker.Flush();

            Assert.IsTrue(task.IsCompleted);
            Assert.IsTrue(task.GetAwaiter().GetResult(), "resolves true once its batch is delivered");
        }

        [Test]
        public void SendMapAsync_ResolvesFalse_WhenTransportReportsFailure()
        {
            TrackingSystem tracker = NewTracker(out RecordingTransport transport);
            transport.NextResult = false;

            Task<bool> task = tracker.SendMapAsync(new Dictionary<string, object> { ["k"] = "v" });
            tracker.Flush();

            Assert.IsTrue(task.IsCompleted);
            Assert.IsFalse(task.GetAwaiter().GetResult());
        }

        [Test]
        public void SendMapAsync_ResolvesFalse_WhenEvictedByDropOldest()
        {
            TrackingConfig config = new TrackingConfig { MaxQueueCapacity = 1, OverflowPolicy = OverflowPolicy.DropOldest };
            TrackingSystem tracker = NewTracker(out _, config);

            Task<bool> evicted = tracker.SendMapAsync(new Dictionary<string, object> { ["n"] = 1 });
            Task<bool> survivor = tracker.SendMapAsync(new Dictionary<string, object> { ["n"] = 2 }); // evicts the first

            Assert.IsTrue(evicted.IsCompleted, "an evicted event's awaiter resolves immediately");
            Assert.IsFalse(evicted.GetAwaiter().GetResult(), "evicted => false");
            Assert.IsFalse(survivor.IsCompleted, "survivor stays pending until flush");

            tracker.Flush();
            Assert.IsTrue(survivor.GetAwaiter().GetResult());
        }

        [Test]
        public void SendMapAsync_WithNullValue_DropsEntry_KeepsValidOnes()
        {
            TrackingSystem tracker = NewTracker(out RecordingTransport transport);

            Task<bool> task = tracker.SendMapAsync(new Dictionary<string, object> { ["k"] = null, ["n"] = 1 });
            tracker.Flush();

            Assert.IsTrue(task.GetAwaiter().GetResult(), "valid entry remains — accepted");
            Assert.AreEqual(1, transport.Events.Count);
            Assert.IsFalse(transport.Events[0].Payload.ContainsKey("k"), "null-value entry filtered out");
            Assert.IsTrue(transport.Events[0].Payload.ContainsKey("n"), "valid entry kept");
        }

        [Test]
        public void SendMessage_ReturnsFalse_WhenQueueFull_UnderRejectNew()
        {
            TrackingConfig config = new TrackingConfig { MaxQueueCapacity = 2, OverflowPolicy = OverflowPolicy.RejectNew };
            TrackingSystem tracker = NewTracker(out _, config);

            Assert.IsTrue(tracker.SendMessage("a"));
            Assert.IsTrue(tracker.SendMessage("b"));
            Assert.IsFalse(tracker.SendMessage("c"), "queue full + RejectNew => false");
        }

        [Test]
        public void SendMessage_ReturnsTrue_WhenQueueFull_UnderDropOldest()
        {
            TrackingConfig config = new TrackingConfig { MaxQueueCapacity = 2, OverflowPolicy = OverflowPolicy.DropOldest };
            TrackingSystem tracker = NewTracker(out RecordingTransport transport, config);

            Assert.IsTrue(tracker.SendMessage("a"));
            Assert.IsTrue(tracker.SendMessage("b"));
            Assert.IsTrue(tracker.SendMessage("c"), "DropOldest never rejects the producer");

            tracker.Flush();

            // 'a' was evicted; 'b' and 'c' survive, in order.
            Assert.AreEqual(2, transport.Events.Count);
            Assert.AreEqual("b", transport.Events[0].Payload["message"]);
            Assert.AreEqual("c", transport.Events[1].Payload["message"]);
        }
    }
}
