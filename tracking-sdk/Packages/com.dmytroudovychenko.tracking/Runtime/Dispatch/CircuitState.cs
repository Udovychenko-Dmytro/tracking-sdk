namespace DmytroUdovychenko.Tracking
{
    public enum CircuitState
    {
        // 0 reserved as the unset/None sentinel.

        /// <summary>Healthy — requests flow.</summary>
        Closed = 1,

        /// <summary>Tripped — requests are blocked until the cooldown elapses.</summary>
        Open = 2,

        /// <summary>Cooldown elapsed — a single trial request is allowed to probe recovery.</summary>
        HalfOpen = 3
    }
}
