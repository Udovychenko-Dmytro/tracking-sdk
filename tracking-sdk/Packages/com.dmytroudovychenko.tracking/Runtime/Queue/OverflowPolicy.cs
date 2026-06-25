namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// What the bounded <c>EventQueue</c> does when a new event arrives and the buffer is full.
    /// </summary>
    public enum OverflowPolicy
    {
        // 0 reserved as the unset/None sentinel.

        /// <summary>
        /// Evict the oldest buffered event to make room for the new one. The producer is never
        /// blocked or rejected (so <see cref="ITracker.SendMessage"/> keeps returning <c>true</c>);
        /// the oldest data is sacrificed first. Good default for telemetry, where recent events
        /// matter most. The evicted event's awaiter (if any) resolves <c>false</c>.
        /// </summary>
        DropOldest = 1,

        /// <summary>
        /// Reject the incoming event when full. <see cref="ITracker.SendMessage"/> returns
        /// <c>false</c> and <see cref="ITracker.SendMapAsync"/> resolves <c>false</c>. Preserves the
        /// oldest data; applies hard backpressure on the producer.
        /// </summary>
        RejectNew = 2
    }
}
