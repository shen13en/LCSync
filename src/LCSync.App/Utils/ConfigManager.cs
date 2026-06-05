using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using LCSync.Models;

namespace LCSync.Utils;

public static class ConfigManager
{
    private static StorageConfig? _cached;
    private static readonly object _lock = new();

    public static StorageConfig Load()
    {
        if (_cached != null)
            return _cached;

        lock (_lock)
        {
            if (_cached != null)
                return _cached;

            var configPath = StorageConfig.GetDefaultConfigPath();
            var dir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath, Encoding.UTF8);
                    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
                    var serializer = new DataContractJsonSerializer(typeof(StorageConfig));
                    var config = (StorageConfig)serializer.ReadObject(ms)!;
                    _cached = config;
                    return config;
                }
                catch
                {
                    // fall through to default
                }
            }

            _cached = new StorageConfig
            {
                SharedDirectory = StorageConfig.GetDefaultSharedDir(),
                SubmissionDirectory = StorageConfig.GetDefaultSubmissionDir(),
                MaxBandwidthBytesPerSecond = 0
            };
            return _cached;
        }
    }

    public static void Save(StorageConfig config)
    {
        lock (_lock)
        {
            _cached = config;

            var configPath = StorageConfig.GetDefaultConfigPath();
            var dir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var ms = new MemoryStream();
            var serializer = new DataContractJsonSerializer(typeof(StorageConfig));
            serializer.WriteObject(ms, config);
            var json = Encoding.UTF8.GetString(ms.ToArray());
            File.WriteAllText(configPath, json, Encoding.UTF8);
        }
    }

    public static void EnsureDirectories(StorageConfig config)
    {
        if (!string.IsNullOrEmpty(config.SharedDirectory) && !Directory.Exists(config.SharedDirectory))
            Directory.CreateDirectory(config.SharedDirectory);

        if (!string.IsNullOrEmpty(config.SubmissionDirectory) && !Directory.Exists(config.SubmissionDirectory))
            Directory.CreateDirectory(config.SubmissionDirectory);
    }
}
