using NUnit.Framework;
using UnityEngine;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class LifecycleTests
    {
        [Test]
        public void TrackingLifecycle_PersistNow_SnapshotsTrackerQueueToStore()
        {
            InMemoryEventStore store = new InMemoryEventStore();
            TrackingSystem tracker = new TrackingSystem(
                new TrackingConfig(),
                new RecordingTransport(),
                new FakeClock(TestEvents.T0),
                new FakeRuntimeInfo(TestConstants.TEST_PLATFORM, TestConstants.TEST_APP_VERSION),
                startWorker: false,
                delayer: new RecordingDelayer(),
                store: store);

            tracker.SendMessage("a");
            tracker.SendMessage("b");

            GameObject go = new GameObject("lifecycle-test");
            try
            {
                TrackingLifecycle lifecycle = go.AddComponent<TrackingLifecycle>();
                lifecycle.Bind(tracker);

                lifecycle.PersistNow();

                Assert.AreEqual(2, store.Load().Count, "lifecycle hook persisted the pending queue");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
