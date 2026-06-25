using System.Collections.Generic;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Durable storage for the backlog of undelivered events, so they survive a crash, kill, or
    /// quit and can be resent on next start. Delivery is therefore <em>at-least-once</em>; the event
    /// <see cref="TrackingEvent.Id"/> is the idempotency key the server uses to de-duplicate.
    /// </summary>
    public interface IEventStore
    {
        /// <summary>Overwrites the stored backlog with the given snapshot (oldest first).</summary>
        void Save(IReadOnlyList<TrackingEvent> events);

        /// <summary>Returns the persisted backlog (oldest first); empty if there is none.</summary>
        IReadOnlyList<TrackingEvent> Load();

        /// <summary>Discards the persisted backlog.</summary>
        void Clear();
    }
}
