using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    /// <summary>
    /// Live end-to-end test against the deployed PHP receiver at <see cref="TestConstants.LIVE_ENDPOINT"/>.
    /// Runs as part of the default suite — it always POSTs to the real server, so the run needs network.
    /// </summary>
    public class LiveTransportTests
    {
        [Test, Category("Live")]
        public void Posts_RealBatch_AndServerAccepts()
        {
            using (HttpTransport transport = new HttpTransport(TestConstants.LIVE_ENDPOINT, logger: NullTrackingLogger.Instance))
            {
                bool ok = transport.SendAsync(new[]
                {
                    TestEvents.Message("live-smoke-1"),
                    TestEvents.Message("live-smoke-2"),
                }).GetAwaiter().GetResult();

                Assert.IsTrue(ok, "the live endpoint should accept the batch with a 2xx response");
            }
        }
    }
}
