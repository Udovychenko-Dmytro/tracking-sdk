namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Static build metadata for the Dmytro Udovychenko Tracking SDK.
    /// </summary>
    /// <remarks>
    /// <see cref="VERSION"/> is kept in sync with <c>package.json</c>. It is also a convenient,
    /// dependency-free target for the Phase 0 smoke test that proves the Runtime assembly compiles
    /// and is visible to the Tests assembly.
    /// </remarks>
    public static class TrackingSdk
    {
        /// <summary>Semantic version of the SDK. Must match the <c>version</c> field in <c>package.json</c>.</summary>
        public const string VERSION = "1.0.0";
    }
}
