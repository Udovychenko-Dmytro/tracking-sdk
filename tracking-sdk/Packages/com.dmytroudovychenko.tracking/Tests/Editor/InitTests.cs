using System;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    /// <summary>Covers the <see cref="TrackingSystem.Init(string)"/> factory overloads and endpoint mapping.</summary>
    public class InitTests
    {
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Init_BlankUserId_Throws(string userId)
        {
            Assert.Throws<ArgumentException>(() => TrackingSystem.Init(userId));
            Assert.Throws<ArgumentException>(() => TrackingSystem.Init(userId, ServerEnvironment.HttpTestServer));
            Assert.Throws<ArgumentException>(() => TrackingSystem.Init(userId, "https://example.test/track"));
        }

        [Test]
        public void Init_WithUserId_ExposesUserId()
        {
            using (TrackingSystem tracker = TrackingSystem.Init("user-1"))
            {
                Assert.AreEqual("user-1", tracker.UserId);
                Assert.IsFalse(string.IsNullOrEmpty(tracker.SessionId), "session id is set");
            }
        }

        [Test]
        public void Init_WithFakeServer_ExposesUserId()
        {
            // FakeServer is the offline fake (simulated) — no real network, never gated on connectivity.
            // (Init wires a real FileEventStore, so construction tests stay on simulated paths.)
            using (TrackingSystem tracker = TrackingSystem.Init("user-2", ServerEnvironment.FakeServer))
            {
                Assert.AreEqual("user-2", tracker.UserId);
            }
        }

        [TestCase(ServerEnvironment.FakeServer, TrackingConfig.DEFAULT_ENDPOINT)]
        [TestCase(ServerEnvironment.FakeServerChaos, TrackingConfig.DEFAULT_ENDPOINT)]
        [TestCase(ServerEnvironment.HttpTestServer, TrackingConfig.HTTP_TEST_ENDPOINT)]
        [TestCase(ServerEnvironment.HttpTestServerChaos, TrackingConfig.HTTP_TEST_CHAOS_ENDPOINT)]
        public void EndpointFor_MapsEnvironmentToUrl(ServerEnvironment server, string expected)
        {
            Assert.AreEqual(expected, TrackingConfig.EndpointFor(server));
        }

        // Both fake variants are simulated (no network); both HTTP variants deliver over real HTTP.
        [TestCase(ServerEnvironment.FakeServer, TransportMode.Simulated)]
        [TestCase(ServerEnvironment.FakeServerChaos, TransportMode.Simulated)]
        [TestCase(ServerEnvironment.HttpTestServer, TransportMode.Http)]
        [TestCase(ServerEnvironment.HttpTestServerChaos, TransportMode.Http)]
        public void TransportModeFor_MapsEnvironmentToMode(ServerEnvironment server, TransportMode expected)
        {
            Assert.AreEqual(expected, TrackingSystem.TransportModeFor(server));
        }

        // Only FakeServerChaos injects simulated failures; every other named server is clean (0%).
        [TestCase(ServerEnvironment.FakeServer, 0)]
        [TestCase(ServerEnvironment.FakeServerChaos, TrackingConfig.CHAOS_FAIL_PERCENT)]
        [TestCase(ServerEnvironment.HttpTestServer, 0)]
        [TestCase(ServerEnvironment.HttpTestServerChaos, 0)]
        public void SimulatedFailPercentFor_OnlyFakeChaosInjects(ServerEnvironment server, int expected)
        {
            Assert.AreEqual(expected, TrackingConfig.SimulatedFailPercentFor(server));
        }

        // The offline gate: only real HTTP delivery requires connectivity; the simulated path never blocks.
        [TestCase(TransportMode.Http, false, true)]
        [TestCase(TransportMode.Http, true, false)]
        [TestCase(TransportMode.Simulated, false, false)]
        [TestCase(TransportMode.Simulated, true, false)]
        public void IsBlockedOffline_BlocksOnlyHttpWhileOffline(TransportMode mode, bool online, bool expectedBlocked)
        {
            FakeConnectivity connectivity = new FakeConnectivity { IsOnline = online };
            Assert.AreEqual(expectedBlocked, TrackingSystem.IsBlockedOffline(mode, connectivity));
        }
    }
}
