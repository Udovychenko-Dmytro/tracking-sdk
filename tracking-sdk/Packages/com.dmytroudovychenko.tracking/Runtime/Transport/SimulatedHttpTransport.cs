using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Default production transport. It <em>simulates</em> POSTing the serialized batch to the
    /// configured endpoint (a real backend is intentionally out of scope): no socket is opened.
    /// With <c>failPercent == 0</c> delivery always succeeds; a positive value injects transient failures
    /// (~<c>failPercent</c>% of sends) so the retry / circuit-breaker / dead-letter pipeline can be exercised
    /// offline — this is what <see cref="ServerEnvironment.FakeServerChaos"/> selects. Each injected drop logs a
    /// <c>Warning</c> (like <see cref="HttpTransport"/> on a 503), so simulated chaos is just as visible.
    /// </summary>
    public sealed class SimulatedHttpTransport : ITransport
    {
        private const int PERCENT_MIN = 0;
        private const int PERCENT_MAX = 100;

        private readonly string m_endpoint;
        private readonly bool m_verbose;
        private readonly ITrackingLogger m_logger;
        private readonly int m_failPercent;
        private readonly Func<int> m_nextRoll;

        /// <param name="failPercent">Share of sends to fail transiently, clamped to [0,100]; 0 = always succeed.</param>
        /// <param name="nextRoll">DI seam returning a 1..100 roll; failure when <c>roll &lt;= failPercent</c>.
        /// Defaults to a per-instance <see cref="Random"/> (the single worker thread drives it, so no sharing).</param>
        public SimulatedHttpTransport(
            string endpoint = TrackingConfig.DEFAULT_ENDPOINT, bool verbose = false, ITrackingLogger logger = null,
            int failPercent = 0, Func<int> nextRoll = null)
        {
            m_endpoint = string.IsNullOrEmpty(endpoint) ? TrackingConfig.DEFAULT_ENDPOINT : endpoint;
            m_verbose = verbose;
            m_logger = logger ?? NullTrackingLogger.Instance;
            m_failPercent = Math.Min(PERCENT_MAX, Math.Max(PERCENT_MIN, failPercent));
            if (nextRoll != null)
            {
                m_nextRoll = nextRoll;
            }
            else
            {
                Random random = new Random();
                m_nextRoll = () => random.Next(PERCENT_MIN + 1, PERCENT_MAX + 1);
            }
        }

        public Task<bool> SendAsync(IReadOnlyList<TrackingEvent> batch, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(false);
            }

            if (m_failPercent > 0 && m_nextRoll() <= m_failPercent)
            {
                // Warn unconditionally, mirroring HttpTransport's failure log, so simulated chaos is as visible as a 503.
                m_logger.Log(TrackingLogLevel.Warning, $"POST {m_endpoint} failed: simulated chaos {m_failPercent}% → transient failure");
                return Task.FromResult(false);
            }

            if (m_verbose && batch != null)
            {
                m_logger.Log(TrackingLogLevel.Info, $"[SimulatedHttpTransport] POST {m_endpoint} — {batch.Count} event(s)");
            }

            return Task.FromResult(true);
        }
    }
}
