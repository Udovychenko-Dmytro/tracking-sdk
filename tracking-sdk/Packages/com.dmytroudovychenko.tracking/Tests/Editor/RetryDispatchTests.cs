using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class RetryDispatchTests
    {
        private static (EventDispatcher dispatcher, EventQueue queue) NewDispatcher(
            ITransport transport, IDelayer delayer, int maxAttempts, Func<double> jitter = null)
        {
            FakeClock clock = new FakeClock(TestEvents.T0);
            TrackingConfig config = new TrackingConfig
            {
                BatchSize = 10,
                MaxRetryAttempts = maxAttempts,
                InitialRetryDelay = TimeSpan.FromMilliseconds(100),
                MaxRetryDelay = TimeSpan.FromSeconds(10),
            };
            EventQueue queue = new EventQueue(TestConstants.DEFAULT_QUEUE_CAPACITY, config.OverflowPolicy);
            RetryPolicy retry = new RetryPolicy(maxAttempts, config.InitialRetryDelay, config.MaxRetryDelay, jitter);
            EventDispatcher dispatcher = new EventDispatcher(queue, transport, clock, config, retry, delayer);
            return (dispatcher, queue);
        }

        private static TaskCompletionSource<bool> EnqueueAwaitable(EventQueue queue)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.TryEnqueue(new QueuedEvent { Event = TestEvents.Message("e"), Completion = tcs }, out _);
            return tcs;
        }

        [Test]
        public void Delivery_Succeeds_AfterTransientFailures()
        {
            FlakyTransport transport = new FlakyTransport(failuresBeforeSuccess: 2);
            RecordingDelayer delayer = new RecordingDelayer();
            (EventDispatcher dispatcher, EventQueue queue) = NewDispatcher(transport, delayer, maxAttempts: 5);
            TaskCompletionSource<bool> tcs = EnqueueAwaitable(queue);

            dispatcher.DrainAsync().GetAwaiter().GetResult();

            Assert.AreEqual(3, transport.SendCount, "2 failures + 1 success");
            Assert.IsTrue(tcs.Task.GetAwaiter().GetResult(), "delivered => true");
            Assert.AreEqual(2, delayer.Delays.Count, "one backoff before each retry");
        }

        [Test]
        public void Delivery_GivesUp_AfterMaxAttempts()
        {
            FlakyTransport transport = new FlakyTransport(failuresBeforeSuccess: TestConstants.NEVER_SUCCEEDS); // permanently down
            RecordingDelayer delayer = new RecordingDelayer();
            (EventDispatcher dispatcher, EventQueue queue) = NewDispatcher(transport, delayer, maxAttempts: 4);
            TaskCompletionSource<bool> tcs = EnqueueAwaitable(queue);

            dispatcher.DrainAsync().GetAwaiter().GetResult();

            Assert.AreEqual(4, transport.SendCount, "exactly maxAttempts tries");
            Assert.AreEqual(3, delayer.Delays.Count, "delays only sit between attempts");
            Assert.IsFalse(tcs.Task.GetAwaiter().GetResult(), "give up => false");
        }

        [Test]
        public void Backoff_GrowsExponentially_BetweenRetries()
        {
            FlakyTransport transport = new FlakyTransport(failuresBeforeSuccess: TestConstants.NEVER_SUCCEEDS);
            RecordingDelayer delayer = new RecordingDelayer();
            // jitter=0 => delay == base/2; initial 100ms => bases 100,200,400 => delays 50,100,200.
            (EventDispatcher dispatcher, EventQueue queue) = NewDispatcher(transport, delayer, maxAttempts: 4, jitter: () => 0.0);
            TaskCompletionSource<bool> tcs = EnqueueAwaitable(queue);

            dispatcher.DrainAsync().GetAwaiter().GetResult();

            Assert.AreEqual(3, delayer.Delays.Count);
            Assert.AreEqual(50, delayer.Delays[0].TotalMilliseconds, 0.001);
            Assert.AreEqual(100, delayer.Delays[1].TotalMilliseconds, 0.001);
            Assert.AreEqual(200, delayer.Delays[2].TotalMilliseconds, 0.001);
            Assert.IsFalse(tcs.Task.GetAwaiter().GetResult());
        }

        [Test]
        public void Retry_StopsImmediately_WhenCancelledDuringBackoff()
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            FlakyTransport transport = new FlakyTransport(failuresBeforeSuccess: TestConstants.NEVER_SUCCEEDS);
            CancelOnFirstDelay delayer = new CancelOnFirstDelay(cts);
            (EventDispatcher dispatcher, EventQueue queue) = NewDispatcher(transport, delayer, maxAttempts: 5);
            TaskCompletionSource<bool> tcs = EnqueueAwaitable(queue);

            dispatcher.DrainAsync(cts.Token).GetAwaiter().GetResult();

            Assert.AreEqual(1, transport.SendCount, "one attempt, then cancelled during the first backoff");
            Assert.IsFalse(tcs.Task.GetAwaiter().GetResult(), "cancelled => not delivered");
        }
    }
}
