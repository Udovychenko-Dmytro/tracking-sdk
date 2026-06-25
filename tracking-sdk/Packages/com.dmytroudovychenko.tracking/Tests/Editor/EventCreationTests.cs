using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class EventCreationTests
    {
        private static TrackingSystem NewTracker(out RecordingTransport transport)
        {
            transport = new RecordingTransport();
            return new TrackingSystem(
                new TrackingConfig(),
                transport,
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo(TestConstants.TEST_PLATFORM, TestConstants.TEST_APP_VERSION),
                startWorker: false);
        }

        [Test]
        public void SendMapAsync_CreatesMapEvent_PreservingPayloadValues()
        {
            TrackingSystem tracker = NewTracker(out RecordingTransport transport);
            Dictionary<string, object> map = new Dictionary<string, object> { ["event"] = "purchase", ["price"] = 4.99 };

            Task<bool> task = tracker.SendMapAsync(map);
            tracker.Flush();

            Assert.IsTrue(task.GetAwaiter().GetResult());
            TrackingEvent evt = transport.Events[0];
            Assert.AreEqual(TrackingEventType.MAP, evt.Type);
            Assert.AreEqual("purchase", evt.Payload["event"]);
            Assert.AreEqual(4.99, evt.Payload["price"]);
        }

        [Test]
        public void SendMapAsync_SnapshotsPayload_SoLaterCallerMutationDoesNotLeak()
        {
            TrackingSystem tracker = NewTracker(out RecordingTransport transport);
            Dictionary<string, object> map = new Dictionary<string, object> { ["k"] = "original" };

            tracker.SendMapAsync(map);

            // Mutate the caller's dictionary after the call (snapshot was taken synchronously).
            map["k"] = "mutated";
            map["added"] = true;

            tracker.Flush();

            TrackingEvent evt = transport.Events[0];
            Assert.AreEqual("original", evt.Payload["k"], "event keeps its own snapshot");
            Assert.IsFalse(evt.Payload.ContainsKey("added"), "post-call additions must not leak in");
        }

        [Test]
        public void SendMessage_And_SendMapAsync_ProduceDistinctEventIds()
        {
            TrackingSystem tracker = NewTracker(out RecordingTransport transport);

            tracker.SendMessage("hello");
            tracker.SendMapAsync(new Dictionary<string, object> { ["k"] = "v" });
            tracker.Flush();

            Assert.AreEqual(2, transport.Events.Count);
            Assert.AreNotEqual(transport.Events[0].Id, transport.Events[1].Id);
        }

        [Test]
        public void Events_PreserveProducerOrder()
        {
            TrackingSystem tracker = NewTracker(out RecordingTransport transport);

            tracker.SendMessage("first");
            tracker.SendMessage("second");
            tracker.SendMessage("third");
            tracker.Flush();

            Assert.AreEqual("first", transport.Events[0].Payload["message"]);
            Assert.AreEqual("second", transport.Events[1].Payload["message"]);
            Assert.AreEqual("third", transport.Events[2].Payload["message"]);
        }
    }
}
