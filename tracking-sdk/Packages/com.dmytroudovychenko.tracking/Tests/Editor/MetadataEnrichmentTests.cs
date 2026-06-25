using System;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class MetadataEnrichmentTests
    {
        private static readonly DateTimeOffset FixedNow =
            new DateTimeOffset(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

        private static TrackingSystem NewTracker(out RecordingTransport transport)
        {
            transport = new RecordingTransport();
            return new TrackingSystem(
                new TrackingConfig(),
                transport,
                new FakeClock(FixedNow),
                new FakeRuntimeInfo("TestPlatform", "9.9.9"),
                startWorker: false);
        }

        [Test]
        public void SendMessage_EnrichesEvent_WithFullMetadataEnvelope()
        {
            TrackingSystem tracker = NewTracker(out RecordingTransport transport);

            Assert.IsTrue(tracker.SendMessage("hello"));
            tracker.Flush();

            TrackingEvent evt = transport.Events[0];
            Assert.AreEqual(TrackingEventType.MESSAGE, evt.Type, "type");
            Assert.AreEqual("hello", evt.Payload["message"], "payload");
            Assert.AreEqual(FixedNow, evt.TimestampUtc, "timestamp comes from the injected clock");
            Assert.AreEqual(TrackingSdk.VERSION, evt.SdkVersion, "sdk version");
            Assert.AreEqual("TestPlatform", evt.Platform, "platform");
            Assert.AreEqual("9.9.9", evt.AppVersion, "app version");
            Assert.IsFalse(string.IsNullOrEmpty(evt.Id), "id is set");
            Assert.IsFalse(string.IsNullOrEmpty(evt.SessionId), "session id is set");
        }

        [Test]
        public void SendMessage_EnrichesEvent_WithDeviceContext()
        {
            TrackingSystem tracker = NewTracker(out RecordingTransport transport);

            Assert.IsTrue(tracker.SendMessage("hello"));
            tracker.Flush();

            TrackingEvent evt = transport.Events[0];
            Assert.AreEqual(TestConstants.TEST_DEVICE_MODEL, evt.DeviceModel, "device model");
            Assert.AreEqual(TestConstants.TEST_OS_VERSION, evt.OsVersion, "os version");
            Assert.AreEqual(TestConstants.TEST_NETWORK_TYPE, evt.NetworkType, "network type");
            Assert.AreEqual(TestConstants.TEST_TIMEZONE, evt.Timezone, "timezone");
            Assert.AreEqual(TestConstants.TEST_LOCALE, evt.Locale, "locale");
            Assert.AreEqual(TestConstants.TEST_BUNDLE_ID, evt.BundleId, "bundle id");
        }

        [Test]
        public void SendMessage_StampsConfiguredUserId_OnEvent()
        {
            RecordingTransport transport = new RecordingTransport();
            TrackingSystem tracker = new TrackingSystem(
                new TrackingConfig { UserId = "user-42" },
                transport,
                new FakeClock(FixedNow),
                new FakeRuntimeInfo("TestPlatform", "9.9.9"),
                startWorker: false);

            Assert.IsTrue(tracker.SendMessage("hello"));
            tracker.Flush();

            Assert.AreEqual("user-42", transport.Events[0].UserId, "user id is stamped from config");
            Assert.AreEqual("user-42", tracker.UserId, "tracker exposes its user id");
        }

        [Test]
        public void AllEvents_FromSameTracker_ShareSessionId_ButHaveUniqueIds()
        {
            TrackingSystem tracker = NewTracker(out RecordingTransport transport);

            tracker.SendMessage("a");
            tracker.SendMessage("b");
            tracker.Flush();

            TrackingEvent first = transport.Events[0];
            TrackingEvent second = transport.Events[1];
            Assert.AreEqual(first.SessionId, second.SessionId, "session id is stable for a tracker instance");
            Assert.AreEqual(tracker.SessionId, first.SessionId, "tracker exposes its own session id");
            Assert.AreNotEqual(first.Id, second.Id, "each event has a unique idempotency id");
        }

        [Test]
        public void DifferentTrackers_HaveDifferentSessionIds()
        {
            TrackingSystem trackerA = NewTracker(out RecordingTransport transportA);
            TrackingSystem trackerB = NewTracker(out RecordingTransport transportB);

            trackerA.SendMessage("a");
            trackerB.SendMessage("b");
            trackerA.Flush();
            trackerB.Flush();

            Assert.AreNotEqual(
                transportA.Events[0].SessionId,
                transportB.Events[0].SessionId,
                "separate tracker instances start separate sessions");
        }
    }
}
