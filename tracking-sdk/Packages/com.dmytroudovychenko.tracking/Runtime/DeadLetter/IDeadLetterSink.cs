using System.Collections.Generic;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Destination for events that could not be delivered after exhausting all retries. Keeping them
    /// (rather than silently dropping) lets a host app inspect, alert on, or replay failed events.
    /// </summary>
    public interface IDeadLetterSink
    {
        void DeadLetter(IReadOnlyList<TrackingEvent> events);

        IReadOnlyList<TrackingEvent> Snapshot();

        int Count { get; }

        void Clear();
    }

    /// <summary>Default bounded, thread-safe in-memory dead-letter queue (drops oldest when full).</summary>
    public sealed class InMemoryDeadLetterQueue : IDeadLetterSink
    {
        private readonly object m_gate = new object();
        private readonly LinkedList<TrackingEvent> m_items = new LinkedList<TrackingEvent>();
        private readonly int m_capacity;

        public InMemoryDeadLetterQueue(int capacity = 1000)
        {
            m_capacity = capacity < 1 ? 1 : capacity;
        }

        public int Count
        {
            get { lock (m_gate) { return m_items.Count; } }
        }

        public void DeadLetter(IReadOnlyList<TrackingEvent> events)
        {
            if (events == null) return;
            lock (m_gate)
            {
                foreach (TrackingEvent e in events)
                {
                    if (e == null) continue;
                    while (m_items.Count >= m_capacity)
                    {
                        m_items.RemoveFirst();
                    }
                    m_items.AddLast(e);
                }
            }
        }

        public IReadOnlyList<TrackingEvent> Snapshot()
        {
            lock (m_gate) { return new List<TrackingEvent>(m_items); }
        }

        public void Clear()
        {
            lock (m_gate) { m_items.Clear(); }
        }
    }
}
