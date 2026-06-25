using System;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Computes retry backoff using capped exponential growth with "equal jitter"
    /// (delay ∈ [base/2, base]), so failed deliveries are retried with increasing, spread-out delays
    /// instead of hammering a struggling server in lockstep.
    /// </summary>
    /// <remarks>
    /// Pure and side-effect free (the actual waiting is delegated to an <see cref="IDelayer"/>), which
    /// makes backoff timing trivially unit-testable. A deterministic jitter source can be injected for
    /// exact assertions; production uses a thread-safe <see cref="System.Random"/>.
    /// </remarks>
    public sealed class RetryPolicy
    {
        private readonly int m_maxAttempts;
        private readonly double m_initialMs;
        private readonly double m_maxMs;
        private readonly Func<double> m_jitter;
        private readonly object m_rngGate = new object();
        private readonly Random m_rng;

        /// <param name="maxAttempts">Total delivery attempts (including the first), at least 1.</param>
        /// <param name="initialDelay">Base delay before the first retry.</param>
        /// <param name="maxDelay">Ceiling the exponential base is capped at.</param>
        /// <param name="jitter">Optional jitter source returning [0,1]; defaults to a random source.</param>
        public RetryPolicy(int maxAttempts, TimeSpan initialDelay, TimeSpan maxDelay, Func<double> jitter = null)
        {
            m_maxAttempts = maxAttempts < 1 ? 1 : maxAttempts;
            m_initialMs = Math.Max(0, initialDelay.TotalMilliseconds);
            m_maxMs = Math.Max(m_initialMs, maxDelay.TotalMilliseconds);

            if (jitter != null)
            {
                m_jitter = jitter;
            }
            else
            {
                m_rng = new Random();
                m_jitter = DefaultJitter;
            }
        }

        /// <summary>Total number of attempts allowed (including the first try).</summary>
        public int MaxAttempts => m_maxAttempts;

        /// <summary>
        /// Delay to wait after a failed attempt before the next one.
        /// </summary>
        /// <param name="attemptNumber">1-based number of the attempt that just failed.</param>
        /// <param name="delay">The computed backoff delay, when a retry remains.</param>
        /// <returns><c>false</c> when no further attempts remain (give up).</returns>
        public bool TryGetDelay(int attemptNumber, out TimeSpan delay)
        {
            if (attemptNumber < 1)
            {
                attemptNumber = 1;
            }

            if (attemptNumber >= m_maxAttempts)
            {
                delay = TimeSpan.Zero;
                return false;
            }

            double baseMs = m_initialMs * Math.Pow(2, attemptNumber - 1);
            if (baseMs > m_maxMs)
            {
                baseMs = m_maxMs;
            }

            double j = m_jitter();
            if (j < 0)
            {
                j = 0;
            }
            else if (j > 1)
            {
                j = 1;
            }

            double ms = baseMs * 0.5 + j * baseMs * 0.5;
            delay = TimeSpan.FromMilliseconds(ms);
            return true;
        }

        private double DefaultJitter()
        {
            lock (m_rngGate) { return m_rng.NextDouble(); }
        }
    }
}
