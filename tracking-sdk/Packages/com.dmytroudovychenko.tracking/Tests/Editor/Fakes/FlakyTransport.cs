using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DmytroUdovychenko.Tracking.Tests
{
    /// <summary>
    /// Transport that fails the first <c>failuresBeforeSuccess</c> sends, then succeeds. Pass a large
    /// number to model a permanently-down server. Records how many times it was called.
    /// </summary>
    internal sealed class FlakyTransport : ITransport
    {
        private int m_failuresRemaining;

        public int SendCount { get; private set; }

        public FlakyTransport(int failuresBeforeSuccess)
        {
            m_failuresRemaining = failuresBeforeSuccess;
        }

        public Task<bool> SendAsync(IReadOnlyList<TrackingEvent> batch, CancellationToken cancellationToken = default)
        {
            SendCount++;
            if (m_failuresRemaining > 0)
            {
                m_failuresRemaining--;
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }
    }
}
