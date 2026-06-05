using System;

namespace LCSync.Models;

public class SubmissionItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StudentName { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime SubmitTime { get; set; }
    public string StoragePath { get; set; } = string.Empty;
}
