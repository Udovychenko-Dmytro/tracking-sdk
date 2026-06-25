using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Durable <see cref="IEventStore"/> backed by a JSON file (default: under
    /// <see cref="Application.persistentDataPath"/>). Writes are atomic (temp file + move) and reads are
    /// resilient: a missing or corrupt file is treated as "no backlog" rather than throwing.
    /// </summary>
    public sealed class FileEventStore : IEventStore
    {
        public const string DEFAULT_FILE_NAME = "tracking-events.json";

        private readonly string m_path;
        private readonly object m_gate = new object();
        private readonly ITrackingLogger m_logger;

        /// <param name="filePath">Full file path; defaults to persistentDataPath/<see cref="DEFAULT_FILE_NAME"/>.</param>
        /// <param name="logger">Diagnostics sink for I/O failures; defaults to <see cref="UnityTrackingLogger"/>.</param>
        public FileEventStore(string filePath = null, ITrackingLogger logger = null)
        {
            m_path = string.IsNullOrEmpty(filePath)
                ? Path.Combine(Application.persistentDataPath, DEFAULT_FILE_NAME)
                : filePath;
            m_logger = logger ?? UnityTrackingLogger.Instance;
        }

        public string FilePath => m_path;

        public void Save(IReadOnlyList<TrackingEvent> events)
        {
            try
            {
                string json = EventSerializer.ToJson(events);
                lock (m_gate)
                {
                    string dir = Path.GetDirectoryName(m_path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    string tempPath = m_path + ".tmp";
                    File.WriteAllText(tempPath, json);
                    if (File.Exists(m_path))
                    {
                        // File.Replace is atomic and never leaves the destination absent — unlike
                        // delete-then-move, a crash here cannot lose the existing backlog.
                        File.Replace(tempPath, m_path, null);
                    }
                    else
                    {
                        File.Move(tempPath, m_path);
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.Log(TrackingLogLevel.Error, "FileEventStore.Save failed", e);
            }
        }

        public IReadOnlyList<TrackingEvent> Load()
        {
            try
            {
                lock (m_gate)
                {
                    if (!File.Exists(m_path)) return Array.Empty<TrackingEvent>();
                    string json = File.ReadAllText(m_path);
                    return EventSerializer.FromJson(json);
                }
            }
            catch (Exception e)
            {
                m_logger.Log(TrackingLogLevel.Error, "FileEventStore.Load failed", e);
                return Array.Empty<TrackingEvent>();
            }
        }

        public void Clear()
        {
            try
            {
                lock (m_gate)
                {
                    if (File.Exists(m_path))
                    {
                        File.Delete(m_path);
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.Log(TrackingLogLevel.Error, "FileEventStore.Clear failed", e);
            }
        }
    }
}
