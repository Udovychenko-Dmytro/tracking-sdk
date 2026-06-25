using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class LoggingHookTests
    {
        private sealed class RecordingLogger : ITrackingLogger
        {
            public readonly List<(TrackingLogLevel level, string message)> Entries =
                new List<(TrackingLogLevel, string)>();

            public void Log(TrackingLogLevel level, string message, Exception exception = null)
                => Entries.Add((level, message));
        }

        [Test]
        public void DeadLetter_RoutesAWarning_ThroughTheInjectedLogger()
        {
            RecordingLogger logger = new RecordingLogger();
            TrackingConfig config = new TrackingConfig { MaxRetryAttempts = 1 };
            TrackingSystem tracker = new TrackingSystem(
                config,
                new FlakyTransport(failuresBeforeSuccess: TestConstants.NEVER_SUCCEEDS),
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo(TestConstants.TEST_PLATFORM, TestConstants.TEST_APP_VERSION),
                startWorker: false,
                delayer: new RecordingDelayer(),
                logger: logger);

            tracker.SendMessage("a");
            tracker.Flush();

            Assert.IsTrue(
                logger.Entries.Exists(e => e.level == TrackingLogLevel.Warning && e.message.Contains("dead-letter")),
                "give-up should emit a warning through the logging hook");
        }

        [Test]
        public void Logger_IsNotInvoked_OnTheHappyPath()
        {
            RecordingLogger logger = new RecordingLogger();
            TrackingSystem tracker = new TrackingSystem(
                new TrackingConfig(),
                new RecordingTransport(),
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo(TestConstants.TEST_PLATFORM, TestConstants.TEST_APP_VERSION),
                startWorker: false,
                delayer: new RecordingDelayer(),
                logger: logger);

            tracker.SendMessage("a");
            tracker.Flush();

            Assert.AreEqual(0, logger.Entries.Count, "successful delivery is silent");
        }

        [Test]
        public void MinLogLevel_Info_LogsLifecycleSteps_WithoutPayloadDetail()
        {
            RecordingLogger logger = new RecordingLogger();
            TrackingSystem tracker = new TrackingSystem(
                new TrackingConfig { MinLogLevel = TrackingLogLevel.Info },
                new RecordingTransport(),
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo(TestConstants.TEST_PLATFORM, TestConstants.TEST_APP_VERSION),
                startWorker: false,
                delayer: new RecordingDelayer(),
                logger: logger);

            tracker.SendMessage("hello");
            tracker.Flush();

            Assert.IsTrue(logger.Entries.Exists(e => e.level == TrackingLogLevel.Info && e.message.Contains("initialized")),
                "init step should log at Info");
            Assert.IsTrue(logger.Entries.Exists(e => e.level == TrackingLogLevel.Info && e.message.Contains("enqueued")),
                "enqueue step should log at Info");
            Assert.IsTrue(logger.Entries.Exists(e => e.level == TrackingLogLevel.Info && e.message.Contains("delivered")),
                "delivery step should log at Info");
            Assert.IsFalse(logger.Entries.Exists(e => e.level == TrackingLogLevel.Debug),
                "payload/JSON Debug traces are suppressed at Info");
        }

        [Test]
        public void MinLogLevel_Debug_LogsPayloadContentAndWireJson()
        {
            RecordingLogger logger = new RecordingLogger();
            TrackingSystem tracker = new TrackingSystem(
                new TrackingConfig { MinLogLevel = TrackingLogLevel.Debug },
                new RecordingTransport(),
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo(TestConstants.TEST_PLATFORM, TestConstants.TEST_APP_VERSION),
                startWorker: false,
                delayer: new RecordingDelayer(),
                logger: logger);

            tracker.SendMapAsync(new Dictionary<string, object> { ["sku"] = "coins_500" });
            tracker.Flush();

            Assert.IsTrue(logger.Entries.Exists(e => e.level == TrackingLogLevel.Debug && e.message.Contains("sku")),
                "event payload content should log at Debug");
            Assert.IsTrue(logger.Entries.Exists(e => e.level == TrackingLogLevel.Debug && e.message.Contains("sending")),
                "the serialized wire JSON should log at Debug");
        }
    }
}
