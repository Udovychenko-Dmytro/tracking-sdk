using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    /// <summary>Covers <see cref="HttpConnectivityProbe"/>: any HTTP response (even 405, as track.php returns to a
    /// HEAD) → reachable; transport throw → not; interface fast-fail short-circuits the network call.</summary>
    public class ConnectivityProbeTests
    {
        private const string SERVER_URL = "https://server.test/track.php";

        private sealed class RecordingLogger : ITrackingLogger
        {
            public readonly List<(TrackingLogLevel level, string message)> Entries =
                new List<(TrackingLogLevel, string)>();

            public void Log(TrackingLogLevel level, string message, Exception exception = null)
                => Entries.Add((level, message));
        }

        private static HttpConnectivityProbe Build(FakeHttpMessageHandler handler, bool online, ITrackingLogger logger = null)
        {
            return new HttpConnectivityProbe(
                new FakeConnectivity { IsOnline = online },
                logger: logger ?? NullTrackingLogger.Instance,
                client: new HttpClient(handler));
        }

        [Test]
        public void IsReachable_True_On2xx()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler { StatusCode = HttpStatusCode.OK };
            using (HttpConnectivityProbe probe = Build(handler, online: true))
            {
                Assert.IsTrue(probe.IsReachableAsync(SERVER_URL).GetAwaiter().GetResult());
                Assert.AreEqual(1, handler.CallCount);
            }
        }

        [Test]
        public void IsReachable_True_WhenServerResponds_Even405()
        {
            // track.php replies 405 to a non-POST — that still proves the server is alive and answering.
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler { StatusCode = HttpStatusCode.MethodNotAllowed };
            using (HttpConnectivityProbe probe = Build(handler, online: true))
            {
                Assert.IsTrue(probe.IsReachableAsync(SERVER_URL).GetAwaiter().GetResult());
            }
        }

        [Test]
        public void IsReachable_False_WhenServerDoesNotRespond()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler { ThrowOnSend = new HttpRequestException("no route") };
            using (HttpConnectivityProbe probe = Build(handler, online: true))
            {
                Assert.IsFalse(probe.IsReachableAsync(SERVER_URL).GetAwaiter().GetResult());
            }
        }

        [Test]
        public void IsReachable_False_FastFails_Offline_WithoutHttpCall()
        {
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler { StatusCode = HttpStatusCode.OK };
            using (HttpConnectivityProbe probe = Build(handler, online: false))
            {
                Assert.IsFalse(probe.IsReachableAsync(SERVER_URL).GetAwaiter().GetResult());
                Assert.AreEqual(0, handler.CallCount, "a down interface must short-circuit the network call");
            }
        }

        // ---- BLI-001 logging: steps at Debug, connection failure at Error ----

        [Test]
        public void Reachable_LogsEachStep_AtDebug()
        {
            RecordingLogger logger = new RecordingLogger();
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler { StatusCode = HttpStatusCode.OK };
            using (HttpConnectivityProbe probe = Build(handler, online: true, logger: logger))
            {
                Assert.IsTrue(probe.IsReachableAsync(SERVER_URL).GetAwaiter().GetResult());
            }

            Assert.IsTrue(logger.Entries.Exists(e => e.level == TrackingLogLevel.Debug && e.message.Contains("device online")),
                "device-online step logs at Debug");
            Assert.IsTrue(logger.Entries.Exists(e => e.level == TrackingLogLevel.Debug && e.message.Contains("reachable")),
                "server-reachable step logs at Debug");
            Assert.IsFalse(logger.Entries.Exists(e => e.level == TrackingLogLevel.Error), "no Error on the happy path");
        }

        [Test]
        public void Offline_LogsConnectionFailure_AtError()
        {
            RecordingLogger logger = new RecordingLogger();
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler { StatusCode = HttpStatusCode.OK };
            using (HttpConnectivityProbe probe = Build(handler, online: false, logger: logger))
            {
                Assert.IsFalse(probe.IsReachableAsync(SERVER_URL).GetAwaiter().GetResult());
            }

            Assert.IsTrue(logger.Entries.Exists(e => e.level == TrackingLogLevel.Error && e.message.Contains("no internet")),
                "device-offline failure logs at Error");
        }

        [Test]
        public void ServerDown_LogsConnectionFailure_AtError()
        {
            RecordingLogger logger = new RecordingLogger();
            FakeHttpMessageHandler handler = new FakeHttpMessageHandler { ThrowOnSend = new HttpRequestException("no route") };
            using (HttpConnectivityProbe probe = Build(handler, online: true, logger: logger))
            {
                Assert.IsFalse(probe.IsReachableAsync(SERVER_URL).GetAwaiter().GetResult());
            }

            Assert.IsTrue(logger.Entries.Exists(e => e.level == TrackingLogLevel.Error && e.message.Contains("did not respond")),
                "server-no-response failure logs at Error");
        }
    }
}
