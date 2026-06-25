using System;
using UnityEngine;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Logging hook. The SDK never writes to the console directly — it routes everything through this
    /// seam, so a host app can forward diagnostics to its own logging stack (or silence them entirely).
    /// </summary>
    public interface ITrackingLogger
    {
        void Log(TrackingLogLevel level, string message, Exception exception = null);
    }

    /// <summary>Default logger that forwards to UnityEngine's <see cref="Debug"/>.</summary>
    public sealed class UnityTrackingLogger : ITrackingLogger
    {
        public static readonly UnityTrackingLogger Instance = new UnityTrackingLogger();
        private const string PREFIX = "[DmytroUdovychenko.Tracking] ";

        public void Log(TrackingLogLevel level, string message, Exception exception = null)
        {
            switch (level)
            {
                case TrackingLogLevel.Error:
                    if (exception != null)
                    {
                        Debug.LogException(exception);
                    }
                    else
                    {
                        Debug.LogError(PREFIX + message);
                    }
                    break;
                case TrackingLogLevel.Warning:
                    Debug.LogWarning(PREFIX + message);
                    break;
                default:
                    Debug.Log(PREFIX + message);
                    break;
            }
        }
    }

    /// <summary>Logger that discards everything. Useful for tests and for opting out of SDK logs.</summary>
    public sealed class NullTrackingLogger : ITrackingLogger
    {
        public static readonly NullTrackingLogger Instance = new NullTrackingLogger();

        public void Log(TrackingLogLevel level, string message, Exception exception = null) { }
    }

    /// <summary>Decorator that forwards to an inner logger only messages at or above a minimum severity.</summary>
    public sealed class LevelFilteringTrackingLogger : ITrackingLogger
    {
        private readonly ITrackingLogger m_inner;
        private readonly TrackingLogLevel m_minLevel;

        public LevelFilteringTrackingLogger(ITrackingLogger inner, TrackingLogLevel minLevel)
        {
            m_inner = inner ?? NullTrackingLogger.Instance;
            m_minLevel = minLevel;
        }

        public void Log(TrackingLogLevel level, string message, Exception exception = null)
        {
            if (level < m_minLevel)
            {
                return;
            }
            m_inner.Log(level, message, exception);
        }
    }
}
