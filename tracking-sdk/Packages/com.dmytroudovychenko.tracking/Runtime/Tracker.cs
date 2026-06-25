using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Static front door to the SDK: <c>Tracker.Init(userId)</c> once, then <c>Tracker.SendMessage(...)</c> —
    /// no instance to hold or pass around.
    /// </summary>
    /// <remarks>
    /// A thin facade over the DI'd <see cref="TrackingSystem"/>, never a replacement — the instance core
    /// stays public for tests/advanced DI and multiple trackers. Design + edge-case policy (call-before-Init,
    /// double-Init, lifecycle, domain-reload reset): KnowledgeBase/Documentation/STATIC_FACADE.md.
    /// </remarks>
    public static class Tracker
    {
        private static readonly object m_gate = new object();
        private static volatile TrackingSystem m_instance;
        private static TrackingLifecycle m_lifecycle;
        private static bool m_lifecycleWired;
        private static int m_notInitWarned;
        // Shared server-reachability probe for InitAsync/IsServerReachableAsync; HttpClient is process-lifetime by design.
        private static IConnectivityProbe m_defaultProbe;

        /// <summary>Whether a tracker has been initialized and is accepting calls.</summary>
        public static bool IsInitialized => m_instance != null;

        /// <summary>The underlying global tracker, or <c>null</c> before <see cref="Init(string)"/> (advanced/testing).</summary>
        public static TrackingSystem Current => m_instance;

        /// <summary>Whether tracking is currently accepting events (<c>false</c> before <see cref="Init(string)"/>).</summary>
        public static bool IsEnabled => m_instance?.IsEnabled ?? false;

        /// <summary>Whether anonymous (privacy) mode is on (<c>false</c> before <see cref="Init(string)"/>).</summary>
        public static bool IsPrivacyMode => m_instance?.IsPrivacyMode ?? false;

        /// <summary>Live diagnostic counters (all-zero before <see cref="Init(string)"/>).</summary>
        public static TrackingMetricsSnapshot Metrics => m_instance?.Metrics ?? default;

        /// <summary>Events that exhausted their retries, or <c>null</c> before <see cref="Init(string)"/>.</summary>
        public static IDeadLetterSink DeadLetter => m_instance?.DeadLetter;

        /// <summary>Stable id shared by every event of the current session, or <c>null</c> before <see cref="Init(string)"/>.</summary>
        public static string SessionId => m_instance?.SessionId;

        /// <summary>The user every event is attributed to, or <c>null</c> before <see cref="Init(string)"/>.</summary>
        public static string UserId => m_instance?.UserId;

        /// <summary>Initializes the global tracker for <paramref name="userId"/> against the default endpoint (simulated).
        /// Lower <paramref name="minLogLevel"/> (e.g. <c>Debug</c>) to trace each step and event payload through the logger.</summary>
        public static void Init(string userId, TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)
        {
            lock (m_gate)
            {
                if (AlreadyInitialized())
                {
                    return;
                }
                AdoptIfBuilt(TrackingSystem.Init(userId, minLogLevel));
            }
        }

        /// <summary>Initializes the global tracker for <paramref name="userId"/> against a named <paramref name="server"/>.
        /// Lower <paramref name="minLogLevel"/> (e.g. <c>Debug</c>) to trace each step and event payload through the logger.</summary>
        public static void Init(string userId, ServerEnvironment server, TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)
        {
            lock (m_gate)
            {
                if (AlreadyInitialized()) return;
                AdoptIfBuilt(TrackingSystem.Init(userId, server, minLogLevel));
            }
        }

        /// <summary>Initializes the global tracker for <paramref name="userId"/> against a custom <paramref name="endpoint"/> (real HTTP).
        /// Lower <paramref name="minLogLevel"/> (e.g. <c>Debug</c>) to trace each step and event payload through the logger.</summary>
        public static void Init(string userId, string endpoint, TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)
        {
            lock (m_gate)
            {
                if (AlreadyInitialized()) return;
                AdoptIfBuilt(TrackingSystem.Init(userId, endpoint, minLogLevel));
            }
        }

        /// <summary>
        /// Like <see cref="Init(string, ServerEnvironment)"/>, but first confirms the target server is reachable
        /// (interface check, then a HEAD ping) when a live server is targeted; if not, nothing is initialized.
        /// <c>FakeServer</c> skips the check.
        /// </summary>
        /// <returns><c>true</c> if a tracker is now initialized (including already-initialized); <c>false</c> if
        /// there is no internet or the server did not respond (the reason is logged).</returns>
        /// <remarks>Lower <paramref name="minLogLevel"/> (e.g. <c>Debug</c>) to trace the initialized tracker's
        /// pipeline; the pre-init reachability probe keeps its own always-on diagnostics.</remarks>
        public static Task<bool> InitAsync(string userId, ServerEnvironment server,
            CancellationToken cancellationToken = default, TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)
            => InitAsync(userId, server, DefaultProbe(), cancellationToken, minLogLevel);

        /// <summary>Like <see cref="Init(string, string)"/>, but first confirms the custom <paramref name="endpoint"/>
        /// is reachable; if not, nothing is initialized.</summary>
        /// <returns><c>true</c> if a tracker is now initialized; <c>false</c> if the server was unreachable.</returns>
        /// <remarks>Lower <paramref name="minLogLevel"/> (e.g. <c>Debug</c>) to trace the initialized tracker's pipeline.</remarks>
        public static Task<bool> InitAsync(string userId, string endpoint,
            CancellationToken cancellationToken = default, TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)
            => InitAsync(userId, endpoint, DefaultProbe(), cancellationToken, minLogLevel);

        /// <summary>Checks whether <paramref name="endpoint"/> is reachable (interface up + the server answers a
        /// HEAD ping with any HTTP status). Gate your own Init with it, or rely on <see cref="InitAsync(string,
        /// ServerEnvironment, CancellationToken)"/> which calls it for live servers.</summary>
        public static Task<bool> IsServerReachableAsync(string endpoint, CancellationToken cancellationToken = default)
            => IsServerReachableAsync(endpoint, DefaultProbe(), cancellationToken);

        // Probe-injecting cores (tests pass a fake IConnectivityProbe; the public methods pass the shared probe).
        // NOTE: no ConfigureAwait(false) on the probe await — the continuation builds Unity objects
        // (UnityConnectivity, the lifecycle GameObject), which are main-thread-only; capturing the Unity
        // SynchronizationContext resumes init on the main thread. (The probe itself runs off-thread.)
        internal static async Task<bool> InitAsync(
            string userId, ServerEnvironment server, IConnectivityProbe probe, CancellationToken cancellationToken,
            TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)
        {
            if (TrackingSystem.TransportModeFor(server) == TransportMode.Http)
            {
                // The probe logs each step (online/reachable at Debug, failure at Error).
                if (!await IsServerReachableAsync(TrackingConfig.EndpointFor(server), probe, cancellationToken))
                {
                    return false;
                }
            }
            else
            {
                UnityTrackingLogger.Instance.Log(
                    TrackingLogLevel.Debug, $"Init: simulated server ({server}) — skipping reachability probe.");
            }
            bool initialized = AdoptUnderGate(() => TrackingSystem.Init(userId, server, minLogLevel));
            UnityTrackingLogger.Instance.Log(
                TrackingLogLevel.Debug, $"Init: {(initialized ? "succeeded" : "failed")} (server {server}).");
            return initialized;
        }

        internal static async Task<bool> InitAsync(
            string userId, string endpoint, IConnectivityProbe probe, CancellationToken cancellationToken,
            TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)
        {
            // The probe logs each step (online/reachable at Debug, failure at Error).
            if (!await IsServerReachableAsync(endpoint, probe, cancellationToken))
            {
                return false;
            }
            bool initialized = AdoptUnderGate(() => TrackingSystem.Init(userId, endpoint, minLogLevel));
            UnityTrackingLogger.Instance.Log(
                TrackingLogLevel.Debug, $"Init: {(initialized ? "succeeded" : "failed")} (endpoint '{endpoint}').");
            return initialized;
        }

        internal static async Task<bool> IsServerReachableAsync(
            string endpoint, IConnectivityProbe probe, CancellationToken cancellationToken)
        {
            if (probe == null)
            {
                return false;
            }
            try
            {
                return await probe.IsReachableAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // Error isolation: a misbehaving probe must never throw into caller code — treat as unreachable.
                UnityTrackingLogger.Instance.Log(TrackingLogLevel.Warning, "Tracker connectivity probe threw; treating as unreachable", e);
                return false;
            }
        }

        /// <summary>
        /// Adopts a pre-built <paramref name="tracker"/> as the global instance (advanced / DI / tests).
        /// </summary>
        /// <param name="attachLifecycle">When <c>true</c> (default) auto-wires lifecycle persistence and
        /// shutdown-on-quit; tests pass <c>false</c> to avoid touching the Unity scene/quit events.</param>
        public static void Init(TrackingSystem tracker, bool attachLifecycle = true)
        {
            if (tracker == null)
            {
                throw new ArgumentNullException(nameof(tracker));
            }
            lock (m_gate)
            {
                if (AlreadyInitialized())
                {
                    // Already initialized: dispose the rejected pre-built tracker so its worker/HttpClient
                    // don't leak (the caller can't tell it wasn't adopted). Never the live instance.
                    if (!ReferenceEquals(tracker, m_instance))
                    {
                        tracker.Dispose();
                    }
                    return;
                }
                Adopt(tracker, attachLifecycle);
            }
        }

        /// <summary>Records a single message event. No-op returning <c>false</c> before <see cref="Init(string)"/>.</summary>
        public static bool SendMessage(string message)
        {
            TrackingSystem tracker = m_instance;
            if (tracker == null)
            {
                WarnNotInitialized(nameof(SendMessage));
                return false;
            }
            return tracker.SendMessage(message);
        }

        /// <summary>Records a structured event. Resolves <c>false</c> (never hangs) before <see cref="Init(string)"/>.</summary>
        public static Task<bool> SendMapAsync(Dictionary<string, object> map)
        {
            TrackingSystem tracker = m_instance;
            if (tracker == null)
            {
                WarnNotInitialized(nameof(SendMapAsync));
                return Task.FromResult(false);
            }
            return tracker.SendMapAsync(map);
        }

        /// <summary>Forces delivery of everything buffered. Completed no-op before <see cref="Init(string)"/>.</summary>
        public static Task FlushAsync()
        {
            TrackingSystem tracker = m_instance;
            if (tracker == null)
            {
                WarnNotInitialized(nameof(FlushAsync));
                return Task.CompletedTask;
            }
            return tracker.FlushAsync();
        }

        /// <summary>Snapshots buffered events to durable storage. No-op before <see cref="Init(string)"/>.</summary>
        public static void Persist()
        {
            TrackingSystem tracker = m_instance;
            if (tracker == null)
            {
                WarnNotInitialized(nameof(Persist));
                return;
            }
            tracker.Persist();
        }

        /// <summary>Enables/disables tracking (disabling purges buffered data). No-op before <see cref="Init(string)"/>.</summary>
        public static void SetEnabled(bool enabled)
        {
            TrackingSystem tracker = m_instance;
            if (tracker == null)
            {
                WarnNotInitialized(nameof(SetEnabled));
                return;
            }
            tracker.SetEnabled(enabled);
        }

        /// <summary>Turns anonymous (privacy) mode on/off — userId becomes <c>"anonymous"</c>. No-op before <see cref="Init(string)"/>.</summary>
        public static void SetPrivacyMode(bool enabled)
        {
            TrackingSystem tracker = m_instance;
            if (tracker == null)
            {
                WarnNotInitialized(nameof(SetPrivacyMode));
                return;
            }
            tracker.SetPrivacyMode(enabled);
        }

        /// <summary>Discards all buffered, dead-lettered, and persisted events. No-op before <see cref="Init(string)"/>.</summary>
        public static void Purge()
        {
            TrackingSystem tracker = m_instance;
            if (tracker == null)
            {
                WarnNotInitialized(nameof(Purge));
                return;
            }
            tracker.Purge();
        }

        /// <summary>
        /// Disposes the global tracker and clears all static state. Safe when not initialized; call it
        /// before a second <see cref="Init(string)"/> to reconfigure (e.g. a new user).
        /// </summary>
        public static void Dispose()
        {
            TrackingSystem tracker;
            lock (m_gate)
            {
                tracker = m_instance;
                m_instance = null;
                if (m_lifecycleWired)
                {
                    Application.quitting -= Dispose;
                    m_lifecycleWired = false;
                }
                try
                {
                    DestroyLifecycle();
                }
                catch (Exception e)
                {
                    // Best-effort: lifecycle teardown must never strand Dispose or escape into the caller.
                    UnityTrackingLogger.Instance.Log(TrackingLogLevel.Error, "Tracker.Dispose lifecycle teardown failed", e);
                }
                Interlocked.Exchange(ref m_notInitWarned, 0);
            }
            // Dispose outside the lock: Dispose may block on the worker's final drain.
            tracker?.Dispose();
        }

        // Domain reload off ("Enter Play Mode Options") leaves statics alive between play sessions; dispose
        // any leftover so the previous run's worker/HttpClient don't leak and each play starts clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnEnterPlayMode() => Dispose();

        private static bool AlreadyInitialized()
        {
            if (m_instance == null)
            {
                return false;
            }
            UnityTrackingLogger.Instance.Log(
                TrackingLogLevel.Warning,
                "Tracker.Init called twice; keeping the existing tracker. Call Tracker.Dispose() before re-initializing.");
            return true;
        }

        // Adopts a factory-built tracker, or stays uninitialized when Init was gated offline (null).
        // The factory already logged the actionable warning, so this path is a silent no-op.
        private static void AdoptIfBuilt(TrackingSystem tracker)
        {
            if (tracker == null)
            {
                return;
            }
            Adopt(tracker, attachLifecycle: true);
        }

        // Builds the tracker under the gate and reports whether a tracker is now live (incl. already-init).
        private static bool AdoptUnderGate(Func<TrackingSystem> factory)
        {
            lock (m_gate)
            {
                if (AlreadyInitialized())
                {
                    return true;
                }
                AdoptIfBuilt(factory());
                return IsInitialized;
            }
        }

        private static IConnectivityProbe DefaultProbe()
        {
            lock (m_gate)
            {
                // Lazily built once on the main thread (UnityConnectivity snapshots reachability in its ctor);
                // logs the unreachable reason through the Unity logger so Init failures are visible.
                return m_defaultProbe ?? (m_defaultProbe =
                    new HttpConnectivityProbe(new UnityConnectivity(), logger: UnityTrackingLogger.Instance));
            }
        }

        private static void Adopt(TrackingSystem tracker, bool attachLifecycle)
        {
            TrackingLifecycle lifecycle = null;
            if (attachLifecycle)
            {
                try
                {
                    lifecycle = TrackingLifecycle.Attach(tracker);
                    Application.quitting += Dispose;
                }
                catch
                {
                    // Roll back partial wiring so a failed Init leaves no global and no leaked worker.
                    Application.quitting -= Dispose;
                    DestroyLifecycleGameObject(lifecycle);
                    tracker.Dispose();
                    throw;
                }
            }
            // Publish the fully-wired global last, so IsInitialized never observes a half-built state.
            m_instance = tracker;
            m_lifecycle = lifecycle;
            m_lifecycleWired = attachLifecycle;
            Interlocked.Exchange(ref m_notInitWarned, 0);
        }

        private static void DestroyLifecycle()
        {
            TrackingLifecycle lifecycle = m_lifecycle;
            m_lifecycle = null;
            DestroyLifecycleGameObject(lifecycle);
        }

        private static void DestroyLifecycleGameObject(TrackingLifecycle lifecycle)
        {
            if (lifecycle == null) return;
            GameObject go = lifecycle.gameObject;
            if (go == null) return; // already torn down (e.g. exiting play mode)
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(go);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void WarnNotInitialized(string op)
        {
            // Warn once per session — these calls land in hot loops (Update), so don't spam.
            if (Interlocked.Exchange(ref m_notInitWarned, 1) != 0) return;
            UnityTrackingLogger.Instance.Log(
                TrackingLogLevel.Warning,
                "Tracker." + op + " called before Init(); the call was ignored. Call Tracker.Init(userId) at startup.");
        }
    }
}
