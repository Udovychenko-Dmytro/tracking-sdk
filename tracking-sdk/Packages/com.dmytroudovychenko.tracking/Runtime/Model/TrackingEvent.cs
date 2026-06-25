using System;
using System.Collections.Generic;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// An immutable, enriched tracking event: the unit that flows through the queue, the dispatcher,
    /// and out to the transport. Carries a stable <see cref="Id"/> (used as the idempotency key so
    /// retries never double-count server-side) plus a metadata envelope.
    /// </summary>
    public sealed class TrackingEvent
    {
        /// <summary>Globally-unique id; doubles as the server-side idempotency key.</summary>
        public string Id { get; }

        /// <summary>Logical kind of event — see <see cref="TrackingEventType"/>.</summary>
        public string Type { get; }

        /// <summary>UTC creation time, captured from the injected <see cref="IClock"/>.</summary>
        public DateTimeOffset TimestampUtc { get; }

        /// <summary>Identifier of the tracker session that produced this event.</summary>
        public string SessionId { get; }

        /// <summary>Identifier of the user this event belongs to.</summary>
        public string UserId { get; }

        /// <summary>Version of the SDK that produced this event.</summary>
        public string SdkVersion { get; }

        /// <summary>Runtime platform (e.g. Android, IPhonePlayer, OSXEditor).</summary>
        public string Platform { get; }

        /// <summary>Host application version.</summary>
        public string AppVersion { get; }

        /// <summary>Coarse device model (e.g. "iPhone14,5") — non-identifying device context.</summary>
        public string DeviceModel { get; }

        /// <summary>Operating-system name + version (e.g. "iOS 17.1").</summary>
        public string OsVersion { get; }

        /// <summary>Active network type at capture: <c>wifi</c> | <c>cellular</c> | <c>none</c>.</summary>
        public string NetworkType { get; }

        /// <summary>Local UTC offset (e.g. "UTC+02:00") — coarse region hint.</summary>
        public string Timezone { get; }

        /// <summary>Coarse locale / system language (e.g. "English").</summary>
        public string Locale { get; }

        /// <summary>Application bundle / package identifier.</summary>
        public string BundleId { get; }

        /// <summary>Event-specific key/value payload (a defensive snapshot owned by this event).</summary>
        public IReadOnlyDictionary<string, object> Payload { get; }

        public TrackingEvent(
            string id,
            string type,
            DateTimeOffset timestampUtc,
            string sessionId,
            string userId,
            string sdkVersion,
            string platform,
            string appVersion,
            string deviceModel,
            string osVersion,
            string networkType,
            string timezone,
            string locale,
            string bundleId,
            IReadOnlyDictionary<string, object> payload)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Type = type ?? throw new ArgumentNullException(nameof(type));
            TimestampUtc = timestampUtc;
            SessionId = sessionId ?? string.Empty;
            UserId = userId ?? string.Empty;
            SdkVersion = sdkVersion ?? string.Empty;
            Platform = platform ?? string.Empty;
            AppVersion = appVersion ?? string.Empty;
            DeviceModel = deviceModel ?? string.Empty;
            OsVersion = osVersion ?? string.Empty;
            NetworkType = networkType ?? string.Empty;
            Timezone = timezone ?? string.Empty;
            Locale = locale ?? string.Empty;
            BundleId = bundleId ?? string.Empty;
            Payload = payload ?? EmptyPayload;
        }

        private static readonly IReadOnlyDictionary<string, object> EmptyPayload =
            new Dictionary<string, object>(0);

        public override string ToString() => $"TrackingEvent({Type} #{Id} @ {TimestampUtc:O})";
    }

    /// <summary>Well-known values for <see cref="TrackingEvent.Type"/>.</summary>
    public static class TrackingEventType
    {
        /// <summary>Produced by <see cref="ITracker.SendMessage"/>.</summary>
        public const string MESSAGE = "message";

        /// <summary>Produced by <see cref="ITracker.SendMapAsync"/>.</summary>
        public const string MAP = "map";
    }
}
