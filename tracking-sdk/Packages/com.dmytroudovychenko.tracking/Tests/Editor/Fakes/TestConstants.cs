namespace DmytroUdovychenko.Tracking.Tests
{
    /// <summary>
    /// Magic literals shared across the test suite: deployed endpoints, the fixed runtime identity,
    /// and common dispatcher sentinels. Per-test thresholds stay inline with their assertions.
    /// </summary>
    internal static class TestConstants
    {
        /// <summary>Deployed PHP receiver used by the live end-to-end tests.</summary>
        public const string LIVE_ENDPOINT = TrackingConfig.HTTP_TEST_ENDPOINT;

        /// <summary>Same receiver in chaos mode (<c>?fail=20</c> → ~20% transient 503s).</summary>
        public const string CHAOS_ENDPOINT = TrackingConfig.HTTP_TEST_CHAOS_ENDPOINT;

        /// <summary>Offline fake host — the SDK's documented default endpoint; no real network is hit.</summary>
        public const string FAKE_ENDPOINT = TrackingConfig.DEFAULT_ENDPOINT;
        public const string FAKE_ENDPOINT_TRACK = TrackingConfig.DEFAULT_ENDPOINT + "/track.php";

        /// <summary>Fixed runtime identity so enrichment assertions don't depend on the host.</summary>
        public const string TEST_PLATFORM = "TestPlatform";
        public const string TEST_APP_VERSION = "1.0.0";
        public const string TEST_SESSION_ID = "test-session";
        public const string TEST_USER_ID = "test-user";

        /// <summary>Fixed device context (BLI-007) so enrichment assertions don't depend on the host.</summary>
        public const string TEST_DEVICE_MODEL = "TestDevice1,1";
        public const string TEST_OS_VERSION = "TestOS 1.0";
        public const string TEST_NETWORK_TYPE = "wifi";
        public const string TEST_TIMEZONE = "UTC+00:00";
        public const string TEST_LOCALE = "English";
        public const string TEST_BUNDLE_ID = "com.dmytroudovychenko.tracking.tests";

        /// <summary>Roomy queue capacity for dispatcher tests that never mean to overflow.</summary>
        public const int DEFAULT_QUEUE_CAPACITY = 1000;

        /// <summary>Dead-letter sink capacity for tests that aren't probing its bound.</summary>
        public const int DEAD_LETTER_CAPACITY = 100;

        /// <summary><see cref="FlakyTransport"/> failure count standing in for a permanently-down server.</summary>
        public const int NEVER_SUCCEEDS = 999;
    }
}
