using UnityEngine;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// MonoBehaviour glue that persists the tracker's pending queue on the mobile lifecycle events
    /// (app backgrounded / quitting), so the tail of buffered events survives process death.
    /// </summary>
    public sealed class TrackingLifecycle : MonoBehaviour
    {
        private TrackingSystem m_tracker;

        /// <summary>Creates a hidden, scene-persistent GameObject wired to the tracker.</summary>
        public static TrackingLifecycle Attach(TrackingSystem tracker)
        {
            GameObject go = new GameObject("[DmytroUdovychenko.Tracking] Lifecycle");
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(go);
            }
            go.hideFlags = HideFlags.HideInHierarchy;

            TrackingLifecycle component = go.AddComponent<TrackingLifecycle>();
            component.Bind(tracker);
            return component;
        }

        /// <summary>Associates this lifecycle hook with a tracker.</summary>
        public void Bind(TrackingSystem tracker) => m_tracker = tracker;

        /// <summary>Snapshots the tracker's pending events to durable storage now.</summary>
        public void PersistNow() => m_tracker?.Persist();

        // Refreshes connectivity from the main thread; the worker only reads the cached snapshot.
        private void Update() => m_tracker?.RefreshConnectivity();

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                PersistNow();
            }
        }

        private void OnApplicationQuit() => PersistNow();
    }
}
