using System;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Tunable configuration for the tracking pipeline. Sensible production defaults are baked in;
    /// every value is overridable via object-initializer syntax.
    /// </summary>
    /// <remarks>
    /// Fields are introduced up-front (rather than grown phase-by-phase) so the configuration surface
    /// is stable. The comment on each value notes the phase from which it becomes active.
    /// </remarks>
    public sealed class TrackingConfig
    {
        /// <summary>Documented default placeholder endpoint.</summary>
        public const string DEFAULT_ENDPOINT = "https://fakeserver.com";

        /// <summary>Live test receiver (stub) endpoint — developer diagnostic, not a production backend.
        /// (Selected by <see cref="ServerEnvironment.HttpTestServer"/>.)</summary>
        public const string HTTP_TEST_ENDPOINT = "https://udovychenko.xyz/test/track.php";

        /// <summary>Chaos failure rate (percent): the share of sends that fail transiently in chaos mode —
        /// both the HTTP receiver (<see cref="CHAOS_QUERY"/>) and the simulated transport
        /// (<see cref="ServerEnvironment.FakeServerChaos"/>) honour it.</summary>
        public const int CHAOS_FAIL_PERCENT = 20;

        /// <summary>Query suffix that switches the receiver into chaos mode (~20% transient 503s).
        /// Keep the number in sync with <see cref="CHAOS_FAIL_PERCENT"/>.</summary>
        public const string CHAOS_QUERY = "?fail=20";

        /// <summary>Receiver in chaos mode. (Selected by <see cref="ServerEnvironment.HttpTestServerChaos"/>.)</summary>
        public const string HTTP_TEST_CHAOS_ENDPOINT = HTTP_TEST_ENDPOINT + CHAOS_QUERY;

        /// <summary>Per-request timeout for the server-reachability probe — kept short so Init never hangs.</summary>
        public static readonly TimeSpan DEFAULT_CONNECTIVITY_PROBE_TIMEOUT = TimeSpan.FromSeconds(5);

        /// <summary>Identifier of the user these events belong to; stamped on every event. (Active: Phase 1.)</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>Destination the transport posts batches to. (Active: Phase 6 real transport.)</summary>
        public string Endpoint { get; set; } = DEFAULT_ENDPOINT;

        /// <summary>Which default transport to build when none is injected. (Active: Phase 6.)</summary>
        public TransportMode TransportMode { get; set; } = TransportMode.Simulated;

        /// <summary>Per-request HTTP timeout for the real transport. (Active: Phase 6.)</summary>
        public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Percent of sends the <em>simulated</em> transport fails transiently (0 = never).
        /// Set to <see cref="CHAOS_FAIL_PERCENT"/> by <see cref="ServerEnvironment.FakeServerChaos"/> to drive the
        /// retry / circuit-breaker / dead-letter pipeline offline; ignored by the real HTTP transport.</summary>
        public int SimulatedFailPercent { get; set; } = 0;

        /// <summary>Master on/off switch. When <c>false</c>, the API accepts nothing. (Active: Phase 1.)</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Anonymous (privacy) mode: when <c>true</c>, events are stamped with userId <c>"anonymous"</c>
        /// instead of the real user. Flip at runtime via <see cref="TrackingSystem.SetPrivacyMode"/>. (Active: BLI-006.)</summary>
        public bool PrivacyMode { get; set; } = false;

        /// <summary>Maximum number of events delivered in a single batch. (Active: Phase 2.)</summary>
        public int BatchSize { get; set; } = 20;

        /// <summary>Maximum time a partial batch waits before being flushed. (Active: Phase 2.)</summary>
        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>Upper bound on buffered events; beyond it the drop policy applies. (Active: Phase 2.)</summary>
        public int MaxQueueCapacity { get; set; } = 10_000;

        /// <summary>What happens when an event arrives and the buffer is full. (Active: Phase 2.)</summary>
        public OverflowPolicy OverflowPolicy { get; set; } = OverflowPolicy.DropOldest;

        /// <summary>Maximum delivery attempts before an event is given up / dead-lettered. (Active: Phase 3.)</summary>
        public int MaxRetryAttempts { get; set; } = 5;

        /// <summary>Base delay for the first retry; grows exponentially with jitter. (Active: Phase 3.)</summary>
        public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>Ceiling for exponential backoff between retries. (Active: Phase 3.)</summary>
        public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Consecutive batch failures before the circuit breaker opens. (Active: Phase 5.)</summary>
        public int CircuitBreakerThreshold { get; set; } = 5;

        /// <summary>How long the circuit breaker stays open before a trial request. (Active: Phase 5.)</summary>
        public TimeSpan CircuitBreakerCooldown { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Capacity of the in-memory dead-letter queue for give-up events. (Active: Phase 5.)</summary>
        public int DeadLetterCapacity { get; set; } = 1000;

        /// <summary>How long <see cref="TrackingSystem.Dispose"/> waits for the worker's final drain before abandoning it.</summary>
        public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>Minimum severity that reaches the logger: <c>Debug</c> (most verbose — adds per-step traces + event payload/JSON) … <c>Error</c> (least). Default <c>Warning</c> (quiet). (Active: Phase 2.)</summary>
        public TrackingLogLevel MinLogLevel { get; set; } = TrackingLogLevel.Warning;

        /// <summary>Maps a named <see cref="ServerEnvironment"/> to its endpoint URL.</summary>
        public static string EndpointFor(ServerEnvironment server)
        {
            switch (server)
            {
                case ServerEnvironment.HttpTestServer: return HTTP_TEST_ENDPOINT;
                case ServerEnvironment.HttpTestServerChaos: return HTTP_TEST_CHAOS_ENDPOINT;
                case ServerEnvironment.FakeServer: return DEFAULT_ENDPOINT;
                case ServerEnvironment.FakeServerChaos: return DEFAULT_ENDPOINT;
                default: return DEFAULT_ENDPOINT;
            }
        }

        /// <summary>Fault rate the simulated transport applies for a named server: only
        /// <see cref="ServerEnvironment.FakeServerChaos"/> injects failures; every other value is 0 (clean).</summary>
        public static int SimulatedFailPercentFor(ServerEnvironment server)
            => server == ServerEnvironment.FakeServerChaos ? CHAOS_FAIL_PERCENT : 0;
    }
}
