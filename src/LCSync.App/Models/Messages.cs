using System;

namespace LCSync.Models;

public enum MessageType : byte
{
    Join = 0x01,
    Joined = 0x02,
    Leave = 0x03,
    PeerCount = 0x04,
    VideoFrame = 0x10,
    FileNotify = 0x30,      // 教师通知学生有新文件
    SubmissionNotify = 0x31, // 学生通知教师已提交
    Error = 0xFF
}

public class JoinMessage
{
    public string StudentId { get; set; } = string.Empty;
}

public class JoinedMessage
{
    public string StudentId { get; set; } = string.Empty;
}

public class PeerCountMessage
{
    public int Count { get; set; }
}

public class VideoFrameMessage
{
    public byte[] Data { get; set; } = new byte[0];
    public long Timestamp { get; set; }
    public bool IsKeyFrame { get; set; }
}

public class ErrorMessage
{
    public string Message { get; set; } = string.Empty;
}
