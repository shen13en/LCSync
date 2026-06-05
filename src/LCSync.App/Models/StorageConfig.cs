using System;
using System.IO;
using System.Runtime.Serialization;

namespace LCSync.Models;

[DataContract]
public class StorageConfig
{
    [DataMember] public string SharedDirectory { get; set; } = string.Empty;
    [DataMember] public string SubmissionDirectory { get; set; } = string.Empty;
    [DataMember] public long MaxBandwidthBytesPerSecond { get; set; } = 0;

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
            Environment.GetFolderPath(Environ