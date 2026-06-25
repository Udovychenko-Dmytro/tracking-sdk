using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DmytroUdovychenko.Tracking.Tests
{
    /// <summary>
    /// Fake <see cref="HttpMessageHandler"/> that captures the outgoing request and returns a canned
    /// status — lets the HTTP transport's serialize → POST → status-mapping path be tested with no
    /// real network. Set <see cref="ThrowOnSend"/> to simulate a timeout / transport failure.
    /// </summary>
    internal sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public Exception ThrowOnSend { get; set; }
        public int CallCount { get; private set; }
        public string LastBody { get; private set; }
        public string LastUrl { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastUrl = request.RequestUri?.ToString();
            LastBody = request.Content != null
                ? await request.Content.ReadAsStringAsync().ConfigureAwait(false)
                : null;
            if (ThrowOnSend != null)
            {
                throw ThrowOnSend;
            }
            return new HttpResponseMessage(StatusCode);
        }
    }
}
