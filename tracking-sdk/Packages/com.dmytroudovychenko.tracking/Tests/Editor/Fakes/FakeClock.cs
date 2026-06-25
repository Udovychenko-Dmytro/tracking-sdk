using System;

namespace DmytroUdovychenko.Tracking.Tests
{
    /// <summary>
    /// Deterministic <see cref="IClock"/> for tests. Time only moves when the test moves it, so
    /// timestamps and (from Phase 3) retry backoff are exact and free of real delays.
    /// </summary>
    public sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset start) => UtcNow = start;

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan by) => UtcNow += by;

        public void Set(DateTimeOffset now) => UtcNow = now;
    }
}
