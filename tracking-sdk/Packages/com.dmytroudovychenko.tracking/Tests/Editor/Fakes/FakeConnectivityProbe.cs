using System.Threading;
using System.Threading.Tasks;

namespace DmytroUdovychenko.Tracking.Tests
{
    /// <summary>Deterministic <see cref="IConnectivityProbe"/> for InitAsync/IsServerReachableAsync tests — returns
    /// a settable result, records the pinged endpoint, and counts calls (to assert it was or wasn't consulted).</summary>
    internal sealed class FakeConnectivityProbe : IConnectivityProbe
    {
        public bool Reachable { get; set; } = true;
        public int CallCount { get; private set; }
        public string LastEndpoint { get; private set; }

        public Task<bool> IsReachableAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastEndpoint = endpoint;
            return Task.FromResult(Reachable);
        }
    }
}
