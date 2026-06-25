using System.Threading.Tasks;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// A queued event plus its optional delivery awaiter. <see cref="Completion"/> is non-null only
    /// for <see cref="ITracker.SendMapAsync"/> events, which correlate an async result to the actual
    /// batch delivery; <see cref="ITracker.SendMessage"/> events are fire-and-forget.
    /// </summary>
    internal sealed class QueuedEvent
    {
        public TrackingEvent Event;
        public TaskCompletionSource<bool> Completion;
    }
}
