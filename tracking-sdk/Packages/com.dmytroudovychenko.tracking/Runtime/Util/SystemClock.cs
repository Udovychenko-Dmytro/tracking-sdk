using System;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>Production <see cref="IClock"/> backed by the system wall clock.</summary>
    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new SystemClock();

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
