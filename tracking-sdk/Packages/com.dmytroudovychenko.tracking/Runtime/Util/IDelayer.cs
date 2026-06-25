using System;
using System.Threading;
using System.Threading.Tasks;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Abstraction over "wait for a duration". The retry loop awaits this between attempts; injecting a
    /// fake lets tests verify backoff <em>without real delays</em> (the fake completes immediately and
    /// records what it was asked to wait).
    /// </summary>
    public interface IDelayer
    {
        Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken);
    }

    /// <summary>Production <see cref="IDelayer"/> backed by <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
    public sealed class TaskDelayer : IDelayer
    {
        public static readonly TaskDelayer Instance = new TaskDelayer();

        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            if (duration <= TimeSpan.Zero) return Task.CompletedTask;
            return Task.Delay(duration, cancellationToken);
        }
    }
}
