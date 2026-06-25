using System.Collections.Generic;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Thread-safe in-memory <see cref="IEventStore"/>. Doesn't survive a process restart, but is ideal
    /// for tests and for modelling the persist/reload flow without disk I/O.
    /// </summary>
    public sealed class InMemoryEventStore : IEventStore
    {
        private readonly object m_gate = new object();
        private List<TrackingEvent> m_events = new List<TrackingEvent>();

        public void Save(IReadOnlyList<TrackingEvent> events)
        {
            lock (m_gate)
            {
                m_events = events != null ? new List<TrackingEvent>(events) : new List<TrackingEvent>();
            }
        }

        public IReadOnlyList<TrackingEvent> Load()
        {
            lock (m_gate) { return m_events.ToArray(); }
        }

        public void Clear()
        {
            lock (m_gate) { m_events = new List<TrackingEvent>(); }
        }
    }
}
