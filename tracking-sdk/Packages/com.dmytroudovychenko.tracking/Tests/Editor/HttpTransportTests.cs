using System;
using System.Net;
using System.Net.Http;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class HttpTransportTests
    {
        [Test]
        public void Send_PostsJsonBatch_ToEndpoint_AndReturnsTrue_On2xx()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler { StatusCode = HttpStatusCode.OK };
            using (HttpTransport transport = new HttpTransport(TestConstants.FAKE_ENDPOINT_TRACK, client: new HttpClient(handler)))
            {
                bool ok = transport.SendAsync(new[] { TestEvents.Message("hello") }).GetAwaiter().GetResult();

                Assert.IsTrue(ok);
                Assert.AreEqual(1, handler.CallCount);
                StringAssert.Contains("hello", handler.LastBody);
                StringAssert.Contains("track.php", handler.LastUrl);
            }
        }

        [Test]
        public void Send_ReturnsFalse_OnServerError()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler { StatusCode = HttpStatusCode.InternalServerError };
            using (HttpTransport transport = new HttpTransport(
                TestConstants.FAKE_ENDPOINT_TRACK, logger: NullTrackingLogger.Instance, client: new HttpClient(handler)))
            {
                bool ok = transport.SendAsync(new[] { TestEvents.Message("x") }).GetAwaiter().GetResult();
                Assert.IsFalse(ok);
            }
        }

        [Test]
        public void Send_EmptyBatch_IsNoOp()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler();
            using (HttpTransport transport = new HttpTransport(TestConstants.FAKE_ENDPOINT, client: new HttpClient(handler)))
            {
                bool ok = transport.SendAsync(new TrackingEvent[0]).GetAwaiter().GetResult();
                Assert.IsTrue(ok);
                Assert.AreEqual(0, handler.CallCount, "no request for an empty batch");
            }
        }

        [Test]
        public void DualMode_Factory_SelectsTransport_ByConfiguredMode()
        {
            ITransport simulated = TrackingSystem.CreateDefaultTransport(
                new TrackingConfig { TransportMode = TransportMode.Simulated }, NullTrackingLogger.Instance);
            ITransport http = TrackingSystem.CreateDefaultTransport(
                new TrackingConfig { TransportMode = TransportMode.Http }, NullTrackingLogger.Instance);

            Assert.IsInstanceOf<SimulatedHttpTransport>(simulated);
            Assert.IsInstanceOf<HttpTransport>(http);

            (http as IDisposable)?.Dispose();
        }

        [Test]
        public void Tracker_RoutesEvents_ThroughInjectedHttpTransport()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler { StatusCode = HttpStatusCode.OK };
            HttpTransport http = new HttpTransport(TestConstants.FAKE_ENDPOINT_TRACK, client: new HttpClient(handler));
            TrackingSystem tracker = new TrackingSystem(
                new TrackingConfig { TransportMode = TransportMode.Http },
                http,
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo(TestConstants.TEST_PLATFORM, TestConstants.TEST_APP_VERSION),
                startWorker: false,
                delayer: new RecordingDelayer(),
                logger: NullTrackingLogger.Instance);

            tracker.SendMessage("routed");
            tracker.Flush();

            Assert.AreEqual(1, handler.CallCount);
            StringAssert.Contains("routed", handler.LastBody);
        }
    }
}
