using System;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Abstraction over "the current time". Injecting it lets tests pin timestamps and drive retry
    /// backoff without real delays (a virtual clock), keeping time-dependent behaviour deterministic.
    /// </summary>
    public interface IClock
    {
        /// <summary>Current UTC time.</summary>
        DateTimeOffset UtcNow { get; }
    }
}
