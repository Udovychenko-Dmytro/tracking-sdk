using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Abstraction over "sending a batch of events to a server". This is the primary test seam:
    /// fakes stand in for the network so batching, retries and backoff are fully deterministic.
    /// Real implementations (simulated + live HTTP) arrive in Phase 2 / Phase 6.
    /// </summary>
    public interface ITransport
    {
        /// <summary>
        /// Attempts to deliver a batch of events.
        /// </summary>
        /// <returns><c>true</c> if the batch was accepted by the server; <c>false</c> on a failure
        /// the caller may retry.</returns>
        Task<bool> SendAsync(IReadOnlyList<TrackingEvent> batch, CancellationToken cancellationToken = default);
    }
}
