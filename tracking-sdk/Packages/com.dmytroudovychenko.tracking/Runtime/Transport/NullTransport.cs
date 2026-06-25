using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// No-op transport that silently accepts every batch. Used as the safe default when a
    /// <see cref="TrackingSystem"/> is constructed without an explicit transport, so the public API is
    /// always usable even before a real transport is configured.
    /// </summary>
    public sealed class NullTransport : ITransport
    {
        public static readonly NullTransport Instance = new NullTransport();

        private NullTransport() { }

        public Task<bool> SendAsync(IReadOnlyList<TrackingEvent> batch, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
