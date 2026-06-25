using System.Threading;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>Immutable point-in-time view of the SDK's counters.</summary>
    public readonly struct TrackingMetricsSnapshot
    {
        public readonly long Enqueued;
        public readonly long Sent;
        public readonly long Dropped;
        public readonly long Retried;
        public readonly long GivenUp;
        public readonly long DeadLettered;

        public TrackingMetricsSnapshot(long enqueued, long sent, long dropped, long retried, long givenUp, long deadLettered)
        {
            Enqueued = enqueued;
            Sent = sent;
            Dropped = dropped;
            Retried = retried;
            GivenUp = givenUp;
            DeadLettered = deadLettered;
        }

        public override string ToString() =>
            $"enqueued={Enqueued} sent={Sent} dropped={Dropped} retried={Retried} givenUp={GivenUp} deadLettered={DeadLettered}";
    }

    /// <summary>
    /// Thread-safe diagnostic counters for the pipeline. Cheap (interlocked) and exposed as an
    /// immutable <see cref="TrackingMetricsSnapshot"/> for dashboards / the demo's live counters.
    /// </summary>
    public sealed class TrackingMetrics
    {
        private long m_enqueued;
        private long m_sent;
        private long m_dropped;
        private long m_retried;
        private long m_givenUp;
        private long m_deadLettered;

        public void IncEnqueued() => Interlocked.Increment(ref m_enqueued);
        public void IncDropped() => Interlocked.Increment(ref m_dropped);
        public void IncRetried() => Interlocked.Increment(ref m_retried);
        public void AddSent(long count) => Interlocked.Add(ref m_sent, count);
        public void AddGivenUp(long count) => Interlocked.Add(ref m_givenUp, count);
        public void AddDeadLettered(long count) => Interlocked.Add(ref m_deadLettered, count);

        public TrackingMetricsSnapshot Snapshot() => new TrackingMetricsSnapshot(
            Interlocked.Read(ref m_enqueued),
            Interlocked.Read(ref m_sent),
            Interlocked.Read(ref m_dropped),
            Interlocked.Read(ref m_retried),
            Interlocked.Read(ref m_givenUp),
            Interlocked.Read(ref m_deadLettered));
    }
}
