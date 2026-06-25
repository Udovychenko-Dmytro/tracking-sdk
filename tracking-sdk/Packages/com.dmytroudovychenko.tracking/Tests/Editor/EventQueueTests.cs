using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class EventQueueTests
    {
        private static QueuedEvent Q(string message) => new QueuedEvent { Event = TestEvents.Message(message) };

        [Test]
        public void DropOldest_EvictsOldest_WhenOverCapacity()
        {
            EventQueue q = new EventQueue(capacity: 3, policy: OverflowPolicy.DropOldest);

            for (int i = 0; i < 5; i++)
            {
                Assert.IsTrue(q.TryEnqueue(Q("m" + i), out _), "DropOldest always accepts the producer");
            }

            Assert.AreEqual(3, q.Count, "capacity is enforced");
            Assert.AreEqual(2, q.DroppedCount, "two oldest events dropped");

            List<QueuedEvent> batch = q.DequeueBatch(10);
            CollectionAssert.AreEqual(
                new[] { "m2", "m3", "m4" },
                batch.ConvertAll(b => (string)b.Event.Payload["message"]),
                "the three newest survive, in order");
        }

        [Test]
        public void RejectNew_RejectsIncoming_WhenFull()
        {
            EventQueue q = new EventQueue(capacity: 2, policy: OverflowPolicy.RejectNew);

            Assert.IsTrue(q.TryEnqueue(Q("a"), out _));
            Assert.IsTrue(q.TryEnqueue(Q("b"), out _));
            Assert.IsFalse(q.TryEnqueue(Q("c"), out _), "full => reject");

            Assert.AreEqual(2, q.Count);
            Assert.AreEqual(1, q.DroppedCount);
        }

        [Test]
        public void DropOldest_ReportsEvictedItem_SoCallerCanFailItsAwaiter()
        {
            EventQueue q = new EventQueue(capacity: 1, policy: OverflowPolicy.DropOldest);
            QueuedEvent first = Q("first");
            q.TryEnqueue(first, out _);

            q.TryEnqueue(Q("second"), out QueuedEvent evicted);

            Assert.AreSame(first, evicted);
        }

        [Test]
        public void DequeueBatch_IsFifo_AndRespectsMax()
        {
            EventQueue q = new EventQueue(capacity: 100, policy: OverflowPolicy.DropOldest);
            for (int i = 0; i < 5; i++)
            {
                q.TryEnqueue(Q("m" + i), out _);
            }

            List<QueuedEvent> first3 = q.DequeueBatch(3);

            Assert.AreEqual(3, first3.Count);
            Assert.AreEqual("m0", first3[0].Event.Payload["message"]);
            Assert.AreEqual("m2", first3[2].Event.Payload["message"]);
            Assert.AreEqual(2, q.Count, "remaining events stay buffered");
        }

        [Test]
        public void TryEnqueue_IsThreadSafe_UnderConcurrentProducers()
        {
            EventQueue q = new EventQueue(capacity: 100000, policy: OverflowPolicy.DropOldest);
            const int threads = 8;
            const int perThread = 1000;

            Task[] producers = new Task[threads];
            for (int t = 0; t < threads; t++)
            {
                producers[t] = Task.Run(() =>
                {
                    for (int i = 0; i < perThread; i++)
                    {
                        q.TryEnqueue(Q("m"), out _);
                    }
                });
            }
            Task.WaitAll(producers);

            Assert.AreEqual(threads * perThread, q.Count, "no lost updates under concurrent producers");
            Assert.AreEqual(0, q.DroppedCount, "capacity not exceeded, so nothing dropped");
        }
    }
}
