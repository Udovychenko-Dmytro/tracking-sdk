using System.Collections.Generic;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Thread-safe, bounded FIFO buffer between the producer (any thread calling the public API) and
    /// the background dispatcher. Enforces a hard capacity via the configured <see cref="OverflowPolicy"/>
    /// so memory stays bounded even when the network is down and events pile up.
    /// </summary>
    internal sealed class EventQueue
    {
        private readonly object m_gate = new object();
        private readonly LinkedList<QueuedEvent> m_items = new LinkedList<QueuedEvent>();
        private readonly int m_capacity;
        private readonly OverflowPolicy m_policy;
        private long m_dropped;

        public EventQueue(int capacity, OverflowPolicy policy)
        {
            m_capacity = capacity < 1 ? 1 : capacity;
            m_policy = policy;
        }

        /// <summary>Current number of buffered events.</summary>
        public int Count
        {
            get { lock (m_gate) { return m_items.Count; } }
        }

        /// <summary>Total events dropped due to overflow over the lifetime of this queue.</summary>
        public long DroppedCount
        {
            get { lock (m_gate) { return m_dropped; } }
        }

        /// <summary>
        /// Attempts to add an event.
        /// </summary>
        /// <param name="evicted">
        /// Under <see cref="OverflowPolicy.DropOldest"/>, the event that was evicted to make room
        /// (so the caller can fail its awaiter); otherwise <c>null</c>.
        /// </param>
        /// <returns>
        /// <c>true</c> if the event is now buffered; <c>false</c> if it was rejected
        /// (<see cref="OverflowPolicy.RejectNew"/> while full).
        /// </returns>
        public bool TryEnqueue(QueuedEvent item, out QueuedEvent evicted)
        {
            evicted = null;
            lock (m_gate)
            {
                if (m_items.Count >= m_capacity)
                {
                    if (m_policy == OverflowPolicy.RejectNew)
                    {
                        m_dropped++;
                        return false;
                    }

                    evicted = m_items.First.Value;
                    m_items.RemoveFirst();
                    m_dropped++;
                }

                m_items.AddLast(item);
                return true;
            }
        }

        /// <summary>Removes and returns up to <paramref name="max"/> events in FIFO order.</summary>
        public List<QueuedEvent> DequeueBatch(int max)
        {
            if (max < 1)
            {
                max = 1;
            }
            List<QueuedEvent> batch = new List<QueuedEvent>(max);
            lock (m_gate)
            {
                while (batch.Count < max && m_items.Count > 0)
                {
                    batch.Add(m_items.First.Value);
                    m_items.RemoveFirst();
                }
            }
            return batch;
        }

        /// <summary>Removes and returns every buffered item (for privacy purge).</summary>
        public List<QueuedEvent> RemoveAll()
        {
            lock (m_gate)
            {
                List<QueuedEvent> all = new List<QueuedEvent>(m_items);
                m_items.Clear();
                return all;
            }
        }

        /// <summary>Returns a snapshot of the buffered events (oldest first) without removing them.</summary>
        public List<TrackingEvent> Snapshot()
        {
            lock (m_gate)
            {
                List<TrackingEvent> list = new List<TrackingEvent>(m_items.Count);
                foreach (QueuedEvent q in m_items)
                {
                    list.Add(q.Event);
                }
                return list;
            }
        }

        /// <summary>Peeks the oldest buffered event without removing it (for time-based flush checks).</summary>
        public bool TryPeekOldest(out QueuedEvent item)
        {
            lock (m_gate)
            {
                if (m_items.Count == 0)
                {
                    item = null;
                    return false;
                }
                item = m_items.First.Value;
                return true;
            }
        }
    }
}
