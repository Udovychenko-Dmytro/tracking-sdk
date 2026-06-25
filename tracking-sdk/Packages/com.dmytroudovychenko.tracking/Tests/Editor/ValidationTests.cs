using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class ValidationTests
    {
        private sealed class RecordingLogger : ITrackingLogger
        {
            public readonly List<(TrackingLogLevel level, string message)> Entries =
                new List<(TrackingLogLevel, string)>();

            public void Log(TrackingLogLevel level, string message, Exception exception = null)
                => Entries.Add((level, message));
        }

        private static TrackingSystem NewTracker(out RecordingTransport transport, bool enabled = true,
            ITrackingLogger logger = null)
        {
            transport = new RecordingTransport();
            TrackingConfig config = new TrackingConfig { Enabled = enabled };
            return new TrackingSystem(
                config,
                transport,
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo("TestPlatform", "1.2.3"),
                startWorker: false,
                logger: logger);
        }

        [Test]
        public void SendMessage_NullEmptyOrWhitespace_ReturnsFalse_AndRecordsNothing()
        {
            TrackingSystem tracker = NewTracker(out RecordingTransport transport,
                logger: NullTrackingLogger.Instance);

            Assert.IsFalse(tracker.SendMessage(null), "null message");
            Assert.IsFalse(tracker.SendMessage(string.Empty), "empty message");
            Assert.IsFalse(tracker.SendMessage("   "), "whitespace message");

            tracker.Flush();
            Assert.AreEqual(0, transport.Events.Count);
        }

        [Test]
        public void SendMessage_ValidMessage_ReturnsTrue_AndRecordsExactlyOneEvent()
        {
            TrackingSystem tracker = NewTracker(out RecordingTransport transport);

            Assert.IsTrue(tracker.SendMessage("hello"));

            tracker.Flush();
            Assert.AreEqual(1, transport.Events.Count);
        }

        [Test]
        public void SendMessage_WhenTrackingDisabled_ReturnsFalse_AndRecordsNothing()
        {
            TrackingSystem tracker = NewTracker(out RecordingTransport transport, enabled: false);

            Assert.IsFalse(tracker.SendMessage("hello"));

            tracker.Flush();
            Assert.AreEqual(0, transport.Events.Count);
        }

        [Test]
        public void SendMapAsync_NullOrEmptyMap_ReturnsFalse_AndRecordsNothing()
        {
            TrackingSystem tracker = NewTracker(out RecordingTransport transport,
                logger: NullTrackingLogger.Instance);

            // Invalid input short-circuits to an already-completed task (no enqueue, no deadlock).
            Assert.IsFalse(tracker.SendMapAsync(null).GetAwaiter().GetResult(), "null map");
            Assert.IsFalse(tracker.SendMapAsync(new Dictionary<string, object>()).GetAwaiter().GetResult(), "empty map");

            tracker.Flush();
            Assert.AreEqual(0, transport.Events.Count);
        }

        [Test]
        public void SendMapAsync_ValidMap_ReturnsTrue_AndRecordsExactlyOneEvent()
        {
            TrackingSystem tracker = NewTracker(out RecordingTransport transport);

            Task<bool> task = tracker.SendMapAsync(new Dictionary<string, object> { ["k"] = "v" });
            tracker.Flush();

            Assert.IsTrue(task.GetAwaiter().GetResult());
            Assert.AreEqual(1, transport.Events.Count);
        }

        [Test]
        public void SendMapAsync_WhenTrackingDisabled_ReturnsFalse_AndRecordsNothing()
        {
            TrackingSystem tracker = NewTracker(out RecordingTransport transport, enabled: false);

            Assert.IsFalse(tracker.SendMapAsync(new Dictionary<string, object> { ["k"] = "v" }).GetAwaiter().GetResult());

            tracker.Flush();
            Assert.AreEqual(0, transport.Events.Count);
        }

        [Test]
        public void SendMessage_EmptyMessage_LogsError()
        {
            RecordingLogger logger = new RecordingLogger();
            TrackingSystem tracker = NewTracker(out RecordingTransport transport, logger: logger);

            tracker.SendMessage(null);
            tracker.SendMessage("");
            tracker.SendMessage("   ");

            Assert.AreEqual(3, logger.Entries.Count(e => e.level == TrackingLogLevel.Error),
                "each empty-message variant should log an Error");
        }

        [Test]
        public void SendMapAsync_EmptyMap_LogsError()
        {
            RecordingLogger logger = new RecordingLogger();
            TrackingSystem tracker = NewTracker(out RecordingTransport transport, logger: logger);

            tracker.SendMapAsync(null).GetAwaiter().GetResult();
            tracker.SendMapAsync(new Dictionary<string, object>()).GetAwaiter().GetResult();

            Assert.AreEqual(2, logger.Entries.Count(e => e.level == TrackingLogLevel.Error),
                "null and empty map should each log an Error");
        }

        [Test]
        public void SendMapAsync_FiltersInvalidEntries_LogsWarningPerDrop()
        {
            RecordingLogger logger = new RecordingLogger();
            TrackingSystem tracker = NewTracker(out RecordingTransport transport, logger: logger);

            Dictionary<string, object> map = new Dictionary<string, object>
            {
                ["good"] = "value",
                [""] = "orphan-value",
                ["nullValue"] = null
            };

            Task<bool> task = tracker.SendMapAsync(map);
            tracker.Flush();

            Assert.IsTrue(task.GetAwaiter().GetResult(), "valid entry remains — should accept");
            Assert.AreEqual(1, transport.Events.Count, "one event with filtered map");

            IReadOnlyDictionary<string, object> payload = transport.Events[0].Payload;
            Assert.IsTrue(payload.ContainsKey("good"), "valid entry kept");
            Assert.IsFalse(payload.ContainsKey(""), "empty-key entry dropped");
            Assert.IsFalse(payload.ContainsKey("nullValue"), "null-value entry dropped");

            Assert.AreEqual(2, logger.Entries.Count(e => e.level == TrackingLogLevel.Warning),
                "one Warning per invalid entry");
        }

        [Test]
        public void SendMapAsync_AllEntriesInvalid_ReturnsFalse_LogsError()
        {
            RecordingLogger logger = new RecordingLogger();
            TrackingSystem tracker = NewTracker(out RecordingTransport transport, logger: logger);

            Dictionary<string, object> map = new Dictionary<string, object>
            {
                [""] = "orphan",
                ["  "] = "whitespace-key",
                ["nullVal"] = null
            };

            bool result = tracker.SendMapAsync(map).GetAwaiter().GetResult();

            Assert.IsFalse(result, "no valid entries — should reject");
            tracker.Flush();
            Assert.AreEqual(0, transport.Events.Count, "nothing enqueued");

            Assert.IsTrue(
                logger.Entries.Any(e => e.level == TrackingLogLevel.Error && e.message.Contains("nothing to send")),
                "should log Error for empty-after-filter");
        }
    }
}
