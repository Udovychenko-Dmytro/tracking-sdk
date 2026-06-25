using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Background worker that pulls events off the <see cref="EventQueue"/>, groups them into batches
    /// (by size or elapsed time), and delivers each batch through the <see cref="ITransport"/> with
    /// retries — keeping all I/O off the caller's thread.
    /// </summary>
    /// <remarks>
    /// Sending is gated by connectivity and a circuit breaker (don't burn retries while offline or
    /// while a server is known-down). Events that exhaust their retries are moved to the dead-letter
    /// sink rather than dropped. All timing decisions use the injected <see cref="IClock"/>, so the
    /// pipeline is deterministically testable by advancing a virtual clock and calling
    /// <see cref="PumpOnceAsync"/> directly instead of starting the real worker thread.
    /// </remarks>
    internal sealed class EventDispatcher : IDisposable
    {
        private const int MAX_IDLE_WAIT_MS = 1000;

        private readonly EventQueue m_queue;
        private readonly ITransport m_transport;
        private readonly IClock m_clock;
        private readonly TrackingConfig m_config;
        private readonly RetryPolicy m_retryPolicy;
        private readonly IDelayer m_delayer;
        private readonly TrackingMetrics m_metrics;
        private readonly IConnectivity m_connectivity;
        private readonly CircuitBreaker m_breaker;
        private readonly IDeadLetterSink m_deadLetter;
        private readonly ITrackingLogger m_logger;
        private readonly SemaphoreSlim m_signal = new SemaphoreSlim(0);
        // Serializes batch delivery so only one batch is ever in flight, even when a caller's
        // FlushAsync drains concurrently with the background worker (single owner of I/O).
        private readonly SemaphoreSlim m_sendGate = new SemaphoreSlim(1, 1);

        private CancellationTokenSource m_cts;
        private Task m_worker;
        private int m_started;
        // The batch currently in transport I/O. Tracked so Dispose can fail its awaiters if a misbehaving
        // transport ignores cancellation and outlasts the shutdown drain (in-flight events never strand).
        private volatile List<QueuedEvent> m_activeBatch;

        // Whether the worker had actually stopped by the end of Dispose — the owner reads it to decide
        // whether disposing the transport out from under the worker is safe.
        internal bool WorkerStopped { get; private set; } = true;

        public EventDispatcher(
            EventQueue queue,
            ITransport transport,
            IClock clock,
            TrackingConfig config,
            RetryPolicy retryPolicy = null,
            IDelayer delayer = null,
            TrackingMetrics metrics = null,
            IConnectivity connectivity = null,
            CircuitBreaker breaker = null,
            IDeadLetterSink deadLetter = null,
            ITrackingLogger logger = null)
        {
            m_queue = queue;
            m_transport = transport ?? NullTransport.Instance;
            m_clock = clock ?? SystemClock.Instance;
            m_config = config ?? new TrackingConfig();
            m_retryPolicy = retryPolicy ??
                new RetryPolicy(m_config.MaxRetryAttempts, m_config.InitialRetryDelay, m_config.MaxRetryDelay);
            m_delayer = delayer ?? TaskDelayer.Instance;
            m_metrics = metrics ?? new TrackingMetrics();
            m_connectivity = connectivity ?? AlwaysOnlineConnectivity.Instance;
            m_breaker = breaker;
            m_deadLetter = deadLetter;
            m_logger = logger ?? NullTrackingLogger.Instance;
        }

        /// <summary>Wakes the worker to re-evaluate the queue (called after each enqueue).</summary>
        public void Signal()
        {
            try
            {
                if (m_signal.CurrentCount == 0)
                {
                    m_signal.Release();
                }
            }
            catch (SemaphoreFullException) { /* already signalled */ }
        }

        /// <summary>Starts the background worker. Idempotent.</summary>
        public void Start()
        {
            if (Interlocked.Exchange(ref m_started, 1) == 1) return;
            m_cts = new CancellationTokenSource();
            m_worker = Task.Run(() => RunAsync(m_cts.Token));
        }

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await PumpOnceAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e) { m_logger.Log(TrackingLogLevel.Error, "dispatcher pump failed", e); }

                try
                {
                    await m_signal.WaitAsync(NextWaitMs(), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
            }

            // Bound the final drain so a slow ONLINE backlog can't outlast Dispose's wait and keep the
            // worker running against transport/primitives that are about to be disposed.
            TimeSpan drainBudget = m_config.ShutdownDrainTimeout;
            if (drainBudget < TimeSpan.Zero) drainBudget = TimeSpan.Zero;
            using (CancellationTokenSource drainCts = new CancellationTokenSource(drainBudget))
            {
                try { await DrainAsync(drainCts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { /* drain budget elapsed at shutdown */ }
                catch (Exception e) { m_logger.Log(TrackingLogLevel.Error, "dispatcher final drain failed", e); }
            }

            // Anything still buffered could not be delivered (offline / breaker-open / drain budget elapsed
            // at shutdown); fail its awaiter so a SendMapAsync caller never hangs on a never-completed Task.
            FailRemainingAwaiters();
        }

        private void FailRemainingAwaiters()
        {
            // Fail the in-flight batch too (a transport that outlasted the drain still holds it), not just
            // what is left in the queue — otherwise its awaiters would hang on a never-completed Task.
            List<QueuedEvent> active = m_activeBatch;
            if (active != null)
            {
                for (int i = 0; i < active.Count; i++)
                {
                    active[i].Completion?.TrySetResult(false);
                }
            }

            List<QueuedEvent> remaining = m_queue.RemoveAll();
            for (int i = 0; i < remaining.Count; i++)
            {
                remaining[i].Completion?.TrySetResult(false);
            }
        }

        private int NextWaitMs()
        {
            // While we cannot send (offline / breaker open) back off a fixed amount instead of
            // busy-spinning on overdue events.
            if (!CanSend()) return MAX_IDLE_WAIT_MS;

            if (m_queue.TryPeekOldest(out QueuedEvent oldest))
            {
                DateTimeOffset due = oldest.Event.TimestampUtc + m_config.FlushInterval;
                double ms = (due - m_clock.UtcNow).TotalMilliseconds;
                if (ms < 0)
                {
                    ms = 0;
                }
                if (ms > MAX_IDLE_WAIT_MS)
                {
                    ms = MAX_IDLE_WAIT_MS;
                }
                return (int)ms;
            }
            return MAX_IDLE_WAIT_MS;
        }

        private bool CanSend()
        {
            if (!m_connectivity.IsOnline) return false;
            if (m_breaker != null && !m_breaker.AllowRequest()) return false;
            return true;
        }

        // Guards verbose-only logs so their serialization is skipped when below the configured MinLogLevel.
        private bool ShouldLog(TrackingLogLevel level) => level >= m_config.MinLogLevel;

        /// <summary>Sends every batch currently <em>due</em> (by size or elapsed time), then returns.</summary>
        public async Task PumpOnceAsync(CancellationToken ct = default)
        {
            while (!ct.IsCancellationRequested && CanSend() && ShouldFlush())
            {
                List<QueuedEvent> batch = m_queue.DequeueBatch(m_config.BatchSize);
                if (batch.Count == 0) break;
                await SendBatchAsync(batch, ct).ConfigureAwait(false);
            }
        }

        /// <summary>Sends everything currently queued, ignoring size/time thresholds (flush / shutdown).</summary>
        public async Task DrainAsync(CancellationToken ct = default)
        {
            while (!ct.IsCancellationRequested && CanSend())
            {
                List<QueuedEvent> batch = m_queue.DequeueBatch(m_config.BatchSize);
                if (batch.Count == 0) break;
                await SendBatchAsync(batch, ct).ConfigureAwait(false);
            }
        }

        private bool ShouldFlush()
        {
            int count = m_queue.Count;
            if (count == 0) return false;
            if (count >= m_config.BatchSize) return true;
            if (m_queue.TryPeekOldest(out QueuedEvent oldest))
            {
                if (m_clock.UtcNow - oldest.Event.TimestampUtc >= m_config.FlushInterval) return true;
            }
            return false;
        }

        private async Task SendBatchAsync(List<QueuedEvent> batch, CancellationToken ct)
        {
            try
            {
                await m_sendGate.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
            {
                // Cancelled, or the gate was disposed during shutdown, before we acquired the send slot:
                // the batch is already out of the queue, so fail its awaiters here rather than strand them.
                for (int i = 0; i < batch.Count; i++)
                {
                    batch[i].Completion?.TrySetResult(false);
                }
                return;
            }

            try
            {
                await SendBatchCoreAsync(batch, ct).ConfigureAwait(false);
            }
            finally
            {
                try { m_sendGate.Release(); } catch (ObjectDisposedException) { /* disposed during shutdown */ }
            }
        }

        private async Task SendBatchCoreAsync(List<QueuedEvent> batch, CancellationToken ct)
        {
            m_activeBatch = batch;
            List<TrackingEvent> events = new List<TrackingEvent>(batch.Count);
            for (int i = 0; i < batch.Count; i++)
            {
                events.Add(batch[i].Event);
            }

            // Show the exact wire payload once per batch (before retries) when tracing at Debug.
            if (ShouldLog(TrackingLogLevel.Debug))
            {
                m_logger.Log(TrackingLogLevel.Debug,
                    $"sending {events.Count} event(s) to {m_config.Endpoint}: {EventSerializer.ToJson(events)}");
            }

            bool ok = false;
            int attempt = 0;

            // A half-open breaker permits a single trial batch; don't burn the whole retry budget
            // hammering a server that is only being probed for recovery.
            bool halfOpenTrial = m_breaker != null && m_breaker.State == CircuitState.HalfOpen;

            while (!ct.IsCancellationRequested)
            {
                attempt++;
                if (attempt > 1)
                {
                    m_metrics.IncRetried();
                }

                try
                {
                    ok = await m_transport.SendAsync(events, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    ok = false;
                    break;
                }
                catch (Exception e)
                {
                    m_logger.Log(TrackingLogLevel.Error, "transport send threw", e);
                    ok = false;
                }

                if (ok) break;
                if (halfOpenTrial) break; // single probe; a failure re-opens the breaker below.

                // Exponential backoff + jitter; the same idempotency id is reused so the server can dedupe.
                if (!m_retryPolicy.TryGetDelay(attempt, out TimeSpan delay)) break;

                try
                {
                    await m_delayer.DelayAsync(delay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            bool cancelled = !ok && ct.IsCancellationRequested;

            try
            {
                if (ok)
                {
                    m_breaker?.RecordSuccess();
                    m_metrics.AddSent(batch.Count);
                    if (ShouldLog(TrackingLogLevel.Info))
                    {
                        m_logger.Log(TrackingLogLevel.Info, $"delivered {batch.Count} event(s) to {m_config.Endpoint}");
                    }
                }
                else if (!cancelled)
                {
                    // Genuine give-up (retries exhausted), not a shutdown.
                    m_breaker?.RecordFailure();
                    m_metrics.AddGivenUp(batch.Count);
                    if (m_deadLetter != null)
                    {
                        try
                        {
                            m_deadLetter.DeadLetter(events);
                            m_metrics.AddDeadLettered(batch.Count);
                            m_logger.Log(TrackingLogLevel.Warning, $"dead-lettered {batch.Count} event(s) after exhausting retries");
                        }
                        catch (Exception e)
                        {
                            // A throwing custom sink must not strand the batch's awaiters.
                            m_logger.Log(TrackingLogLevel.Error, "dead-letter sink threw", e);
                        }
                    }
                }
            }
            finally
            {
                for (int i = 0; i < batch.Count; i++)
                {
                    batch[i].Completion?.TrySetResult(ok);
                }
                m_activeBatch = null;
            }
        }

        public void Dispose()
        {
            int shutdownWaitMs = (int)m_config.ShutdownDrainTimeout.TotalMilliseconds;
            if (shutdownWaitMs < 0) shutdownWaitMs = 0;
            try { m_cts?.Cancel(); } catch { }
            try { if (m_signal.CurrentCount == 0) m_signal.Release(); } catch { }

            bool workerStopped = true;
            try { workerStopped = m_worker == null || m_worker.Wait(shutdownWaitMs); } catch { workerStopped = false; }
            WorkerStopped = workerStopped;

            // Backstop: unblock any awaiter the worker didn't resolve — never started (test mode), or its
            // final drain timed out. Idempotent: if the worker already drained, the queue is empty here.
            FailRemainingAwaiters();

            // Only release the sync primitives once the worker has actually stopped; disposing them under a
            // still-running worker would surface ObjectDisposedException on its next await. They hold no
            // unmanaged resource here, so leaving them for GC when a transport refuses to cancel is safe.
            if (workerStopped)
            {
                try { m_cts?.Dispose(); } catch { }
                try { m_signal.Dispose(); } catch { }
                try { m_sendGate.Dispose(); } catch { }
            }
        }
    }
}
