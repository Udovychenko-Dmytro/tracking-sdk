using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Serializes events to/from JSON using Unity's dependency-free <see cref="JsonUtility"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="JsonUtility"/> cannot serialize a <c>Dictionary&lt;string, object&gt;</c>, so each
    /// payload entry is flattened to a type-tagged key/value triple (<c>k</c>, <c>t</c>, <c>v</c>) and
    /// restored to its original primitive type on load. Supported value types: string, bool, int, long,
    /// float, double, and null (anything else degrades to its invariant string form).
    /// </remarks>
    internal static class EventSerializer
    {
        [Serializable]
        private sealed class PayloadEntry
        {
            public string k;
            public string t;
            public string v;
        }

        [Serializable]
        private sealed class SerializedEvent
        {
            public string id;
            public string type;
            public long ts;
            public string sessionId;
            public string userId;
            public string sdkVersion;
            public string platform;
            public string appVersion;
            public string deviceModel;
            public string osVersion;
            public string networkType;
            public string timezone;
            public string locale;
            public string bundleId;
            public List<PayloadEntry> payload;
        }

        [Serializable]
        private sealed class Batch
        {
            public List<SerializedEvent> events;
        }

        public static string ToJson(IReadOnlyList<TrackingEvent> events)
        {
            Batch batch = new Batch { events = new List<SerializedEvent>(events?.Count ?? 0) };

            if (events != null)
            {
                foreach (TrackingEvent e in events)
                {
                    SerializedEvent serializedEvent = new SerializedEvent
                    {
                        id = e.Id,
                        type = e.Type,
                        ts = e.TimestampUtc.ToUnixTimeMilliseconds(),
                        sessionId = e.SessionId,
                        userId = e.UserId,
                        sdkVersion = e.SdkVersion,
                        platform = e.Platform,
                        appVersion = e.AppVersion,
                        deviceModel = e.DeviceModel,
                        osVersion = e.OsVersion,
                        networkType = e.NetworkType,
                        timezone = e.Timezone,
                        locale = e.Locale,
                        bundleId = e.BundleId,
                        payload = new List<PayloadEntry>(e.Payload.Count),
                    };
                    foreach (KeyValuePair<string, object> entry in e.Payload)
                    {
                        serializedEvent.payload.Add(Encode(entry.Key, entry.Value));
                    }
                    batch.events.Add(serializedEvent);
                }
            }

            return JsonUtility.ToJson(batch);
        }

        public static List<TrackingEvent> FromJson(string json)
        {
            List<TrackingEvent> result = new List<TrackingEvent>();
            if (string.IsNullOrEmpty(json)) return result;

            Batch batch;
            try
            {
                batch = JsonUtility.FromJson<Batch>(json);
            }
            catch
            {
                return result; // corrupt / unparseable — caller treats as "no backlog"
            }

            if (batch?.events == null) return result;

            foreach (SerializedEvent e in batch.events)
            {
                if (e == null || string.IsNullOrEmpty(e.id) || string.IsNullOrEmpty(e.type)) continue;

                Dictionary<string, object> payload = new Dictionary<string, object>(e.payload?.Count ?? 0);
                if (e.payload != null)
                {
                    foreach (PayloadEntry entry in e.payload)
                    {
                        if (entry != null && entry.k != null)
                        {
                            payload[entry.k] = Decode(entry);
                        }
                    }
                }

                result.Add(new TrackingEvent(
                    e.id,
                    e.type,
                    DateTimeOffset.FromUnixTimeMilliseconds(e.ts),
                    e.sessionId,
                    e.userId,
                    e.sdkVersion,
                    e.platform,
                    e.appVersion,
                    e.deviceModel,
                    e.osVersion,
                    e.networkType,
                    e.timezone,
                    e.locale,
                    e.bundleId,
                    payload));
            }

            return result;
        }

        private static PayloadEntry Encode(string key, object value)
        {
            switch (value)
            {
                case null: return new PayloadEntry { k = key, t = "n", v = string.Empty };
                case string s: return new PayloadEntry { k = key, t = "s", v = s };
                case bool b: return new PayloadEntry { k = key, t = "b", v = b ? "1" : "0" };
                case int i: return new PayloadEntry { k = key, t = "i", v = i.ToString(CultureInfo.InvariantCulture) };
                case long l: return new PayloadEntry { k = key, t = "l", v = l.ToString(CultureInfo.InvariantCulture) };
                case float f: return new PayloadEntry { k = key, t = "f", v = f.ToString("R", CultureInfo.InvariantCulture) };
                case double d: return new PayloadEntry { k = key, t = "d", v = d.ToString("R", CultureInfo.InvariantCulture) };
                default: return new PayloadEntry { k = key, t = "s", v = Convert.ToString(value, CultureInfo.InvariantCulture) };
            }
        }

        private static object Decode(PayloadEntry entry)
        {
            switch (entry.t)
            {
                case "n": return null;
                case "s": return entry.v;
                case "b": return entry.v == "1" || entry.v == "true";
                case "i":
                    return int.TryParse(entry.v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? (object)i : entry.v;
                case "l":
                    return long.TryParse(entry.v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l) ? (object)l : entry.v;
                case "f":
                    return float.TryParse(entry.v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? (object)f : entry.v;
                case "d":
                    return double.TryParse(entry.v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? (object)d : entry.v;
                default: return entry.v;
            }
        }
    }
}
