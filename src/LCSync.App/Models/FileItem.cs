using System;
using System.Runtime.Serialization;

namespace LCSync.Models;

[DataContract]
public class FileItem
{
    [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [DataMember] public string FileName { get; set; } = string.Empty;
    [DataMember] public long Size { get; set; }
    [DataMember] public DateTime UploadTime { get; set; }
    [DataMember] public int DownloadCount { get; set; }
}
