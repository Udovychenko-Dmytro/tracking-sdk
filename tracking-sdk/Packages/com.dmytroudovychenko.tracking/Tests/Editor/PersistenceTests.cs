using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class PersistenceTests
    {
        private static TrackingSystem NewTracker(IEventStore store, out RecordingTransport transport)
        {
            transport = new RecordingTransport();
            return new TrackingSystem(
                new TrackingConfig(),
                transport,
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo(TestConstants.TEST_PLATFORM, TestConstants.TEST_APP_VERSION),
                startWorker: false,
                delayer: new RecordingDelayer(),
                store: store);
        }

        [Test]
        public void UnsentEvents_SurviveRestart_AndAreResent_InOrder()
        {
            InMemoryEventStore store = new InMemoryEventStore();

            // Session 1: buffer 3 events, persist (as on pause/quit) — but never deliver them.
            TrackingSystem session1 = NewTracker(store, out RecordingTransport transport1);
            session1.SendMessage("a");
            session1.SendMessage("b");
            session1.SendMessage("c");
            session1.Persist();
            Assert.AreEqual(0, transport1.Events.Count, "session 1 delivered nothing");

            // Session 2: a fresh tracker over the same store reloads the backlog and delivers it.
            TrackingSystem session2 = NewTracker(store, out RecordingTransport transport2);
            session2.Flush();

            Assert.AreEqual(3, transport2.Events.Count);
            Assert.AreEqual("a", transport2.Events[0].Payload["message"]);
            Assert.AreEqual("b", transport2.Events[1].Payload["message"]);
            Assert.AreEqual("c", transport2.Events[2].Payload["message"]);
        }

        [Test]
        public void Persist_SnapshotsQueue_WithoutRemovingEvents()
        {
            InMemoryEventStore store = new InMemoryEventStore();
            TrackingSystem tracker = NewTracker(store, out RecordingTransport transport);

            tracker.SendMessage("x");
            tracker.SendMessage("y");
            tracker.Persist();

            Assert.AreEqual(2, store.Load().Count, "snapshot written to the store");

            // The events remain buffered and still get delivered.
            tracker.Flush();
            Assert.AreEqual(2, transport.Events.Count);
        }

        [Test]
        public void DeliveredEvents_AreNotResent_WhenPersistReflectsEmptyQueue()
        {
            InMemoryEventStore store = new InMemoryEventStore();

            TrackingSystem session1 = NewTracker(store, out RecordingTransport transport1);
            session1.SendMessage("a");
            session1.Flush();                       // delivered => queue empty
            Assert.AreEqual(1, transport1.Events.Count);
            session1.Persist();                     // snapshot of an empty queue clears the backlog
            Assert.AreEqual(0, store.Load().Count);

            TrackingSystem session2 = NewTracker(store, out RecordingTransport transport2);
            session2.Flush();
            Assert.AreEqual(0, transport2.Events.Count, "nothing left to resend");
        }
    }
}
