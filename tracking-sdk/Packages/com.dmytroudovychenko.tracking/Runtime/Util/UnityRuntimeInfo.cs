using System;
using UnityEngine;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>Production <see cref="IRuntimeInfo"/> backed by UnityEngine's <see cref="Application"/> + <see cref="SystemInfo"/>.</summary>
    /// <remarks>Values are snapshotted in the constructor (main thread at Init): some sources — notably
    /// <see cref="Application.internetReachability"/> — are main-thread-only and throw off the main thread.</remarks>
    public sealed class UnityRuntimeInfo : IRuntimeInfo
    {
        public UnityRuntimeInfo()
        {
            Platform = Application.platform.ToString();
            AppVersion = Application.version;
            DeviceModel = SystemInfo.deviceModel;
            OsVersion = SystemInfo.operatingSystem;
            NetworkType = NetworkTypeOf(Application.internetReachability);
            Timezone = LocalUtcOffset();
            Locale = Application.systemLanguage.ToString();
            BundleId = Application.identifier;
        }

        public string Platform { get; }

        public string AppVersion { get; }

        public string DeviceModel { get; }

        public string OsVersion { get; }

        public string NetworkType { get; }

        public string Timezone { get; }

        public string Locale { get; }

        public string BundleId { get; }

        private static string NetworkTypeOf(NetworkReachability reachability)
        {
            switch (reachability)
            {
                case NetworkReachability.ReachableViaLocalAreaNetwork: return "wifi";
                case NetworkReachability.ReachableViaCarrierDataNetwork: return "cellular";
                default: return "none";
            }
        }

        // Coarse local UTC offset (DST-aware for "now") formatted as "UTC±HH:MM"; deliberately no IANA id.
        private static string LocalUtcOffset()
        {
            TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
            string sign = offset < TimeSpan.Zero ? "-" : "+";
            return $"UTC{sign}{Math.Abs(offset.Hours):00}:{Math.Abs(offset.Minutes):00}";
        }
    }
}
