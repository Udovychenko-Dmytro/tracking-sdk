using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class PrivacyTests
    {
        private static TrackingSystem NewTracker(out RecordingTransport transport, IEventStore store = null)
        {
            transport = new RecordingTransport();
            return new TrackingSystem(
                new TrackingConfig(),
                transport,
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo(TestConstants.TEST_PLATFORM, TestConstants.TEST_APP_VERSION),
                startWorker: false,
                delayer: new RecordingDelayer(),
                store: store ?? new InMemoryEventStore(),
                logger: NullTrackingLogger.Instance);
        }

        [Test]
        public void SetEnabledFalse_RejectsSubsequentSends()
        {
            TrackingSystem tracker = NewTracker(out _);

            tracker.SetEnabled(false);

            Assert.IsFalse(tracker.IsEnabled);
            Assert.IsFalse(tracker.SendMessage("a"));
            Assert.IsFalse(tracker.SendMapAsync(new Dictionary<string, object> { ["k"] = "v" }).GetAwaiter().GetResult());
        }

        [Test]
        public void SetEnabledFalse_PurgesPendingQueue_AndFailsAwaiters()
        {
            TrackingSystem tracker = NewTracker(out RecordingTransport transport);

            tracker.SendMessage("a");
            Task<bool> pending = tracker.SendMapAsync(new Dictionary<string, object> { ["k"] = "v" });

            tracker.SetEnabled(false); // purges buffered data

            Assert.IsTrue(pending.IsCompleted);
            Assert.IsFalse(pending.GetAwaiter().GetResult(), "purged awaiter resolves false");

            tracker.SetEnabled(true);
            tracker.Flush();
            Assert.AreEqual(0, transport.Events.Count, "purged events are gone");
        }

        [Test]
        public void Purge_ClearsQueueDeadLetterAndStore()
        {
            InMemoryEventStore store = new InMemoryEventStore();
            TrackingSystem tracker = NewTracker(out RecordingTransport transport, store);

            tracker.SendMessage("a");
            tracker.Persist();
            Assert.AreEqual(1, store.Load().Count);

            tracker.Purge();

            Assert.AreEqual(0, store.Load().Count, "persisted backlog cleared");
            Assert.AreEqual(0, tracker.DeadLetter.Count);
            tracker.Flush();
            Assert.AreEqual(0, transport.Events.Count, "queue cleared");
        }

        [Test]
        public void ReEnabling_AllowsSendingAgain()
        {
            TrackingSystem tracker = NewTracker(out RecordingTransport transport);

            tracker.SetEnabled(false);
            tracker.SetEnabled(true);

            Assert.IsTrue(tracker.SendMessage("hello"));
            tracker.Flush();
            Assert.AreEqual(1, transport.Events.Count);
        }

        // ---- BLI-006 anonymous (privacy) mode ----

        private static TrackingSystem NewTrackerWithUser(string userId, out RecordingTransport transport, bool privacyMode = false)
        {
            transport = new RecordingTransport();
            return new TrackingSystem(
                new TrackingConfig { UserId = userId, PrivacyMode = privacyMode },
                transport,
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo(TestConstants.TEST_PLATFORM, TestConstants.TEST_APP_VERSION),
                startWorker: false,
                delayer: new RecordingDelayer(),
                store: new InMemoryEventStore(),
                logger: NullTrackingLogger.Instance);
        }

        [Test]
        public void PrivacyModeOff_StampsRealUserId()
        {
            TrackingSystem tracker = NewTrackerWithUser("player-42", out RecordingTransport transport);

            Assert.IsFalse(tracker.IsPrivacyMode);
            tracker.SendMessage("a");
            tracker.Flush();

            Assert.AreEqual("player-42", transport.Events[0].UserId);
        }

        [Test]
        public void SetPrivacyModeOn_StampsAnonymous_KeepsSessionAndContext()
        {
            TrackingSystem tracker = NewTrackerWithUser("player-42", out RecordingTransport transport);

            tracker.SetPrivacyMode(true);
            Assert.IsTrue(tracker.IsPrivacyMode);

            tracker.SendMessage("a");
            tracker.Flush();

            TrackingEvent e = transport.Events[0];
            Assert.AreEqual("anonymous", e.UserId, "user identity dropped");
            Assert.AreEqual(tracker.SessionId, e.SessionId, "sessionId kept (per-session, not a person)");
            Assert.AreEqual(TestConstants.TEST_DEVICE_MODEL, e.DeviceModel, "device context kept");
            Assert.IsFalse(string.IsNullOrEmpty(e.Id), "event id kept");
        }

        [Test]
        public void PrivacyModeConfigDefaultOn_StampsAnonymous()
        {
            TrackingSystem tracker = NewTrackerWithUser("player-42", out RecordingTransport transport, privacyMode: true);

            Assert.IsTrue(tracker.IsPrivacyMode);
            tracker.SendMessage("a");
            tracker.Flush();

            Assert.AreEqual("anonymous", transport.Events[0].UserId);
        }

        [Test]
        public void SetPrivacyModeOff_RestoresRealUserId()
        {
            TrackingSystem tracker = NewTrackerWithUser("player-42", out RecordingTransport transport, privacyMode: true);

            tracker.SetPrivacyMode(false);
            tracker.SendMessage("a");
            tracker.Flush();

            Assert.AreEqual("player-42", transport.Events[0].UserId);
        }

        [Test]
        public void SetPrivacyModeOn_DoesNotRetroactivelyAnonymizeBufferedEvents()
        {
            // Forward-only (BLI-006 decision e): flipping privacy ON only affects events built afterwards.
            TrackingSystem tracker = NewTrackerWithUser("player-42", out RecordingTransport transport);

            tracker.SendMessage("before"); // buffered while still identified
            tracker.SetPrivacyMode(true);  // flip AFTER the first event is enqueued
            tracker.SendMessage("after");  // built under privacy mode
            tracker.Flush();

            Assert.AreEqual("player-42", transport.Events[0].UserId, "already-buffered event keeps its identity");
            Assert.AreEqual("anonymous", transport.Events[1].UserId, "new event is anonymized");
        }
    }
}
