namespace DmytroUdovychenko.Tracking
{
    /// <summary>Selects the default transport built by <see cref="TrackingSystem"/> when none is injected.</summary>
    public enum TransportMode
    {
        // 0 reserved as the unset/None sentinel.

        /// <summary>Simulated delivery (no network). Default — the SDK works with no live server.</summary>
        Simulated = 1,

        /// <summary>Real HTTP delivery to <see cref="TrackingConfig.Endpoint"/> via <see cref="HttpTransport"/>.</summary>
        Http = 2
    }
}
