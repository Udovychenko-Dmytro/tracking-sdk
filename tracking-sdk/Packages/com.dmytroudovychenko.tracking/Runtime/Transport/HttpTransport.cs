using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Real HTTP transport: POSTs the JSON-serialized batch to the configured endpoint.
    /// </summary>
    /// <remarks>
    /// Built on <see cref="HttpClient"/> rather than <c>UnityWebRequest</c> because the delivery worker
    /// runs on a background thread and <c>UnityWebRequest</c> is main-thread-only; <see cref="HttpClient"/>
    /// is thread-safe and thread-agnostic. (WebGL is the exception — it has no socket stack, so a WebGL
    /// build would supply a <c>UnityWebRequest</c>-based transport that marshals to the main thread.)
    /// <para>For tests, a fake <see cref="HttpMessageHandler"/> can be injected via <paramref name="client"/>
    /// so the serialize → POST → status-mapping path is verified without any real network.</para>
    /// </remarks>
    public sealed class HttpTransport : ITransport, IDisposable
    {
        private readonly string m_endpoint;
        private readonly HttpClient m_client;
        private readonly ITrackingLogger m_logger;
        private readonly bool m_ownsClient;

        public HttpTransport(
            string endpoint,
            TimeSpan? timeout = null,
            ITrackingLogger logger = null,
            HttpClient client = null)
        {
            m_endpoint = string.IsNullOrEmpty(endpoint) ? TrackingConfig.DEFAULT_ENDPOINT : endpoint;
            m_logger = logger ?? NullTrackingLogger.Instance;

            if (client != null)
            {
                m_client = client;
                m_ownsClient = false;
            }
            else
            {
                m_client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
                m_ownsClient = true;
            }
        }

        public async Task<bool> SendAsync(IReadOnlyList<TrackingEvent> batch, CancellationToken cancellationToken = default)
        {
            if (batch == null || batch.Count == 0) return true;

            try
            {
                string json = EventSerializer.ToJson(batch);
                using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    using (HttpResponseMessage response = await m_client.PostAsync(m_endpoint, content, cancellationToken).ConfigureAwait(false))
                    {
                        if (response.IsSuccessStatusCode) return true;

                        m_logger.Log(TrackingLogLevel.Warning, $"POST {m_endpoint} failed: HTTP {(int)response.StatusCode}");
                        return false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception e)
            {
                m_logger.Log(TrackingLogLevel.Warning, $"POST {m_endpoint} threw: {e.Message}");
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
