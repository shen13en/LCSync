using System;

namespace LCSync.Models;

public class FileItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime UploadTime { get; set; }
    public int DownloadCount { get; set; }
}
