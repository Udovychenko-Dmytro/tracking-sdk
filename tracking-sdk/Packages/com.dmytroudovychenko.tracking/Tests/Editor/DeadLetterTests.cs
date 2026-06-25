using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class DeadLetterTests
    {
        [Test]
        public void GivenUpEvents_AreMovedToTheDeadLetterSink()
        {
            InMemoryDeadLetterQueue deadLetter = new InMemoryDeadLetterQueue(TestConstants.DEAD_LETTER_CAPACITY);
            FakeClock clock = new FakeClock(TestEvents.T0);
            TrackingConfig config = new TrackingConfig { BatchSize = 10, MaxRetryAttempts = 1 };
            EventQueue queue = new EventQueue(TestConstants.DEFAULT_QUEUE_CAPACITY, config.OverflowPolicy);
            EventDispatcher dispatcher = new EventDispatcher(
                queue, new FlakyTransport(failuresBeforeSuccess: TestConstants.NEVER_SUCCEEDS), clock, config,
                retryPolicy: new RetryPolicy(1, TimeSpan.Zero, TimeSpan.Zero),
                delayer: new RecordingDelayer(),
                deadLetter: deadLetter);

            queue.TryEnqueue(new QueuedEvent { Event = TestEvents.Message("a") }, out _);
            queue.TryEnqueue(new QueuedEvent { Event = TestEvents.Message("b") }, out _);

            dispatcher.DrainAsync().GetAwaiter().GetResult();

            Assert.AreEqual(2, deadLetter.Count);
            IReadOnlyList<TrackingEvent> dl = deadLetter.Snapshot();
            Assert.AreEqual("a", dl[0].Payload["message"]);
            Assert.AreEqual("b", dl[1].Payload["message"]);
        }

        [Test]
        public void DeadLetterQueue_IsBounded_DroppingOldest()
        {
            InMemoryDeadLetterQueue dlq = new InMemoryDeadLetterQueue(capacity: 2);

            dlq.DeadLetter(new[] { TestEvents.Message("a") });
            dlq.DeadLetter(new[] { TestEvents.Message("b") });
            dlq.DeadLetter(new[] { TestEvents.Message("c") });

            Assert.AreEqual(2, dlq.Count);
            IReadOnlyList<TrackingEvent> s = dlq.Snapshot();
            Assert.AreEqual("b", s[0].Payload["message"]);
            Assert.AreEqual("c", s[1].Payload["message"]);
        }

        [Test]
        public void Clear_EmptiesTheDeadLetterQueue()
        {
            InMemoryDeadLetterQueue dlq = new InMemoryDeadLetterQueue();
            dlq.DeadLetter(new[] { TestEvents.Message("a") });
            Assert.AreEqual(1, dlq.Count);

            dlq.Clear();
            Assert.AreEqual(0, dlq.Count);
        }

        [Test]
        public void Batch_AwaitersComplete_EvenWhenADeadLetterSinkThrows()
        {
            FakeClock clock = new FakeClock(TestEvents.T0);
            TrackingConfig config = new TrackingConfig { BatchSize = 10, MaxRetryAttempts = 1 };
            EventQueue queue = new EventQueue(TestConstants.DEFAULT_QUEUE_CAPACITY, config.OverflowPolicy);
            EventDispatcher dispatcher = new EventDispatcher(
                queue, new FlakyTransport(failuresBeforeSuccess: TestConstants.NEVER_SUCCEEDS), clock, config,
                retryPolicy: new RetryPolicy(1, TimeSpan.Zero, TimeSpan.Zero),
                delayer: new RecordingDelayer(),
                deadLetter: new ThrowingDeadLetterSink(),
                logger: NullTrackingLogger.Instance);

            TaskCompletionSource<bool> awaiter = new TaskCompletionSource<bool>();
            queue.TryEnqueue(new QueuedEvent { Event = TestEvents.Message("x"), Completion = awaiter }, out _);

            Assert.DoesNotThrow(() => dispatcher.DrainAsync().GetAwaiter().GetResult());
            Assert.IsTrue(awaiter.Task.IsCompleted, "the awaiter completes even when the sink throws");
            Assert.IsFalse(awaiter.Task.GetAwaiter().GetResult(), "give-up resolves false");
        }

        private sealed class ThrowingDeadLetterSink : IDeadLetterSink
        {
            public void DeadLetter(IReadOnlyList<TrackingEvent> events) => throw new InvalidOperationException("boom");
            public IReadOnlyList<TrackingEvent> Snapshot() => Array.Empty<TrackingEvent>();
            public int Count => 0;
            public void Clear() { }
        }
    }
}
