using UnityEngine;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Reports whether the device currently has network access. The dispatcher consults this to avoid
    /// burning retry budget while known-offline — events are held in the queue and flushed when
    /// connectivity returns.
    /// </summary>
    public interface IConnectivity
    {
        bool IsOnline { get; }
    }

    /// <summary>Production <see cref="IConnectivity"/> over UnityEngine reachability. <see cref="Application.internetReachability"/>
    /// is main-thread-only, so the value is cached in a volatile snapshot (worker-safe) and re-polled by <see cref="Refresh"/>.</summary>
    public sealed class UnityConnectivity : IConnectivity
    {
        private volatile bool m_isOnline;

        /// <summary>Snapshots the initial reachability; must be constructed on the main thread.</summary>
        public UnityConnectivity() => Refresh();

        public bool IsOnline => m_isOnline;

        /// <summary>Re-reads reachability; call only from the main thread (driven by <see cref="TrackingLifecycle"/>).</summary>
        public void Refresh() => m_isOnline = Application.internetReachability != NetworkReachability.NotReachable;
    }

    /// <summary>
    /// Always reports online. The default, so connectivity-awareness is strictly opt-in (inject
    /// <see cref="UnityConnectivity"/> to enable hold-while-offline) and tests stay deterministic.
    /// </summary>
    public sealed class AlwaysOnlineConnectivity : IConnectivity
    {
        public static readonly AlwaysOnlineConnectivity Instance = new AlwaysOnlineConnectivity();

        public bool IsOnline => true;
    }
}
