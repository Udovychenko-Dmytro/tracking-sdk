using System;
using System.Collections.Generic;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// No-op store: persists nothing, loads nothing. The default so that <c>new TrackingSystem()</c> never
    /// touches the filesystem unless durability is explicitly opted into (e.g. with a
    /// <see cref="FileEventStore"/>).
    /// </summary>
    public sealed class NullEventStore : IEventStore
    {
        public static readonly NullEventStore Instance = new NullEventStore();

        private static readonly IReadOnlyList<TrackingEvent> Empty = Array.Empty<TrackingEvent>();

        private NullEventStore() { }

        public void Save(IReadOnlyList<TrackingEvent> events) { }

        public IReadOnlyList<TrackingEvent> Load() => Empty;

        public void Clear() { }
    }
}
