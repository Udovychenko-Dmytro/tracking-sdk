using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    /// <summary>
    /// Covers the static <see cref="Tracker"/> facade: not-initialized no-ops, adoption + forwarding to the
    /// underlying <see cref="TrackingSystem"/>, double-Init guard, and Dispose reset. <c>TearDown</c> always
    /// calls <see cref="Tracker.Dispose"/> so the global static state can't leak across tests.
    /// </summary>
    public class StaticFacadeTests
    {
        // Deterministic instance adopted WITHOUT lifecycle wiring (no scene/quit side effects in tests).
        private static TrackingSystem NewInstance(out RecordingTransport transport, bool enabled = true)
        {
            transport = new RecordingTransport();
            return new TrackingSystem(
                new TrackingConfig { Enabled = enabled },
                transport,
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo(TestConstants.TEST_PLATFORM, TestConstants.TEST_APP_VERSION),
                startWorker: false,
                logger: NullTrackingLogger.Instance);
        }

        [TearDown]
        public void TearDown() => Tracker.Dispose();

        [Test]
        public void SendMessage_BeforeInit_ReturnsFalse()
        {
            Assert.IsFalse(Tracker.IsInitialized);
            Assert.IsFalse(Tracker.SendMessage("hi"));
        }

        [Test]
        public void SendMapAsync_BeforeInit_ResolvesFalse_NeverHangs()
        {
            Task<bool> pending = Tracker.SendMapAsync(new Dictionary<string, object> { ["k"] = "v" });

            Assert.IsTrue(pending.IsCompleted, "must not hang before Init");
            Assert.IsFalse(pending.GetAwaiter().GetResult());
        }

        [Test]
        public void FlushAsync_BeforeInit_CompletesNoOp()
        {
            Task flush = Tracker.FlushAsync();
            Assert.IsTrue(flush.IsCompleted);
        }

        [Test]
        public void Diagnostics_BeforeInit_AreSafeDefaults()
        {
            Assert.IsFalse(Tracker.IsInitialized);
            Assert.IsFalse(Tracker.IsEnabled);
            Assert.IsFalse(Tracker.IsPrivacyMode);
            Assert.IsNull(Tracker.Current);
            Assert.IsNull(Tracker.DeadLetter);
            Assert.IsNull(Tracker.SessionId);
            Assert.IsNull(Tracker.UserId);
            Assert.AreEqual(0, Tracker.Metrics.Enqueued);
            Assert.AreEqual(0, Tracker.Metrics.Sent);
        }

        [Test]
        public void Persist_SetEnabled_Purge_BeforeInit_DoNotThrow()
        {
            Assert.DoesNotThrow(() => Tracker.Persist());
            Assert.DoesNotThrow(() => Tracker.SetEnabled(false));
            Assert.DoesNotThrow(() => Tracker.SetPrivacyMode(true));
            Assert.DoesNotThrow(() => Tracker.Purge());
        }

        [Test]
        public void Init_NullInstance_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Tracker.Init((TrackingSystem)null));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Init_BlankUserId_Throws(string userId)
        {
            Assert.Throws<ArgumentException>(() => Tracker.Init(userId));
            Assert.IsFalse(Tracker.IsInitialized, "a failed Init must not leave a half-built global");
        }

        [Test]
        public void Init_AdoptsInstance_ForwardsSendMessage()
        {
            TrackingSystem instance = NewInstance(out RecordingTransport transport);
            Tracker.Init(instance, attachLifecycle: false);

            Assert.IsTrue(Tracker.IsInitialized);
            Assert.AreSame(instance, Tracker.Current);
            Assert.IsTrue(Tracker.SendMessage("hello"));

            Tracker.FlushAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, transport.Events.Count);
        }

        [Test]
        public void Init_AdoptsInstance_ForwardsSendMapAsync()
        {
            TrackingSystem instance = NewInstance(out RecordingTransport transport);
            Tracker.Init(instance, attachLifecycle: false);

            Task<bool> delivered = Tracker.SendMapAsync(new Dictionary<string, object> { ["k"] = "v" });
            Tracker.FlushAsync().GetAwaiter().GetResult();

            Assert.IsTrue(delivered.GetAwaiter().GetResult());
            Assert.AreEqual(1, transport.Events.Count);
        }

        [Test]
        public void Forwarding_ExposesMetricsAndUserState()
        {
            TrackingSystem instance = NewInstance(out _);
            Tracker.Init(instance, attachLifecycle: false);

            Tracker.SendMessage("a");
            Tracker.FlushAsync().GetAwaiter().GetResult();

            Assert.IsTrue(Tracker.IsEnabled);
            Assert.AreEqual(instance.UserId, Tracker.UserId);
            Assert.AreEqual(instance.SessionId, Tracker.SessionId);
            Assert.AreEqual(1, Tracker.Metrics.Enqueued);
            Assert.AreEqual(1, Tracker.Metrics.Sent);
        }

        [Test]
        public void SetEnabled_Forwards_DisablingRejectsSends()
        {
            TrackingSystem instance = NewInstance(out RecordingTransport transport);
            Tracker.Init(instance, attachLifecycle: false);

            Tracker.SetEnabled(false);

            Assert.IsFalse(Tracker.IsEnabled);
            Assert.IsFalse(Tracker.SendMessage("blocked"));
            Tracker.FlushAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0, transport.Events.Count);
        }

        [Test]
        public void SetPrivacyMode_Forwards_AnonymizesUserId()
        {
            TrackingSystem instance = NewInstance(out RecordingTransport transport);
            Tracker.Init(instance, attachLifecycle: false);

            Assert.IsFalse(Tracker.IsPrivacyMode);
            Tracker.SetPrivacyMode(true);
            Assert.IsTrue(Tracker.IsPrivacyMode);

            Tracker.SendMessage("a");
            Tracker.FlushAsync().GetAwaiter().GetResult();

            Assert.AreEqual("anonymous", transport.Events[0].UserId, "facade forwards privacy mode to the instance");
        }

        [Test]
        public void SecondInit_IsIgnored_KeepsFirstInstance()
        {
            TrackingSystem first = NewInstance(out RecordingTransport firstTransport);
            TrackingSystem second = NewInstance(out RecordingTransport secondTransport);

            // Buffer an event on `second` (never flushed) so its awaiter stays pending — disposing `second`
            // resolves it, which is how we observe that Init disposed the rejected instance.
            Task<bool> secondPending = second.SendMapAsync(new Dictionary<string, object> { ["k"] = "v" });
            Assert.IsFalse(secondPending.IsCompleted, "buffered on second, not yet delivered");

            Tracker.Init(first, attachLifecycle: false);
            Tracker.Init(second, attachLifecycle: false); // ignored AND disposed: already initialized

            Assert.AreSame(first, Tracker.Current);

            Tracker.SendMessage("x");
            Tracker.FlushAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, firstTransport.Events.Count, "events go to the first (kept) tracker");
            Assert.AreEqual(0, secondTransport.Events.Count, "the ignored tracker receives nothing");
            // Init disposed the rejected instance (no leaked worker): Dispose resolved its buffered awaiter.
            Assert.IsTrue(secondPending.IsCompleted, "Init disposed the rejected instance (resolved its awaiter)");
            Assert.IsFalse(secondPending.GetAwaiter().GetResult(), "the disposed instance's awaiter resolves false");
        }

        [Test]
        public void Dispose_ClearsState_AndAllowsReinit()
        {
            TrackingSystem first = NewInstance(out RecordingTransport firstTransport);
            Tracker.Init(first, attachLifecycle: false);

            Tracker.Dispose();
            Assert.IsFalse(Tracker.IsInitialized);
            Assert.IsNull(Tracker.Current);

            TrackingSystem second = NewInstance(out RecordingTransport secondTransport);
            Tracker.Init(second, attachLifecycle: false);
            Assert.AreSame(second, Tracker.Current);

            Tracker.SendMessage("y");
            Tracker.FlushAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0, firstTransport.Events.Count, "the disposed first tracker is detached");
            Assert.AreEqual(1, secondTransport.Events.Count);
        }

        [Test]
        public void Dispose_BeforeInit_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Tracker.Dispose());
        }

        [Test]
        public void InitAsync_LiveServer_ProbeOffline_DoesNotInitialize()
        {
            FakeConnectivityProbe probe = new FakeConnectivityProbe { Reachable = false };
            bool inited = Tracker.InitAsync("u", ServerEnvironment.HttpTestServer, probe, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(inited);
            Assert.IsFalse(Tracker.IsInitialized, "a failed probe must leave no global");
            Assert.AreEqual(1, probe.CallCount, "a live server is probed before Init");
        }

        [Test]
        public void InitAsync_FakeServer_SkipsProbe_AndInitializes()
        {
            FakeConnectivityProbe probe = new FakeConnectivityProbe { Reachable = false };
            bool inited = Tracker.InitAsync("u", ServerEnvironment.FakeServer, probe, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsTrue(inited);
            Assert.IsTrue(Tracker.IsInitialized);
            Assert.AreEqual(0, probe.CallCount, "FakeServer (simulated) needs no probe");
        }

        [Test]
        public void IsServerReachableAsync_ReturnsProbeResult()
        {
            Assert.IsTrue(Tracker.IsServerReachableAsync("https://x.test", new FakeConnectivityProbe { Reachable = true }, CancellationToken.None)
                .GetAwaiter().GetResult());
            Assert.IsFalse(Tracker.IsServerReachableAsync("https://x.test", new FakeConnectivityProbe { Reachable = false }, CancellationToken.None)
                .GetAwaiter().GetResult());
        }

        [Test]
        public void InitAsync_LiveServer_ProbesConfiguredEndpoint()
        {
            FakeConnectivityProbe probe = new FakeConnectivityProbe { Reachable = false };
            Tracker.InitAsync("u", ServerEnvironment.HttpTestServer, probe, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(TrackingConfig.HTTP_TEST_ENDPOINT, probe.LastEndpoint, "the live server's own endpoint is pinged");
        }
    }
}
