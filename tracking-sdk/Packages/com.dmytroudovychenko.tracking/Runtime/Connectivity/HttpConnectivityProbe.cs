using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Server-reachability probe: fast-fails on interface state, then pings the target endpoint with a HEAD
    /// request. "Reachable" means the server returned <em>any</em> HTTP response (even 4xx like 405) — that
    /// proves DNS + TCP + the host are alive; only a transport error / timeout counts as "not responding".
    /// </summary>
    /// <remarks>
    /// Built on <see cref="HttpClient"/> (thread-agnostic), not <c>UnityWebRequest</c> (main-thread-only) —
    /// matching <see cref="HttpTransport"/>. A fake <see cref="HttpMessageHandler"/> can be injected via
    /// <paramref name="client"/> to test the probe with no real network. Transient 5xx is deliberately
    /// treated as "reachable": the delivery retry / circuit-breaker handles those, so Init isn't blocked.
    /// </remarks>
    public sealed class HttpConnectivityProbe : IConnectivityProbe, IDisposable
    {
        private readonly IConnectivity m_connectivity;
        private readonly HttpClient m_client;
        private readonly ITrackingLogger m_logger;
        private readonly bool m_ownsClient;

        public HttpConnectivityProbe(
            IConnectivity connectivity,
            TimeSpan? timeout = null,
            ITrackingLogger logger = null,
            HttpClient client = null)
        {
            m_connectivity = connectivity ?? AlwaysOnlineConnectivity.Instance;
            m_logger = logger ?? NullTrackingLogger.Instance;

            if (client != null)
            {
                m_client = client;
                m_ownsClient = false;
            }
            else
            {
                m_client = new HttpClient { Timeout = timeout ?? TrackingConfig.DEFAULT_CONNECTIVITY_PROBE_TIMEOUT };
                m_ownsClient = true;
            }
        }

        public async Task<bool> IsReachableAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            // Stage 1 — interface fast-fail: no point hitting the network when it's known down.
            // Refresh on the calling (main) thread; the cast is null for fakes / AlwaysOnline.
            (m_connectivity as UnityConnectivity)?.Refresh();
            if (!m_connectivity.IsOnline)
            {
                m_logger.Log(TrackingLogLevel.Error, "Connectivity: no internet (network interface unreachable).");
                return false;
            }
            m_logger.Log(TrackingLogLevel.Debug, "Connectivity: device online (network interface reachable).");

            // Stage 2 — server ping: any HTTP response means the destination answered (DNS/TCP/host up).
            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, endpoint))
                using (await m_client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    m_logger.Log(TrackingLogLevel.Debug, $"Connectivity: server '{endpoint}' reachable (responded).");
                    return true;
                }
            }
            catch (Exception e)
            {
                m_logger.Log(TrackingLogLevel.Error, $"Connectivity: server '{endpoint}' did not respond: {e.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (m_ownsClient)
            {
                m_client.Dispose();
            }
        }
    }
}
