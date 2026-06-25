using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class MetricsTests
    {
        private static TrackingSystem NewTracker(out RecordingTransport transport, TrackingConfig config = null)
        {
            transport = new RecordingTransport();
            return new TrackingSystem(
                config ?? new TrackingConfig(),
                transport,
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo(TestConstants.TEST_PLATFORM, TestConstants.TEST_APP_VERSION),
                startWorker: false,
                delayer: new RecordingDelayer(),
                logger: NullTrackingLogger.Instance);
        }

        [Test]
        public void Metrics_CountEnqueuedThenSent()
        {
            TrackingSystem tracker = NewTracker(out _);

            tracker.SendMessage("a");
            tracker.SendMessage("b");

            TrackingMetricsSnapshot before = tracker.Metrics;
            Assert.AreEqual(2, before.Enqueued);
            Assert.AreEqual(0, before.Sent, "not delivered until flush");

            tracker.Flush();
            Assert.AreEqual(2, tracker.Metrics.Sent);
        }

        [Test]
        public void Metrics_CountDropped_UnderDropOldest()
        {
            TrackingConfig config = new TrackingConfig { MaxQueueCapacity = 2, OverflowPolicy = OverflowPolicy.DropOldest };
            TrackingSystem tracker = NewTracker(out _, config);

            tracker.SendMessage("a");
            tracker.SendMessage("b");
            tracker.SendMessage("c"); // evicts 'a'

            TrackingMetricsSnapshot m = tracker.Metrics;
            Assert.AreEqual(3, m.Enqueued);
            Assert.AreEqual(1, m.Dropped);
        }

        [Test]
        public void Metrics_CountGivenUpAndDeadLettered_OnPermanentFailure()
        {
            TrackingConfig config = new TrackingConfig { MaxRetryAttempts = 2 };
            FlakyTransport transport = new FlakyTransport(failuresBeforeSuccess: TestConstants.NEVER_SUCCEEDS);
            TrackingSystem tracker = new TrackingSystem(
                config, transport,
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo(TestConstants.TEST_PLATFORM, TestConstants.TEST_APP_VERSION),
                startWorker: false,
                delayer: new RecordingDelayer(),
                logger: NullTrackingLogger.Instance);

            Task<bool> task = tracker.SendMapAsync(new Dictionary<string, object> { ["k"] = "v" });
            tracker.Flush();

            Assert.IsFalse(task.GetAwaiter().GetResult());
            TrackingMetricsSnapshot m = tracker.Metrics;
            Assert.AreEqual(1, m.GivenUp);
            Assert.AreEqual(1, m.DeadLettered);
            Assert.AreEqual(1, m.Retried, "2 attempts => 1 retry");
            Assert.AreEqual(1, tracker.DeadLetter.Count);
        }
    }
}
