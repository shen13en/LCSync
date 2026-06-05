using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using LCSync.Models;

namespace LCSync.Services;

public class FileServerService : IDisposable
{
    private bool _disposed;
    private HttpListener? _listener;
    private readonly int _port;
    private CancellationTokenSource? _cts;

    private readonly List<FileItem> _sharedFiles = new();
    private readonly List<SubmissionItem> _submissions = new();
    private readonly object _filesLock = new();
    private readonly object _submissionsLock = new();

    private StorageConfig _config;
    private readonly string _metadataPath;

    // 令牌桶带宽控制
    private long _availableBytes;
    private DateTime _lastRefill = DateTime.Now;
    private readonly object _throttleLock = new();

    public event EventHandler<FileItem>? FileShared;
    public event EventHandler<SubmissionItem>? SubmissionReceived;
    public event EventHandler<string>? ErrorOccurred;

    public IReadOnlyList<FileItem> SharedFiles
    {
        get { lock (_filesLock) return _sharedFiles.ToArray(); }
    }

    public IReadOnlyList<SubmissionItem> Submissions
    {
        get { lock (_submissionsLock) return _submissions.ToArray(); }
    }

    public StorageConfig Config => _config;

    public bool IsRunning { get; private set; }

    public FileServerService(int port)
    {
        _port = port;
        _config = Utils.ConfigManager.Load();
        Utils.ConfigManager.EnsureDirectories(_config);

        _metadataPath = Path.Combine(
            Path.GetDirectoryName(StorageConfig.GetDefaultConfigPath()) ?? ".",
            "fileMetadata.json");
        LoadMetadata();
    }

    public void UpdateConfig(StorageConfig newConfig)
    {
        _config = newConfig;
        Utils.ConfigManager.EnsureDirectories(_config);
        Utils.ConfigManager.Save(_config);
        SaveMetadata();
    }

    public void Start()
    {
        if (IsRunning) return;

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{_port}/");
            _listener.Start();
            IsRunning = true;

            _cts = new CancellationTokenSource();
            var thread = new Thread(() => AcceptLoop(_cts.Token))
            {
                IsBackground = true,
                Name = "FileServer"
            };
            thread.Start();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"文件服务启动失败: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;

        IsRunning = false;
        _cts?.Cancel();

        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
    }

