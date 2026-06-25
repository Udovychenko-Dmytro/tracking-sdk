using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class FileEventStoreTests
    {
        private string m_path;

        [SetUp]
        public void SetUp()
        {
            m_path = Path.Combine(Path.GetTempPath(), "tracking-test-" + Guid.NewGuid().ToString("N") + ".json");
        }

        [TearDown]
        public void TearDown()
        {
            try { if (File.Exists(m_path)) File.Delete(m_path); } catch { /* ignore */ }
        }

        [Test]
        public void Save_ThenLoad_RoundTripsEvent_AcrossPayloadTypes()
        {
            FileEventStore store = new FileEventStore(m_path);
            TrackingEvent original = new TrackingEvent(
                id: "id-1",
                type: TrackingEventType.MAP,
                timestampUtc: TestEvents.T0,
                sessionId: "sess",
                userId: "user-9",
                sdkVersion: TrackingSdk.VERSION,
                platform: TestConstants.TEST_PLATFORM,
                appVersion: TestConstants.TEST_APP_VERSION,
                deviceModel: TestConstants.TEST_DEVICE_MODEL,
                osVersion: TestConstants.TEST_OS_VERSION,
                networkType: TestConstants.TEST_NETWORK_TYPE,
                timezone: TestConstants.TEST_TIMEZONE,
                locale: TestConstants.TEST_LOCALE,
                bundleId: TestConstants.TEST_BUNDLE_ID,
                payload: new Dictionary<string, object>
                {
                    ["name"] = "purchase",
                    ["count"] = 42,
                    ["ratio"] = 0.5,
                    ["flag"] = true,
                });

            store.Save(new[] { original });
            IReadOnlyList<TrackingEvent> loaded = store.Load();

            Assert.AreEqual(1, loaded.Count);
            TrackingEvent e = loaded[0];
            Assert.AreEqual("id-1", e.Id);
            Assert.AreEqual(TrackingEventType.MAP, e.Type);
            Assert.AreEqual(TestEvents.T0, e.TimestampUtc);
            Assert.AreEqual("sess", e.SessionId);
            Assert.AreEqual("user-9", e.UserId);
            Assert.AreEqual(TestConstants.TEST_DEVICE_MODEL, e.DeviceModel, "device model round-trips");
            Assert.AreEqual(TestConstants.TEST_OS_VERSION, e.OsVersion, "os version round-trips");
            Assert.AreEqual(TestConstants.TEST_NETWORK_TYPE, e.NetworkType, "network type round-trips");
            Assert.AreEqual(TestConstants.TEST_TIMEZONE, e.Timezone, "timezone round-trips");
            Assert.AreEqual(TestConstants.TEST_LOCALE, e.Locale, "locale round-trips");
            Assert.AreEqual(TestConstants.TEST_BUNDLE_ID, e.BundleId, "bundle id round-trips");
            Assert.AreEqual("purchase", e.Payload["name"]);
            Assert.AreEqual(42, e.Payload["count"]);
            Assert.AreEqual(0.5, e.Payload["ratio"]);
            Assert.AreEqual(true, e.Payload["flag"]);
        }

        [Test]
        public void SerializedEvent_ContainsNoForbiddenFields()
        {
            // BLI-007 "NOT collected" invariant: no stable device id, advertising id, IP, location, or carrier.
            FileEventStore store = new FileEventStore(m_path);
            store.Save(new[] { TestEvents.Message("x") });

            string json = File.ReadAllText(m_path).ToLowerInvariant();
            foreach (string forbidden in new[]
                     { "deviceid", "advertis", "idfa", "idfv", "androidid", "uniqueidentifier", "latitude", "longitude", "carrier", "\"ip\"" })
            {
                StringAssert.DoesNotContain(forbidden, json, $"forbidden field '{forbidden}' must never be serialized");
            }
        }

        [Test]
        public void Load_MissingFile_ReturnsEmpty()
        {
            FileEventStore store = new FileEventStore(m_path);
            Assert.IsFalse(File.Exists(m_path));
            Assert.AreEqual(0, store.Load().Count);
        }

        [Test]
        public void Load_CorruptFile_ReturnsEmpty_WithoutThrowing()
        {
            File.WriteAllText(m_path, "this is not valid json {{{");
            FileEventStore store = new FileEventStore(m_path);

            Assert.DoesNotThrow(() => { IReadOnlyList<TrackingEvent> _ = store.Load(); });
            Assert.AreEqual(0, store.Load().Count);
        }

        [Test]
        public void Clear_RemovesPersistedFile()
        {
            FileEventStore store = new FileEventStore(m_path);
            store.Save(new[] { TestEvents.Message("a") });
            Assert.IsTrue(File.Exists(m_path));

            store.Clear();
            Assert.IsFalse(File.Exists(m_path));
            Assert.AreEqual(0, store.Load().Count);
        }
    }
}
