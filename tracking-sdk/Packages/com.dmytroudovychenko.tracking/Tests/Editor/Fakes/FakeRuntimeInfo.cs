namespace DmytroUdovychenko.Tracking.Tests
{
    /// <summary>Fixed <see cref="IRuntimeInfo"/> so enrichment assertions don't depend on the host.</summary>
    /// <remarks>The device-context fields (BLI-007) default to the shared <see cref="TestConstants"/> values;
    /// override any of them via object-initializer syntax when a test needs specific values.</remarks>
    public sealed class FakeRuntimeInfo : IRuntimeInfo
    {
        public FakeRuntimeInfo(string platform, string appVersion)
        {
            Platform = platform;
            AppVersion = appVersion;
        }

        public string Platform { get; }

        public string AppVersion { get; }

        public string DeviceModel { get; set; } = TestConstants.TEST_DEVICE_MODEL;

        public string OsVersion { get; set; } = TestConstants.TEST_OS_VERSION;

        public string NetworkType { get; set; } = TestConstants.TEST_NETWORK_TYPE;

        public string Timezone { get; set; } = TestConstants.TEST_TIMEZONE;

        public string Locale { get; set; } = TestConstants.TEST_LOCALE;

        public string BundleId { get; set; } = TestConstants.TEST_BUNDLE_ID;
    }
}
