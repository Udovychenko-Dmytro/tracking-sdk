using System.Threading;
using System.Threading.Tasks;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Asynchronous reachability check for a specific destination — distinct from <see cref="IConnectivity"/>'s
    /// cached interface snapshot. <c>Tracker.InitAsync</c> / <c>Tracker.IsServerReachableAsync</c> use it to
    /// confirm the tracking server actually answers before bringing the pipeline up.
    /// </summary>
    public interface IConnectivityProbe
    {
        /// <summary>Resolves <c>true</c> when <paramref name="endpoint"/> is reachable (the server returns any
        /// HTTP response); <c>false</c> when offline or the server does not respond. Never throws.</summary>
        Task<bool> IsReachableAsync(string endpoint, CancellationToken cancellationToken = default);
    }
}
