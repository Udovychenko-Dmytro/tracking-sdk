using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DmytroUdovychenko.Tracking.Tests
{
    /// <summary>
    /// Test <see cref="ITransport"/> that records every batch (and the flattened events) it is asked to
    /// send, and returns a configurable result. The internals are locked so it is safe to use from the
    /// background dispatcher thread introduced in Phase 2.
    /// </summary>
    public sealed class RecordingTransport : ITransport
    {
        private readonly object m_gate = new object();
        private readonly List<TrackingEvent> m_events = new List<TrackingEvent>();
        private readonly List<IReadOnlyList<TrackingEvent>> m_batches = new List<IReadOnlyList<TrackingEvent>>();

        /// <summary>Result returned by the next (and subsequent) <see cref="SendAsync"/> calls.</summary>
        public bool NextResult { get; set; } = true;

        /// <summary>Total number of <see cref="SendAsync"/> invocations (i.e. delivered batches).</summary>
        public int SendCount { get; private set; }

        /// <summary>Snapshot of all events received, in arrival order.</summary>
        public IReadOnlyList<TrackingEvent> Events
        {
            get { lock (m_gate) { return m_events.ToArray(); } }
        }

        /// <summary>Snapshot of all batches received, in arrival order.</summary>
        public IReadOnlyList<IReadOnlyList<TrackingEvent>> Batches
        {
            get { lock (m_gate) { return m_batches.ToArray(); } }
        }

        public Task<bool> SendAsync(IReadOnlyList<TrackingEvent> batch, CancellationToken cancellationToken = default)
        {
            lock (m_gate)
            {
                SendCount++;
                m_batches.Add(batch);
                m_events.AddRange(batch);
                return Task.FromResult(NextResult);
            }
        }
    }
}
