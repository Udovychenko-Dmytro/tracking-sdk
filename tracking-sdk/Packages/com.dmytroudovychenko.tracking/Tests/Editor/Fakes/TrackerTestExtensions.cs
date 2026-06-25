namespace DmytroUdovychenko.Tracking.Tests
{
    internal static class TrackerTestExtensions
    {
        /// <summary>Synchronously drives delivery of everything buffered (deterministic test pump).</summary>
        public static void Flush(this TrackingSystem tracker) => tracker.FlushAsync().GetAwaiter().GetResult();
    }
}
