namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Abstraction over the host environment values that enrich each event. Isolating the UnityEngine
    /// dependency behind this seam keeps event enrichment unit-testable without touching engine APIs.
    /// </summary>
    /// <remarks>
    /// All values are coarse, non-identifying device context (see BLI-007): no stable device id,
    /// advertising id, or location is ever exposed here.
    /// </remarks>
    public interface IRuntimeInfo
    {
        /// <summary>Runtime platform string (e.g. Android, IPhonePlayer, OSXEditor).</summary>
        string Platform { get; }

        /// <summary>Host application version.</summary>
        string AppVersion { get; }

        /// <summary>Coarse device model (e.g. "iPhone14,5"); shared by millions, not identifying.</summary>
        string DeviceModel { get; }

        /// <summary>Operating-system name + version (e.g. "iOS 17.1").</summary>
        string OsVersion { get; }

        /// <summary>Active network type: <c>wifi</c> | <c>cellular</c> | <c>none</c>.</summary>
        string NetworkType { get; }

        /// <summary>Local UTC offset (e.g. "UTC+02:00") — coarse region hint, not a location.</summary>
        string Timezone { get; }

        /// <summary>Coarse locale / system language (e.g. "English").</summary>
        string Locale { get; }

        /// <summary>Application bundle / package identifier (same for every install — not identifying).</summary>
        string BundleId { get; }
    }
}
