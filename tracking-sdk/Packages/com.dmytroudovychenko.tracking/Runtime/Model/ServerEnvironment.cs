namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Named target server for <see cref="TrackingSystem.Init(string, ServerEnvironment)"/>; each value maps to
    /// a fixed endpoint URL via <see cref="TrackingConfig.EndpointFor"/>. Two semantic groups (100-block scheme):
    /// simulated/offline (0-block) and real-HTTP (100-block); each has a clean and a chaos variant.
    /// </summary>
    public enum ServerEnvironment
    {
        /// <summary>Offline fake host; delivery is simulated, no real network is hit. The safe default
        /// (<c>default(ServerEnvironment)</c>) — needs no connectivity, so <c>Init</c> never blocks on it.</summary>
        FakeServer = 0,

        /// <summary>Offline fake host in chaos mode: still simulated (no network), but ~20% of sends fail
        /// transiently — exercises the retry / circuit-breaker / dead-letter pipeline without a real server.</summary>
        FakeServerChaos = 1,

        /// <summary>Live test receiver (stub) — real HTTP to the developer diagnostic endpoint, not a
        /// production backend; Init requires connectivity.</summary>
        HttpTestServer = 101,

        /// <summary>Same live test receiver (stub) in chaos mode (<c>?fail=20</c> → ~20% transient 503s —
        /// exercises retries). (Real HTTP.)</summary>
        HttpTestServerChaos = 102
    }
}
