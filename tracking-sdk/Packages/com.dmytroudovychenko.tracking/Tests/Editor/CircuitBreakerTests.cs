using System;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class CircuitBreakerTests
    {
        [Test]
        public void Opens_AfterThresholdConsecutiveFailures()
        {
            FakeClock clock = new FakeClock(TestEvents.T0);
            CircuitBreaker cb = new CircuitBreaker(failureThreshold: 3, openDuration: TimeSpan.FromSeconds(30), clock);

            Assert.IsTrue(cb.AllowRequest());
            cb.RecordFailure();
            cb.RecordFailure();
            Assert.IsTrue(cb.AllowRequest(), "still closed below threshold");

            cb.RecordFailure(); // third
            Assert.IsFalse(cb.AllowRequest(), "opens at the threshold");
            Assert.AreEqual(CircuitState.Open, cb.State);
        }

        [Test]
        public void HalfOpens_AfterCooldown_AndClosesOnSuccess()
        {
            FakeClock clock = new FakeClock(TestEvents.T0);
            CircuitBreaker cb = new CircuitBreaker(2, TimeSpan.FromSeconds(30), clock);
            cb.RecordFailure();
            cb.RecordFailure(); // open
            Assert.IsFalse(cb.AllowRequest());

            clock.Advance(TimeSpan.FromSeconds(30));
            Assert.IsTrue(cb.AllowRequest(), "half-opens after the cooldown");
            Assert.AreEqual(CircuitState.HalfOpen, cb.State);

            cb.RecordSuccess();
            Assert.AreEqual(CircuitState.Closed, cb.State, "a successful trial closes it");
        }

        [Test]
        public void HalfOpen_Failure_ReopensImmediately()
        {
            FakeClock clock = new FakeClock(TestEvents.T0);
            CircuitBreaker cb = new CircuitBreaker(1, TimeSpan.FromSeconds(10), clock);
            cb.RecordFailure(); // open (threshold 1)

            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.AreEqual(CircuitState.HalfOpen, cb.State);

            cb.RecordFailure();
            Assert.AreEqual(CircuitState.Open, cb.State);
            Assert.IsFalse(cb.AllowRequest());
        }

        [Test]
        public void Open_StopsDispatcher_FromAttemptingFurtherBatches()
        {
            FakeClock clock = new FakeClock(TestEvents.T0);
            FlakyTransport transport = new FlakyTransport(failuresBeforeSuccess: TestConstants.NEVER_SUCCEEDS); // permanently down
            CircuitBreaker breaker = new CircuitBreaker(failureThreshold: 2, openDuration: TimeSpan.FromSeconds(30), clock);
            TrackingConfig config = new TrackingConfig { BatchSize = 1, MaxRetryAttempts = 1 };
            EventQueue queue = new EventQueue(TestConstants.DEFAULT_QUEUE_CAPACITY, config.OverflowPolicy);
            EventDispatcher dispatcher = new EventDispatcher(
                queue, transport, clock, config,
                retryPolicy: new RetryPolicy(1, TimeSpan.Zero, TimeSpan.Zero),
                delayer: new RecordingDelayer(),
                breaker: breaker);

            for (int i = 0; i < 5; i++)
            {
                queue.TryEnqueue(new QueuedEvent { Event = TestEvents.Message("m" + i) }, out _);
            }

            dispatcher.DrainAsync().GetAwaiter().GetResult();

            Assert.AreEqual(2, transport.SendCount, "two batch failures open the breaker, then it holds the rest");
            Assert.AreEqual(CircuitState.Open, breaker.State);
            Assert.AreEqual(3, queue.Count, "remaining events stay queued for a later attempt");
        }

        [Test]
        public void HalfOpen_ProbesWithASingleAttempt_NotTheFullRetryBudget()
        {
            FakeClock clock = new FakeClock(TestEvents.T0);
            FlakyTransport transport = new FlakyTransport(failuresBeforeSuccess: TestConstants.NEVER_SUCCEEDS); // permanently down
            CircuitBreaker breaker = new CircuitBreaker(failureThreshold: 1, openDuration: TimeSpan.FromSeconds(30), clock);
            breaker.RecordFailure();                 // opens (threshold 1)
            clock.Advance(TimeSpan.FromSeconds(30)); // cooldown elapsed -> the next AllowRequest half-opens

            TrackingConfig config = new TrackingConfig { BatchSize = 10, MaxRetryAttempts = 5 };
            EventQueue queue = new EventQueue(TestConstants.DEFAULT_QUEUE_CAPACITY, config.OverflowPolicy);
            EventDispatcher dispatcher = new EventDispatcher(
                queue, transport, clock, config,
                retryPolicy: new RetryPolicy(5, TimeSpan.Zero, TimeSpan.Zero),
                delayer: new RecordingDelayer(),
                breaker: breaker,
                deadLetter: new InMemoryDeadLetterQueue(TestConstants.DEAD_LETTER_CAPACITY),
                logger: NullTrackingLogger.Instance);

            queue.TryEnqueue(new QueuedEvent { Event = TestEvents.Message("probe") }, out _);
            dispatcher.DrainAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, transport.SendCount, "half-open probes once, not MaxRetryAttempts times");
            Assert.AreEqual(CircuitState.Open, breaker.State, "the failed probe re-opens the breaker");
        }
    }
}
