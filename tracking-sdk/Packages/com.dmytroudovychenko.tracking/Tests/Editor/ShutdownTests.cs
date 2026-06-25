using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class ShutdownTests
    {
        [Test]
        public void SendMapAsync_ResolvesFalse_WhenDisposedWhileOffline_NeverHangs()
        {
            FakeConnectivity connectivity = new FakeConnectivity { IsOnline = false };
            TrackingSystem tracker = new TrackingSystem(
                new TrackingConfig(),
                new RecordingTransport(),
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo(TestConstants.TEST_PLATFORM, TestConstants.TEST_APP_VERSION),
                startWorker: true,                    // real worker: only the shutdown path can fail the awaiter
                delayer: new RecordingDelayer(),
                logger: NullTrackingLogger.Instance,
                connectivity: connectivity);

            Task<bool> pending = tracker.SendMapAsync(new Dictionary<string, object> { ["k"] = "v" });
            Assert.IsFalse(pending.IsCompleted, "offline: nothing delivered yet");

            tracker.Dispose(); // final drain can't send while offline -> it must fail the awaiter, not strand it

            Assert.IsTrue(pending.Wait(TimeSpan.FromSeconds(5)), "the awaiter must complete on dispose, never hang");
            Assert.IsFalse(pending.GetAwaiter().GetResult(), "undeliverable-at-shutdown resolves false");
        }

        [Test]
        public void SendMapAsync_ResolvesFalse_WhenDisposedBeforeWorkerStarted_NeverHangs()
        {
            TrackingSystem tracker = new TrackingSystem(
                new TrackingConfig(),
                new RecordingTransport(),
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo(TestConstants.TEST_PLATFORM, TestConstants.TEST_APP_VERSION),
                startWorker: false,                   // worker never runs -> only Dispose can fail the awaiter
                delayer: new RecordingDelayer(),
                logger: NullTrackingLogger.Instance);

            Task<bool> pending = tracker.SendMapAsync(new Dictionary<string, object> { ["k"] = "v" });
            Assert.IsFalse(pending.IsCompleted, "not flushed: nothing delivered yet");

            tracker.Dispose(); // never-started dispatcher must still fail buffered awaiters, not strand them

            Assert.IsTrue(pending.IsCompleted, "Dispose completes the awaiter even when the worker never started");
            Assert.IsFalse(pending.GetAwaiter().GetResult(), "undelivered-at-dispose resolves false");
        }

        [Test]
        public void SendMapAsync_ResolvesFalse_WhenTransportHangsThroughDispose_NeverStrands()
        {
            HangingTransport transport = new HangingTransport();
            TrackingSystem tracker = new TrackingSystem(
                new TrackingConfig { BatchSize = 1, ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100) },
                transport,
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo(TestConstants.TEST_PLATFORM, TestConstants.TEST_APP_VERSION),
                startWorker: true,                    // real worker so a batch reaches the (hanging) transport
                delayer: new RecordingDelayer(),
                logger: NullTrackingLogger.Instance);

            Task<bool> pending = tracker.SendMapAsync(new Dictionary<string, object> { ["k"] = "v" });
            Assert.IsTrue(transport.Entered.Wait(TimeSpan.FromSeconds(2)),
                "the worker should dequeue the batch and enter the transport send");
            Assert.IsFalse(pending.IsCompleted, "the hanging send leaves the awaiter pending");

            tracker.Dispose(); // worker stuck in transport.SendAsync past the drain budget -> the in-flight awaiter must still fail

            Assert.IsTrue(pending.Wait(TimeSpan.FromSeconds(5)),
                "an in-flight batch at dispose must complete its awaiter, never strand it");
            Assert.IsFalse(pending.GetAwaiter().GetResult(), "an undeliverable in-flight batch resolves false");
        }

        // A transport that ignores cancellation and outlasts the shutdown drain budget, then completes — a
        // misbehaving custom transport, without leaking the worker for the rest of the suite.
        private sealed class HangingTransport : ITransport
        {
            public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false);

            public async Task<bool> SendAsync(IReadOnlyList<TrackingEvent> batch, CancellationToken cancellationToken = default)
            {
                Entered.Set();
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                return true;
            }
        }
    }
}
