using System;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Classic circuit breaker. After a run of consecutive failures it "opens" and blocks further
    /// requests for a cooldown, sparing a struggling server from a thundering herd. After the cooldown
    /// it "half-opens" to allow one trial: success closes it, another failure re-opens it.
    /// </summary>
    /// <remarks>Time is taken from the injected <see cref="IClock"/>, so cooldown behaviour is
    /// deterministically testable on a virtual clock.</remarks>
    public sealed class CircuitBreaker
    {
        private readonly int m_threshold;
        private readonly TimeSpan m_openDuration;
        private readonly IClock m_clock;
        private readonly object m_gate = new object();

        private int m_consecutiveFailures;
        private CircuitState m_state = CircuitState.Closed;
        private DateTimeOffset m_openedAt;

        public CircuitBreaker(int failureThreshold, TimeSpan openDuration, IClock clock)
        {
            m_threshold = failureThreshold < 1 ? 1 : failureThreshold;
            m_openDuration = openDuration;
            m_clock = clock ?? SystemClock.Instance;
        }

        public CircuitState State
        {
            get { lock (m_gate) { return Effective(); } }
        }

        /// <summary>Whether a request may be attempted right now.</summary>
        public bool AllowRequest()
        {
            lock (m_gate) { return Effective() != CircuitState.Open; }
        }

        public void RecordSuccess()
        {
            lock (m_gate)
            {
                m_consecutiveFailures = 0;
                m_state = CircuitState.Closed;
            }
        }

        public void RecordFailure()
        {
            lock (m_gate)
            {
                m_consecutiveFailures++;
                if (m_state == CircuitState.HalfOpen || m_consecutiveFailures >= m_threshold)
                {
                    m_state = CircuitState.Open;
                    m_openedAt = m_clock.UtcNow;
                }
            }
        }

        // Must be called under m_gate. Transitions Open -> HalfOpen once the cooldown has elapsed.
        private CircuitState Effective()
        {
            if (m_state == CircuitState.Open && m_clock.UtcNow - m_openedAt >= m_openDuration)
            {
                m_state = CircuitState.HalfOpen;
            }
            return m_state;
        }
    }
}
