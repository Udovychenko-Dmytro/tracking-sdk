using System;
using System.Collections.Generic;

namespace DmytroUdovychenko.Tracking.Tests
{
    /// <summary>Builders for fully-formed <see cref="TrackingEvent"/>s in dispatcher/queue tests.</summary>
    internal static class TestEvents
    {
        public static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public static TrackingEvent Message(string message, DateTimeOffset? timestamp = null)
        {
            return new TrackingEvent(
                id: Guid.NewGuid().ToString("N"),
                type: TrackingEventType.MESSAGE,
                timestampUtc: timestamp ?? T0,
                sessionId: TestConstants.TEST_SESSION_ID,
                userId: TestConstants.TEST_USER_ID,
                sdkVersion: TrackingSdk.VERSION,
                platform: TestConstants.TEST_PLATFORM,
                appVersion: TestConstants.TEST_APP_VERSION,
                deviceModel: TestConstants.TEST_DEVICE_MODEL,
                osVersion: TestConstants.TEST_OS_VERSION,
                networkType: TestConstants.TEST_NETWORK_TYPE,
                timezone: TestConstants.TEST_TIMEZONE,
                locale: TestConstants.TEST_LOCALE,
                bundleId: TestConstants.TEST_BUNDLE_ID,
                payload: new Dictionary<string, object> { ["message"] = message });
        }
    }
}