    private void AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = _listener.GetContext();
                if (token.IsCancellationRequested) break;
                ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
            }
            catch (HttpListenerException) when (!_listener.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;
            var path = request.Url?.AbsolutePath?.Trim('/') ?? "";
            var method = request.HttpMethod.ToUpperInvariant();

            response.AppendHeader("Access-Control-Allow-Origin", "*");

            // 路由分发
            if (method == "GET" && path == "api/files")
                HandleGetFileList(response);
            else if (method == "GET" && path.StartsWith("api/files/"))
                HandleDownloadFile(response, path.Substring("api/files/".Length), request);
            else if (method == "POST" && path == "api/teacher/upload")
                HandleTeacherUpload(request, response);
            else if (method == "POST" && path == "api/upload")
                HandleStudentUpload(request, response);
            else if (method == "GET" && path == "api/submissions")
                HandleGetSubmissions(response);
            else if (method == "GET" && path.StartsWith("api/submissions/"))
                HandleDownloadSubmission(response, path.Substring("api/submissions/".Length));
            else if (method == "DELETE" && path.StartsWith("api/files/"))
                HandleDeleteFile(response, path.Substring("api/files/".Length));
            else if (method == "GET" && path == "api/student/info")
                HandleStudentInfo(response, request);
            else
                RespondJson(response, 404, "{\"error\":\"Not found\"}");
        }
        catch (Exception ex)
        {
            try
            {
                RespondJson(context.Response, 500, $"{{\"error\":\"{EscapeJson(ex.Message)}\"}}");
            }
            catch { }
        }
    }

    // ── API 处理桩方法 (将在 Task 5 中实现) ──────────────────────

    private void HandleGetFileList(HttpListenerResponse response)
    {
        RespondError(response, 501, "Not yet implemented");
    }

    private void HandleDownloadFile(HttpListenerResponse response, string fileId, HttpListenerRequest request)
    {
        RespondError(response, 501, "Not yet implemented");
    }

    private void HandleTeacherUpload(HttpListenerRequest request, HttpListenerResponse response)
    {
        RespondError(response, 501, "Not yet implemented");
    }

    private void HandleStudentUpload(HttpListenerRequest request, HttpListenerResponse response)
    {
        RespondError(response, 501, "Not yet implemented");
    }

    private void HandleGetSubmissions(HttpListenerResponse response)
    {
        RespondError(response, 501, "Not yet implemented");
    }

    private void HandleDownloadSubmission(HttpListenerResponse response, string submissionId)
    {
        RespondError(response, 501, "Not yet implemented");
    }

    private void HandleDeleteFile(HttpListenerResponse response, string fileId)
    {
        RespondError(response, 501, "Not yet implemented");
    }

    private void HandleStudentInfo(HttpListenerResponse response, HttpListenerRequest request)
    {
        RespondError(response, 501, "Not yet implemented");
    }

    // ── 辅助方法 ───────────────────────────────────────────────

    private void RespondJson(HttpListenerResponse response, int statusCode, string json)
    {
        var buffer = Encoding.UTF8.GetBytes(json);
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }

    private void RespondError(HttpListenerResponse response, int statusCode, string message)
    {
        RespondJson(response, statusCode, $"{{\"error\":\"{EscapeJson(message)}\"}}");
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private static string GetMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed",
            ".txt" => "text/plain; charset=utf-8",
            ".cpp" or ".c" => "text/plain; charset=utf-8",
            ".py" => "text/plain; charset=utf-8",
            _ => "application/octet-stream"
        };
    }

    // ── 令牌桶带宽控制 ─────────────────────────────────────────

    private void ApplyThrottle(int bytesToSend)
    {
        if (_config.MaxBandwidthBytesPerSecond <= 0) return;

        lock (_throttleLock)
        {
            var now = DateTime.Now;
            var elapsed = (now - _lastRefill).TotalSeconds;
            _availableBytes = Math.Min(_config.MaxBandwidthBytesPerSecond,
                _availableBytes + (long)(_config.MaxBandwidthBytesPerSecond * elapsed));
            _lastRefill = now;

            if (_availableBytes < bytesToSend)
            {
                var deficit = bytesToSend - _availableBytes;
                var delayMs = (int)(deficit * 1000.0 / _config.MaxBandwidthBytesPerSecond) + 1;
                Thread.Sleep(Math.Min(delayMs, 1000));
                _availableBytes = 0;
                _lastRefill = DateTime.Now;
            }
            else
            {
                _availableBytes -= bytesToSend;
            }
        }
    }

    // ── 元数据持久化 ───────────────────────────────────────────

    private void SaveMetadata()
    {
        try
        {
            var data = new MetadataWrapper
            {
                Files = _sharedFiles,
                Submissions = _submissions
            };
            using var ms = new MemoryStream();
            var serializer = new DataContractJsonSerializer(typeof(MetadataWrapper));
            serializer.WriteObject(ms, data);
            File.WriteAllText(_metadataPath, Encoding.UTF8.GetString(ms.ToArray()), Encoding.UTF8);
        }
        catch { }
    }

    private void LoadMetadata()
    {
        try
        {
            if (!File.Exists(_metadataPath)) return;
            var json = File.ReadAllText(_metadataPath, Encoding.UTF8);
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var serializer = new DataContractJsonSerializer(typeof(MetadataWrapper));
            var data = (MetadataWrapper)serializer.ReadObject(ms)!;
            lock (_filesLock) { _sharedFiles.Clear(); _sharedFiles.AddRange(data.Files ?? new List<FileItem>()); }
            lock (_submissionsLock) { _submissions.Clear(); _submissions.AddRange(data.Submissions ?? new List<SubmissionItem>()); }
        }
        catch { }
    }

    // ── IDisposable ────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        SaveMetadata();
    }
}

// IMPORTANT: MetadataWrapper uses DataContractJsonSerializer - needs [DataContract]/[DataMember]
[System.Runtime.Serialization.DataContract]
internal class MetadataWrapper
{
    [System.Runtime.Serialization.DataMember]
    public List<FileItem> Files { get; set; } = new();

    [System.Runtime.Serialization.DataMember]
    public List<SubmissionItem> Submissions { get; set; } = new();
}
