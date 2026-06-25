using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DmytroUdovychenko.Tracking.Tests
{
    /// <summary>
    /// <see cref="IDelayer"/> that records every requested delay and returns immediately — so retry
    /// backoff can be asserted without any real waiting. Honours cancellation.
    /// </summary>
    internal sealed class RecordingDelayer : IDelayer
    {
        public List<TimeSpan> Delays { get; } = new List<TimeSpan>();

        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(duration);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// <see cref="IDelayer"/> that cancels the supplied source the first time it is asked to wait,
    /// then throws — used to deterministically exercise "cancelled during backoff".
    /// </summary>
    internal sealed class CancelOnFirstDelay : IDelayer
    {
        private readonly CancellationTokenSource m_cts;

        public CancelOnFirstDelay(CancellationTokenSource cts)
        {
            m_cts = cts;
        }

        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            m_cts.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
