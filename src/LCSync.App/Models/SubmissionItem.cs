using System;
using System.Runtime.Serialization;

namespace LCSync.Models;

[DataContract]
public class SubmissionItem
{
    [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [DataMember] public string StudentName { get; set; } = string.Empty;
    [DataMember] public string StudentId { get; set; } = string.Empty;
    [DataMember] public string FileName { get; set; } = string.Empty;
    [DataMember] public long Size { get; set; }
    [DataMember] public DateTime SubmitTime { get; set; }
    [DataMember] public string StoragePath { get; set; } = string.Empty;
}
