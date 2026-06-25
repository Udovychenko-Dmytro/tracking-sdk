using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    /// <summary>
    /// Client-side chaos in <see cref="SimulatedHttpTransport"/>: the failure roll honours <c>failPercent</c>,
    /// and the simulated chaos path drives the real retry / dead-letter pipeline offline
    /// (<see cref="ServerEnvironment.FakeServerChaos"/>).
    /// </summary>
    public class SimulatedTransportTests
    {
        private static readonly TrackingEvent[] OneEvent = { TestEvents.Message("x") };

        [Test]
        public void SendAsync_NoChaos_AlwaysSucceeds()
        {
            SimulatedHttpTransport transport = new SimulatedHttpTransport(failPercent: 0, nextRoll: () => 1);
            Assert.IsTrue(transport.SendAsync(OneEvent).GetAwaiter().GetResult());
        }

        // Failure when roll <= failPercent. Boundary (roll == failPercent) fails; one above succeeds.
        [TestCase(20, 20, false)]
        [TestCase(20, 21, true)]
        [TestCase(100, 100, false)]
        [TestCase(0, 1, true)]
        public void SendAsync_HonoursFailPercent(int failPercent, int roll, bool expectedOk)
        {
            SimulatedHttpTransport transport = new SimulatedHttpTransport(failPercent: failPercent, nextRoll: () => roll);
            Assert.AreEqual(expectedOk, transport.SendAsync(OneEvent).GetAwaiter().GetResult());
        }

        [Test]
        public void SimulatedChaos_TransientFailures_RecoverViaRetries()
        {
            // Scripted roll: fail, fail, then succeed (1 <= 20, 1 <= 20, 100 > 20).
            Queue<int> rolls = new Queue<int>(new[] { 1, 1, 100 });
            SimulatedHttpTransport transport = new SimulatedHttpTransport(
                failPercent: TrackingConfig.CHAOS_FAIL_PERCENT, nextRoll: () => rolls.Count > 0 ? rolls.Dequeue() : 100);
            TrackingConfig config = new TrackingConfig { BatchSize = 1, MaxRetryAttempts = 5 };

            TrackingSystem tracker = new TrackingSystem(
                config, transport, startWorker: false, delayer: new RecordingDelayer(), logger: NullTrackingLogger.Instance);
            tracker.SendMessage("recover");
            tracker.FlushAsync().GetAwaiter().GetResult();

            TrackingMetricsSnapshot m = tracker.Metrics;
            Assert.AreEqual(1, m.Sent, "delivered after the transient failures");
            Assert.AreEqual(0, m.GivenUp, "nothing given up");
            Assert.AreEqual(0, tracker.DeadLetter.Count, "nothing dead-lettered");
            tracker.Dispose();
        }

        [Test]
        public void SimulatedChaos_PermanentFailure_RetriesThenDeadLetters()
        {
            // 100% chaos (roll always <= 100) — every send fails, so events exhaust retries and dead-letter.
            SimulatedHttpTransport transport = new SimulatedHttpTransport(failPercent: 100, nextRoll: () => 1);
            TrackingConfig config = new TrackingConfig { BatchSize = 1, MaxRetryAttempts = 3 };

            TrackingSystem tracker = new TrackingSystem(
                config, transport, startWorker: false, delayer: new RecordingDelayer(), logger: NullTrackingLogger.Instance);
            const int N = 5;
            for (int i = 0; i < N; i++)
            {
                tracker.SendMessage("chaos_" + i);
            }
            tracker.FlushAsync().GetAwaiter().GetResult();   // completes — give-up still resolves the awaiter, no hang

            TrackingMetricsSnapshot m = tracker.Metrics;
            Assert.AreEqual(0, m.Sent, "every send fails under 100% chaos");
            Assert.AreEqual(N, m.GivenUp, "all given up after retries");
            Assert.AreEqual(N, tracker.DeadLetter.Count, "all dead-lettered");
            tracker.Dispose();
        }

        [Test]
        public void SendAsync_ChaosDrop_LogsWarning()
        {
            RecordingLogger logger = new RecordingLogger();
            SimulatedHttpTransport transport = new SimulatedHttpTransport(failPercent: 100, nextRoll: () => 1, logger: logger);

            bool ok = transport.SendAsync(OneEvent).GetAwaiter().GetResult();

            Assert.IsFalse(ok, "the injected chaos drop fails the send");
            Assert.IsTrue(
                logger.Entries.Exists(e => e.level == TrackingLogLevel.Warning && e.message.Contains("chaos")),
                "an injected chaos drop logs a Warning so the failure is visible, like HttpTransport on a 503");
        }

        [Test]
        public void SendAsync_NoDrop_IsSilent()
        {
            RecordingLogger logger = new RecordingLogger();
            SimulatedHttpTransport transport = new SimulatedHttpTransport(failPercent: 0, nextRoll: () => 1, logger: logger);

            transport.SendAsync(OneEvent).GetAwaiter().GetResult();

            Assert.AreEqual(0, logger.Entries.Count, "a successful simulated send is silent");
        }

        private sealed class RecordingLogger : ITrackingLogger
        {
            public readonly List<(TrackingLogLevel level, string message)> Entries =
                new List<(TrackingLogLevel, string)>();

            public void Log(TrackingLogLevel level, string message, Exception exception = null)
                => Entries.Add((level, message));
        }
    }
}
