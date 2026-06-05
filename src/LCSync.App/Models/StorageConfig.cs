using System;
using System.IO;

namespace LCSync.Models;

public class StorageConfig
{
    public string SharedDirectory { get; set; } = string.Empty;
    public string SubmissionDirectory { get; set; } = string.Empty;
    public long MaxBandwidthBytesPerSecond { get; set; } = 0;

    public static string GetDefaultSharedDir()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LCSync", "Shared");
    }

    public static string GetDefaultSubmissionDir()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LCSync", "Uploads");
    }

    public static string GetDefaultConfigPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LCSync", "settings.json");
    }
}
