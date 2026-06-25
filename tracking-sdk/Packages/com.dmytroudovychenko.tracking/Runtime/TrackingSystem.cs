using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Default <see cref="ITracker"/> implementation.
    /// </summary>
    /// <remarks>
    /// Pipeline: validate &amp; enrich on the caller's thread → push to a bounded, thread-safe
    /// <see cref="EventQueue"/> → a background <see cref="EventDispatcher"/> batches, retries, and
    /// delivers via <see cref="ITransport"/>. The hot path never touches the network. Every external
    /// concern is injectable (transport, clock, runtime-info, delayer, store, logger, connectivity,
    /// dead-letter) so the whole flow is deterministically testable; tests pass <c>startWorker: false</c>
    /// and pump delivery explicitly via <see cref="FlushAsync"/>.
    /// </remarks>
    public sealed class TrackingSystem : ITracker, IDisposable
    {
        // BLI-006 privacy mode: stamped in place of the real userId while anonymous mode is on.
        private const string PRIVACY_ANONYMOUS_USER_ID = "anonymous";

        private readonly TrackingConfig m_config;
        private readonly IClock m_clock;
        private readonly IRuntimeInfo m_runtime;
        private readonly IEventStore m_store;
        private readonly ITrackingLogger m_logger;
        private readonly TrackingMetrics m_metrics;
        private readonly IDeadLetterSink m_deadLetter;
        private readonly EventQueue m_queue;
        private readonly EventDispatcher m_dispatcher;
        private readonly IConnectivity m_connectivity;
        private readonly IDisposable m_ownedTransport;
        private readonly string m_sessionId;
        // Serializes the enabled-gate with enqueue/purge so an event can't slip into the queue after a
        // racing SetEnabled(false) has already purged (privacy opt-out). In-memory only — no I/O.
        private readonly object m_lifecycleGate = new object();

        private volatile bool m_enabled;
        private volatile bool m_privacyMode;
        private volatile bool m_disposed;

        /// <summary>
        /// Creates a tracker. Every dependency is optional and falls back to a production default,
        /// so <c>new TrackingSystem()</c> is valid and simulates delivery to <see cref="TrackingConfig.Endpoint"/>.
        /// </summary>
        /// <param name="startWorker">When <c>true</c> (default) the background delivery worker starts
        /// immediately. Tests pass <c>false</c> and drive delivery through <see cref="FlushAsync"/>.</param>
        public TrackingSystem(
            TrackingConfig config = null,
            ITransport transport = null,
            IClock clock = null,
            IRuntimeInfo runtime = null,
            bool startWorker = true,
            IDelayer delayer = null,
            IEventStore store = null,
            ITrackingLogger logger = null,
            IConnectivity connectivity = null,
            IDeadLetterSink deadLetter = null)
        {
            m_config = config ?? new TrackingConfig();
            m_clock = clock ?? SystemClock.Instance;
            m_runtime = runtime ?? new UnityRuntimeInfo();
            m_store = store ?? NullEventStore.Instance;
            // Filter by config severity so MinLogLevel governs every SDK component fed this logger
            // (this system, the dispatcher, the default transport) uniformly.
            m_logger = new LevelFilteringTrackingLogger(logger ?? UnityTrackingLogger.Instance, m_config.MinLogLevel);
            m_metrics = new TrackingMetrics();
            m_deadLetter = deadLetter ?? new InMemoryDeadLetterQueue(m_config.DeadLetterCapacity);
            m_sessionId = Guid.NewGuid().ToString("N");
            m_enabled = m_config.Enabled;
            m_privacyMode = m_config.PrivacyMode;

            ITransport resolvedTransport = transport ?? CreateDefaultTransport(m_config, m_logger);
            m_ownedTransport = transport == null ? resolvedTransport as IDisposable : null;
            IConnectivity resolvedConnectivity = connectivity ?? AlwaysOnlineConnectivity.Instance;
            m_connectivity = resolvedConnectivity;
            RetryPolicy retryPolicy = new RetryPolicy(
                m_config.MaxRetryAttempts, m_config.InitialRetryDelay, m_config.MaxRetryDelay);
            CircuitBreaker breaker = new CircuitBreaker(
                m_config.CircuitBreakerThreshold, m_config.CircuitBreakerCooldown, m_clock);

            m_queue = new EventQueue(m_config.MaxQueueCapacity, m_config.OverflowPolicy);
            m_dispatcher = new EventDispatcher(
                m_queue, resolvedTransport, m_clock, m_config,
                retryPolicy, delayer, m_metrics, resolvedConnectivity, breaker, m_deadLetter, m_logger);

            ReloadPersistedBacklog();

            if (startWorker)
            {
                m_dispatcher.Start();
            }

            if (ShouldLog(TrackingLogLevel.Info))
            {
                m_logger.Log(
                    TrackingLogLevel.Info,
                    $"initialized: userId={m_config.UserId}, sessionId={m_sessionId}, " +
                    $"endpoint={m_config.Endpoint}, transport={m_config.TransportMode}, enabled={m_enabled}");
            }
        }

        // True when a message at this severity passes the configured MinLogLevel — guards verbose-only
        // logs so their interpolation/serialization is skipped when filtered out.
        private bool ShouldLog(TrackingLogLevel level) => level >= m_config.MinLogLevel;

        private static string FormatPayload(Dictionary<string, object> payload)
        {
            List<string> parts = new List<string>(payload.Count);
            foreach (KeyValuePair<string, object> entry in payload)
            {
                parts.Add(entry.Key + "=" + entry.Value);
            }
            return "{" + string.Join(", ", parts) + "}";
        }

        /// <summary>
        /// Initializes a tracker for <paramref name="userId"/> against the default endpoint (simulated delivery).
        /// </summary>
        /// <remarks>Init is the production entry point: it wires durable persistence and
        /// connectivity-awareness; the DI constructor stays bare (Null store, always-online) for tests.</remarks>
        public static TrackingSystem Init(string userId, TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)
        {
            return CreateConfigured(userId, TrackingConfig.DEFAULT_ENDPOINT, TransportMode.Simulated, minLogLevel);
        }

        /// <summary>
        /// Initializes a tracker for <paramref name="userId"/> against a named <paramref name="server"/>.
        /// </summary>
        /// <remarks>The <c>FakeServer*</c> values are the offline fakes (simulated, no network);
        /// the <c>HttpTestServer*</c> values use real HTTP delivery and so require connectivity at Init.</remarks>
        /// <returns>The tracker, or <c>null</c> when a live server is targeted while the device is offline.</returns>
        public static TrackingSystem Init(string userId, ServerEnvironment server, TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)
        {
            return CreateConfigured(
                userId, TrackingConfig.EndpointFor(server), TransportModeFor(server), minLogLevel,
                TrackingConfig.SimulatedFailPercentFor(server));
        }

        /// <summary>Single source of truth for server → transport mode: the <c>FakeServer*</c> values are
        /// simulated (no network); every <c>HttpTestServer*</c> value delivers over real HTTP.</summary>
        internal static TransportMode TransportModeFor(ServerEnvironment server)
            => (server == ServerEnvironment.FakeServer || server == ServerEnvironment.FakeServerChaos)
                ? TransportMode.Simulated : TransportMode.Http;

        /// <summary>
        /// Initializes a tracker for <paramref name="userId"/> against a custom <paramref name="endpoint"/> (real HTTP).
        /// </summary>
        /// <returns>The tracker, or <c>null</c> when the device is offline (real HTTP requires connectivity).</returns>
        public static TrackingSystem Init(string userId, string endpoint, TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)
        {
            return CreateConfigured(userId, endpoint, TransportMode.Http, minLogLevel);
        }

        private static TrackingSystem CreateConfigured(
            string userId, string endpoint, TransportMode transportMode, TrackingLogLevel minLogLevel = TrackingLogLevel.Warning,
            int simulatedFailPercent = 0)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("userId is required and must be non-empty.", nameof(userId));
            }

            // One snapshot drives both the offline gate and the running tracker (built on the main thread).
            UnityConnectivity connectivity = new UnityConnectivity();
            if (IsBlockedOffline(transportMode, connectivity))
            {
                UnityTrackingLogger.Instance.Log(
                    TrackingLogLevel.Warning,
                    "TrackingSystem.Init skipped: device offline and the target requires connectivity. " +
                    "Retry when online, or use ServerEnvironment.FakeServer for offline buffering.");
                return null;
            }

            TrackingConfig config = new TrackingConfig
            {
                UserId = userId,
                Endpoint = endpoint,
                TransportMode = transportMode,
                MinLogLevel = minLogLevel,
                SimulatedFailPercent = simulatedFailPercent,
            };
            // Production seams: durable persistence + connectivity-awareness. (Logger/runtime-info already
            // default to the Unity-backed implementations in the constructor.)
            return new TrackingSystem(
                config,
                store: new FileEventStore(),
                connectivity: connectivity);
        }

        /// <summary>Init blocks only when real HTTP delivery is requested while the device is offline;
        /// the simulated path always proceeds (offline buffering is its whole point).</summary>
        internal static bool IsBlockedOffline(TransportMode transportMode, IConnectivity connectivity)
            => transportMode == TransportMode.Http && connectivity != null && !connectivity.IsOnline;

        /// <summary>Stable identifier shared by every event produced by this tracker instance.</summary>
        public string SessionId => m_sessionId;

        /// <summary>Re-polls connectivity on the main thread; no-op unless Unity-backed connectivity is wired.</summary>
        internal void RefreshConnectivity() => (m_connectivity as UnityConnectivity)?.Refresh();

        /// <summary>Identifier of the user every event from this tracker is attributed to.</summary>
        public string UserId => m_config.UserId;

        /// <summary>Live diagnostic counters (enqueued / sent / dropped / retried / given-up / dead-lettered).</summary>
        public TrackingMetricsSnapshot Metrics => m_metrics.Snapshot();

        /// <summary>Events that exhausted their retries — available for inspection or replay.</summary>
        public IDeadLetterSink DeadLetter => m_deadLetter;

        /// <summary>Whether tracking is currently accepting events.</summary>
        public bool IsEnabled => m_enabled;

        /// <summary>Whether anonymous (privacy) mode is on — every event leaves with userId <c>"anonymous"</c>.</summary>
        public bool IsPrivacyMode => m_privacyMode;

        /// <inheritdoc />
        public bool SendMessage(string message)
        {
            try
            {
                if (!m_enabled) return false;
                if (string.IsNullOrWhiteSpace(message))
                {
                    m_logger.Log(TrackingLogLevel.Error, "SendMessage: empty message, nothing to send");
                    return false;
                }

                TrackingEvent trackingEvent = CreateEvent(
                    TrackingEventType.MESSAGE,
                    new Dictionary<string, object> { ["message"] = message });

                bool accepted = Enqueue(trackingEvent, completion: null);
                if (accepted)
                {
                    if (ShouldLog(TrackingLogLevel.Info))
                    {
                        m_logger.Log(TrackingLogLevel.Info, $"enqueued {trackingEvent.Type} event {trackingEvent.Id}");
                    }
                    if (ShouldLog(TrackingLogLevel.Debug))
                    {
                        m_logger.Log(TrackingLogLevel.Debug, $"  content: message=\"{message}\"");
                    }
                }
                return accepted;
            }
            catch (Exception e)
            {
                // Error isolation: tracking must never throw into game code.
                m_logger.Log(TrackingLogLevel.Error, "SendMessage failed", e);
                return false;
            }
        }

        /// <inheritdoc />
        public Task<bool> SendMapAsync(Dictionary<string, object> map)
        {
            try
            {
                if (!m_enabled) return Task.FromResult(false);
                if (map == null || map.Count == 0)
                {
                    m_logger.Log(TrackingLogLevel.Error, "SendMapAsync: empty map, nothing to send");
                    return Task.FromResult(false);
                }

                Dictionary<string, object> filteredMap = new Dictionary<string, object>(map.Count);
                foreach (KeyValuePair<string, object> entry in map)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key))
                    {
                        m_logger.Log(TrackingLogLevel.Warning, "SendMapAsync: dropping map entry with empty key");
                        continue;
                    }
                    if (entry.Value == null)
                    {
                        m_logger.Log(TrackingLogLevel.Warning,
                            "SendMapAsync: dropping map entry with null value (key: " + entry.Key + ")");
                        continue;
                    }
                    filteredMap[entry.Key] = entry.Value;
                }

                if (filteredMap.Count == 0)
                {
                    m_logger.Log(TrackingLogLevel.Error,
                        "SendMapAsync: all map entries were invalid, nothing to send");
                    return Task.FromResult(false);
                }

                TrackingEvent trackingEvent = CreateEvent(TrackingEventType.MAP, filteredMap);

                TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!Enqueue(trackingEvent, completion))
                {
                    completion.TrySetResult(false); // rejected: queue full under RejectNew
                }
                else
                {
                    if (ShouldLog(TrackingLogLevel.Info))
                    {
                        m_logger.Log(TrackingLogLevel.Info,
                            $"enqueued {trackingEvent.Type} event {trackingEvent.Id} ({filteredMap.Count} field(s))");
                    }
                    if (ShouldLog(TrackingLogLevel.Debug))
                    {
                        m_logger.Log(TrackingLogLevel.Debug, "  content: " + FormatPayload(filteredMap));
                    }
                }

                return completion.Task;
            }
            catch (Exception e)
            {
                m_logger.Log(TrackingLogLevel.Error, "SendMapAsync failed", e);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Forces delivery of everything currently buffered and completes when the queue is empty.
        /// </summary>
        public async Task FlushAsync()
        {
            try
            {
                await m_dispatcher.DrainAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // Error isolation: an explicit flush must never throw into game code (e.g. a drain racing Dispose).
                m_logger.Log(TrackingLogLevel.Error, "FlushAsync failed", e);
            }
        }

        /// <summary>
        /// Snapshots the currently-buffered (undelivered) events to durable storage. Called on the app
        /// lifecycle (pause/quit) via <see cref="TrackingLifecycle"/> so the tail survives a kill.
        /// Non-destructive: the events stay in the queue and are still delivered if the app continues.
        /// </summary>
        public void Persist()
        {
            try { m_store.Save(m_queue.Snapshot()); }
            catch (Exception e) { m_logger.Log(TrackingLogLevel.Error, "Persist failed", e); }
        }

        /// <summary>
        /// Enables or disables tracking at runtime. Disabling stops accepting events and immediately
        /// <see cref="Purge"/>s buffered data (privacy opt-out — e.g. GDPR consent withdrawal).
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            if (!enabled)
            {
                // Flip the gate under the lifecycle lock so any concurrent Enqueue either lands before
                // this point (and is then purged) or observes the disable and is rejected. Purge runs
                // after — no new event can be buffered between the flip and the purge.
                lock (m_lifecycleGate)
                {
                    m_enabled = false;
                }
                Purge();
            }
            else
            {
                m_enabled = true;
            }
        }

        /// <summary>
        /// Turns anonymous (privacy) mode on/off at runtime: when on, events are stamped with userId
        /// <c>"anonymous"</c> (sessionId + context kept). Forward-only — already-buffered events aren't scrubbed.
        /// </summary>
        public void SetPrivacyMode(bool enabled) => m_privacyMode = enabled;

        /// <summary>
        /// Discards all buffered, dead-lettered, and persisted events (a "delete my data" operation).
        /// Pending async awaiters resolve <c>false</c>.
        /// </summary>
        public void Purge()
        {
            try
            {
                List<QueuedEvent> removed = m_queue.RemoveAll();
                for (int i = 0; i < removed.Count; i++)
                {
                    removed[i].Completion?.TrySetResult(false);
                }
                m_deadLetter.Clear();
                m_store.Clear();
            }
            catch (Exception e)
            {
                m_logger.Log(TrackingLogLevel.Error, "Purge failed", e);
            }
        }

        private bool Enqueue(TrackingEvent trackingEvent, TaskCompletionSource<bool> completion)
        {
            QueuedEvent queued = new QueuedEvent { Event = trackingEvent, Completion = completion };

            QueuedEvent evicted;
            lock (m_lifecycleGate)
            {
                // Authoritative enabled/disposed check (the public methods' check is just a fast path). Done
                // with the enqueue under one lock so a racing opt-out/Dispose can't leave this event un-purged
                // or stranded in a dead queue.
                if (m_disposed || !m_enabled) return false;
                if (!m_queue.TryEnqueue(queued, out evicted))
                {
                    m_metrics.IncDropped(); // rejected (queue full under RejectNew)
                    return false;
                }
            }

            if (evicted != null)
            {
                m_metrics.IncDropped(); // evicted (DropOldest)
                evicted.Completion?.TrySetResult(false);
            }

            m_metrics.IncEnqueued();
            try
            {
                m_dispatcher.Signal();
            }
            catch (ObjectDisposedException)
            {
                // Raced a concurrent Dispose: the worker is gone and won't drain this event, so fail its
                // awaiter here rather than strand a never-completed Task.
                completion?.TrySetResult(false);
                return false;
            }
            return true;
        }

        // internal (not private) so the dual-mode selection can be unit-tested without a live network.
        internal static ITransport CreateDefaultTransport(TrackingConfig config, ITrackingLogger logger)
        {
            switch (config.TransportMode)
            {
                case TransportMode.Http:
                    return new HttpTransport(config.Endpoint, config.HttpTimeout, logger);
                default:
                    return new SimulatedHttpTransport(config.Endpoint, logger: logger, failPercent: config.SimulatedFailPercent);
            }
        }

        private TrackingEvent CreateEvent(string type, Dictionary<string, object> payload)
        {
            // Privacy mode drops only the person (userId → "anonymous"); sessionId + all device context stay.
            string userId = m_privacyMode ? PRIVACY_ANONYMOUS_USER_ID : m_config.UserId;
            return new TrackingEvent(
                id: Guid.NewGuid().ToString("N"),
                type: type,
                timestampUtc: m_clock.UtcNow,
                sessionId: m_sessionId,
                userId: userId,
                sdkVersion: TrackingSdk.VERSION,
                platform: m_runtime.Platform,
                appVersion: m_runtime.AppVersion,
                deviceModel: m_runtime.DeviceModel,
                osVersion: m_runtime.OsVersion,
                networkType: m_runtime.NetworkType,
                timezone: m_runtime.Timezone,
                locale: m_runtime.Locale,
                bundleId: m_runtime.BundleId,
                payload: payload);
        }

        private void ReloadPersistedBacklog()
        {
            try
            {
                IReadOnlyList<TrackingEvent> persisted = m_store.Load();
                if (persisted == null) return;
                int dropped = 0;
                for (int i = 0; i < persisted.Count; i++)
                {
                    // Mirror the live Enqueue accounting so a backlog larger than the queue (rejected, or an
                    // earlier reloaded event evicted under DropOldest) isn't dropped silently on reload.
                    if (!m_queue.TryEnqueue(new QueuedEvent { Event = persisted[i], Completion = null }, out QueuedEvent evicted)
                        || evicted != null)
                    {
                        m_metrics.IncDropped();
                        dropped++;
                    }
                }
                if (dropped > 0)
                {
                    m_logger.Log(
                        TrackingLogLevel.Warning,
                        $"persisted backlog ({persisted.Count}) exceeded queue capacity; dropped {dropped} event(s) on reload");
                }
            }
            catch (Exception e)
            {
                m_logger.Log(TrackingLogLevel.Error, "reloading persisted backlog failed", e);
            }
        }

        public void Dispose()
        {
            // Reject any concurrent/late enqueue before teardown, so nothing lands in a dead queue.
            lock (m_lifecycleGate)
            {
                m_disposed = true;
            }
            m_dispatcher.Dispose();
            // Only dispose a transport we own once the worker has actually stopped; tearing it down under a
            // still-running worker (one that ignored cancellation) would fault its in-flight send.
            if (m_dispatcher.WorkerStopped)
            {
                m_ownedTransport?.Dispose();
            }
        }
    }
}
