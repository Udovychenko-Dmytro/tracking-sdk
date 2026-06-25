using NUnit.Framework;
using UnityEngine;

namespace DmytroUdovychenko.Tracking.Tests
{
    /// <summary>
    /// Live retry/resend test against the deployed receiver in chaos mode (<c>?fail=20</c> → ~20%
    /// transient 503s). Drives a full <see cref="TrackingSystem"/> over the real <see cref="HttpTransport"/>
    /// and asserts every event is still delivered — the dispatcher transparently retries. Runs in the
    /// default suite, so it needs network.
    /// </summary>
    public class LiveRetryTests
    {
        [Test, Category("Live")]
        public void Retries_RecoverFrom_A_FlakyServer()
        {
            // BatchSize 1 => one HTTP request per event (so ~20% of them hit a 503 and must retry).
            TrackingConfig config = new TrackingConfig { BatchSize = 1, MaxRetryAttempts = 12 };

            using (HttpTransport transport = new HttpTransport(TestConstants.CHAOS_ENDPOINT, logger: NullTrackingLogger.Instance))
            {
                TrackingSystem tracker = new TrackingSystem(
                    config,
                    transport,
                    startWorker: false,
                    delayer: new RecordingDelayer(),       // retry immediately — no real back-off wait
                    logger: NullTrackingLogger.Instance);

                const int N = 40;
                for (int i = 0; i < N; i++)
                {
                    tracker.SendMessage("retry_live_" + i);
                }

                tracker.FlushAsync().GetAwaiter().GetResult();

                TrackingMetricsSnapshot m = tracker.Metrics;
                Debug.Log($"[live-retry] {m}");   // shows the 'retried' count produced by the 503s

                Assert.AreEqual(N, m.Sent, "every event delivered despite ~20% transient failures");
                Assert.AreEqual(0, m.GivenUp, "nothing given up after retries");
                Assert.AreEqual(0, tracker.DeadLetter.Count, "nothing dead-lettered");

                tracker.Dispose();
            }
        }
    }
}
