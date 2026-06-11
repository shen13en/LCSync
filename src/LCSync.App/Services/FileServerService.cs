using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    // 功能开关（由教师端 UI 控制）
    internal bool _isFileSharingActive;
    internal bool _isSubmissionActive;

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

    public bool RemoveFileById(string fileId)
    {
        System.Diagnostics.Debug.WriteLine($"RemoveFileById: looking for {fileId}");
        lock (_filesLock)
        {
            var item = _sharedFiles.Find(f => f.Id == fileId);
            System.Diagnostics.Debug.WriteLine(item == null ? "RemoveFileById: not found" : $"RemoveFileById: found {item.FileName}");
            if (item == null) return false;
            _sharedFiles.Remove(item);

            var filePath = Path.Combine(_config.SharedDirectory, item.FileName);
            System.Diagnostics.Debug.WriteLine($"RemoveFileById: deleting {filePath}");
            try { if (File.Exists(filePath)) File.Delete(filePath); } catch { }
            System.Diagnostics.Debug.WriteLine("RemoveFileById: done");
            return true;
        }
    }

    public FileServerService(int port)
    {
        _port = port;
        _config = Utils.ConfigManager.Load();
        Utils.ConfigManager.EnsureDirectories(_config);
        _metadataPath = "";
        // 不加载元数据，每次启动从磁盘重新扫描
    }

    public void UpdateConfig(StorageConfig newConfig)
    {
        _config = newConfig;
        Utils.ConfigManager.EnsureDirectories(_config);
        Utils.ConfigManager.Save(_config);
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

    // ── API 处理方法 ───────────────────────────────────────────

    private void HandleGetFileList(HttpListenerResponse response)
    {
        lock (_filesLock)
        {
            var serializer = new DataContractJsonSerializer(typeof(List<FileItem>));
            using var ms = new MemoryStream();
            serializer.WriteObject(ms, _sharedFiles);
            var json = Encoding.UTF8.GetString(ms.ToArray());
            RespondJson(response, 200, json);
        }
    }

    private void HandleDownloadFile(HttpListenerResponse response, string fileId, HttpListenerRequest request)
    {
        FileItem? item;
        lock (_filesLock)
        {
            item = _sharedFiles.Find(f => f.Id == fileId);
        }

        if (item == null)
        {
            RespondError(response, 404, "File not found");
            return;
        }

        var filePath = Path.Combine(_config.SharedDirectory, item.FileName);
        if (!File.Exists(filePath))
        {
            RespondError(response, 404, "File not found on disk");
            return;
        }

        var fileInfo = new FileInfo(filePath);
        var mimeType = GetMimeType(Path.GetExtension(item.FileName));
        response.ContentType = mimeType;
        response.AppendHeader("Accept-Ranges", "bytes");
        response.AppendHeader("Content-Disposition", $"attachment; filename=\"{Uri.EscapeDataString(item.FileName)}\"");

        var rangeHeader = request.Headers["Range"];
        long startByte = 0;
        long endByte = fileInfo.Length - 1;

        if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes="))
        {
            var range = rangeHeader.Substring("bytes=".Length).Split('-');
            if (range.Length > 0 && long.TryParse(range[0], out startByte))
            {
                if (range.Length > 1 && long.TryParse(range[1], out var parsedEnd))
                    endByte = Math.Min(parsedEnd, fileInfo.Length - 1);

                response.StatusCode = 206;
                response.AppendHeader("Content-Range", $"bytes {startByte}-{endByte}/{fileInfo.Length}");
            }
        }

        var contentLength = endByte - startByte + 1;
        response.ContentLength64 = contentLength;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
            fs.Seek(startByte, SeekOrigin.Begin);
            var buffer = new byte[81920];
            long remaining = contentLength;
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = fs.Read(buffer, 0, toRead);
                if (read == 0) break;

                ApplyThrottle(read);

                response.OutputStream.Write(buffer, 0, read);
                remaining -= read;
            }

            // 更新下载计数
            if (startByte == 0)
            {
                lock (_filesLock)
                {
                    item.DownloadCount++;
                }
            }
        }
        catch (HttpListenerException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            try { response.OutputStream.Close(); } catch { }
        }
    }

    private static string GetQueryParam(string rawUrl, string paramName)
    {
        if (string.IsNullOrEmpty(rawUrl)) return "";
        var queryStart = rawUrl.IndexOf('?');
        if (queryStart < 0) return "";
        var query = rawUrl.Substring(queryStart + 1);
        var parts = query.Split('&');
        foreach (var part in parts)
        {
            if (part.StartsWith(paramName + "=", StringComparison.OrdinalIgnoreCase))
            {
                var val = part.Substring(paramName.Length + 1);
                return System.Web.HttpUtility.UrlDecode(val, Encoding.UTF8);
            }
        }
        return "";
    }

    private void HandleTeacherUpload(HttpListenerRequest request, HttpListenerResponse response)
    {
        // 检查文件共享是否开启
        if (!_isFileSharingActive)
        {
            RespondError(response, 403, "File sharing is disabled");
            return;
        }

        var fileName = GetQueryParam(request.RawUrl, "filename");
        if (string.IsNullOrEmpty(fileName))
        {
            RespondError(response, 400, "No file provided");
            return;
        }

        // 读取原始请求体作为文件数据
        var fileData = ReadRequestBody(request);
        if (fileData.Length == 0)
        {
            RespondError(response, 400, "Empty file");
            return;
        }

        var item = new FileItem
        {
            FileName = fileName,
            Size = fileData.Length,
            UploadTime = DateTime.Now
        };

        var savePath = Path.Combine(_config.SharedDirectory, fileName);
        try
        {
            File.WriteAllBytes(savePath, fileData);
        }
        catch (Exception ex)
        {
            RespondError(response, 500, $"Failed to save: {ex.Message}");
            return;
        }

        lock (_filesLock)
        {
            _sharedFiles.Add(item);
        }

        FileShared?.Invoke(this, item);

        var serializer = new DataContractJsonSerializer(typeof(FileItem));
        using var ms = new MemoryStream();
        serializer.WriteObject(ms, item);
        var json = Encoding.UTF8.GetString(ms.ToArray());
        RespondJson(response, 200, json);
    }

    private void HandleStudentUpload(HttpListenerRequest request, HttpListenerResponse response)
    {
        // 检查作业提交是否开启
        if (!_isSubmissionActive)
        {
            RespondError(response, 403, "Submission is disabled");
            return;
        }

        var bodyBytes = ReadRequestBody(request);

        // 从 QueryString 获取参数（用 RawUrl 避免编码问题）
        var fileName = GetQueryParam(request.RawUrl, "filename");
        var studentName = GetQueryParam(request.RawUrl, "studentName");
        var studentId = GetQueryParam(request.RawUrl, "studentId");

        if (string.IsNullOrEmpty(studentName) || string.IsNullOrEmpty(studentId))
        {
            RespondError(response, 400, "studentName and studentId are required");
            return;
        }

        if (string.IsNullOrEmpty(fileName) || bodyBytes.Length == 0)
        {
            RespondError(response, 400, "No file provided");
            return;
        }

        // 限制单文件 200MB
        if (bodyBytes.Length > 200 * 1024 * 1024)
        {
            RespondError(response, 413, "File too large (max 200MB)");
            return;
        }

        var safeName = $"{SanitizeFileName(studentId)}_{SanitizeFileName(studentName)}_{SanitizeFileName(fileName)}";
        var savePath = Path.Combine(_config.SubmissionDirectory, safeName);

        var item = new SubmissionItem
        {
            StudentName = studentName,
            StudentId = studentId,
            FileName = fileName,
            Size = bodyBytes.Length,
            SubmitTime = DateTime.Now,
            StoragePath = savePath
        };

        try
        {
            File.WriteAllBytes(savePath, bodyBytes);
        }
        catch (Exception ex)
        {
            RespondError(response, 500, $"Failed to save: {ex.Message}");
            return;
        }

        lock (_submissionsLock)
        {
            _submissions.Add(item);
        }

        SubmissionReceived?.Invoke(this, item);

        var serializer = new DataContractJsonSerializer(typeof(SubmissionItem));
        using var ms = new MemoryStream();
        serializer.WriteObject(ms, item);
        var json = Encoding.UTF8.GetString(ms.ToArray());
        RespondJson(response, 200, json);
    }

    private void HandleGetSubmissions(HttpListenerResponse response)
    {
        lock (_submissionsLock)
        {
            var serializer = new DataContractJsonSerializer(typeof(List<SubmissionItem>));
            using var ms = new MemoryStream();
            serializer.WriteObject(ms, _submissions);
            var json = Encoding.UTF8.GetString(ms.ToArray());
            RespondJson(response, 200, json);
        }
    }

    private void HandleDownloadSubmission(HttpListenerResponse response, string submissionId)
    {
        SubmissionItem? item;
        lock (_submissionsLock)
        {
            item = _submissions.Find(s => s.Id == submissionId);
        }

        if (item == null || !File.Exists(item.StoragePath))
        {
            RespondError(response, 404, "Submission not found");
            return;
        }

        var fileInfo = new FileInfo(item.StoragePath);
        var mimeType = GetMimeType(Path.GetExtension(item.FileName));
        response.ContentType = mimeType;
        response.ContentLength64 = fileInfo.Length;
        response.AppendHeader("Content-Disposition", $"attachment; filename=\"{Uri.EscapeDataString(item.FileName)}\"");

        try
        {
            using var fs = new FileStream(item.StoragePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
            fs.CopyTo(response.OutputStream);
        }
        catch (HttpListenerException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            try { response.OutputStream.Close(); } catch { }
        }
    }

    private void HandleDeleteFile(HttpListenerResponse response, string fileId)
    {
        FileItem? item;
        lock (_filesLock)
        {
            item = _sharedFiles.Find(f => f.Id == fileId);
            if (item == null)
            {
                RespondError(response, 404, "File not found");
                return;
            }
            _sharedFiles.Remove(item);
        }

        var filePath = Path.Combine(_config.SharedDirectory, item.FileName);
        try { if (File.Exists(filePath)) File.Delete(filePath); } catch { }

        RespondJson(response, 200, "{\"status\":\"deleted\"" + "}");
    }

    private void HandleStudentInfo(HttpListenerResponse response, HttpListenerRequest request)
    {
        var remoteIp = request.RemoteEndPoint?.Address?.ToString() ?? "unknown";
        RespondJson(response, 200, $"{{\"remoteIp\":\"{remoteIp}\"}}");
    }

    // ── 辅助方法 ───────────────────────────────────────────────

    private static byte[] ReadRequestBody(HttpListenerRequest request)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = request.InputStream.Read(buffer, 0, buffer.Length)) > 0)
            ms.Write(buffer, 0, read);
        return ms.ToArray();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sanitized = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c >= 32 && !invalid.Contains(c))
                sanitized.Append(c);
            else
                sanitized.Append('_');
        }
        return sanitized.ToString();
    }

    private static bool TryParseMultipart(byte[] body, string contentType, out string fileName, out byte[] fileData)
    {
        fileName = "";
        fileData = null;

        if (string.IsNullOrEmpty(contentType) || body == null || body.Length == 0)
            return false;

        // 提取 boundary
        string boundary = null;
        var ctParts = contentType.Split(';');
        foreach (var p in ctParts)
        {
            var t = p.Trim();
            if (t.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
                boundary = t.Substring("boundary=".Length).Trim('"');
        }
        if (boundary == null) return false;

        var delimiterBytes = Encoding.UTF8.GetBytes("--" + boundary);
        var headerEndMarker = Encoding.UTF8.GetBytes("\r\n\r\n");

        // 按 boundary 分割
        var parts = SplitBytes(body, delimiterBytes);

        foreach (var part in parts)
        {
            if (part.Length < 20) continue;

            // 找头部结束位置
            int headerEnd = IndexOfBytes(part, headerEndMarker);
            if (headerEnd < 0) continue;

            var header = Encoding.UTF8.GetString(part, 0, Math.Min(headerEnd + 4, part.Length));

            // 检查是否有 filename
            int fnIdx = IndexOfBytes(part, Encoding.UTF8.GetBytes("filename=\""), 0, StringComparison.OrdinalIgnoreCase);
            if (fnIdx < 0) continue;

            // 从 header 正则提取文件名
            var fnMatch = System.Text.RegularExpressions.Regex.Match(header,
                @"filename\s*=\s*""([^""]*)""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (fnMatch.Success)
                fileName = fnMatch.Groups[1].Value;

            if (string.IsNullOrEmpty(fileName)) continue;

            // 提取文件数据
            int dataStart = headerEnd + headerEndMarker.Length;
            while (dataStart < part.Length && (part[dataStart] == '\r' || part[dataStart] == '\n'))
                dataStart++;

            int dataEnd = part.Length;
            // 去掉尾部标记: --\r\n 或 \r\n
            while (dataEnd > dataStart && (part[dataEnd - 1] == '\n' || part[dataEnd - 1] == '\r' || part[dataEnd - 1] == '-'))
                dataEnd--;

            if (dataEnd <= dataStart) continue;

            fileData = new byte[dataEnd - dataStart];
            Buffer.BlockCopy(part, dataStart, fileData, 0, fileData.Length);
            return true;
        }

        return false;
    }

    private static bool TryParseMultipartWithFields(byte[] body, string contentType,
        out string studentName, out string studentId, out string fileName, out byte[] fileData)
    {
        studentName = "";
        studentId = "";
        fileName = "";
        fileData = null;

        if (string.IsNullOrEmpty(contentType) || body == null || body.Length == 0)
            return false;

        // 提取 boundary
        string boundary = null;
        var ctParts = contentType.Split(';');
        foreach (var p in ctParts)
        {
            var t = p.Trim();
            if (t.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
                boundary = t.Substring("boundary=".Length).Trim('"');
        }
        if (boundary == null) return false;

        var delimiterBytes = Encoding.UTF8.GetBytes("--" + boundary);
        var headerEndMarker = Encoding.UTF8.GetBytes("\r\n\r\n");

        var parts = SplitBytes(body, delimiterBytes);

        foreach (var part in parts)
        {
            if (part.Length < 20) continue;

            int headerEnd = IndexOfBytes(part, headerEndMarker);
            if (headerEnd < 0) continue;

            int dataStart = headerEnd + headerEndMarker.Length;
            while (dataStart < part.Length && (part[dataStart] == '\r' || part[dataStart] == '\n'))
                dataStart++;

            int dataEnd = part.Length;
            while (dataEnd > dataStart && (part[dataEnd - 1] == '\n' || part[dataEnd - 1] == '\r' || part[dataEnd - 1] == '-'))
                dataEnd--;

            var header = Encoding.UTF8.GetString(part, 0, Math.Min(headerEnd + 4, part.Length));

            // 检查是否是 file 部分
            if (header.IndexOf("filename", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var fnMatch = System.Text.RegularExpressions.Regex.Match(header,
                    @"filename\s*=\s*""([^""]*)""",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (fnMatch.Success) fileName = fnMatch.Groups[1].Value;

                if (dataEnd > dataStart)
                {
                    fileData = new byte[dataEnd - dataStart];
                    Buffer.BlockCopy(part, dataStart, fileData, 0, fileData.Length);
                }
            }
            else
            {
                // 文本字段
                var nameMatch = System.Text.RegularExpressions.Regex.Match(header,
                    @"name\s*=\s*""([^""]*)""",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (nameMatch.Success)
                {
                    var value = Encoding.UTF8.GetString(part, dataStart, Math.Min(dataEnd - dataStart, 500)).Trim();
                    if (nameMatch.Groups[1].Value == "studentName")
                        studentName = value;
                    else if (nameMatch.Groups[1].Value == "studentId")
                        studentId = value;
                }
            }
        }

        return !string.IsNullOrEmpty(fileName) && fileData != null;
    }

    private static int IndexOfBytes(byte[] source, byte[] pattern, int startIndex = 0)
    {
        for (int i = startIndex; i <= source.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }

    private static int IndexOfBytes(byte[] source, byte[] pattern, int startIndex, StringComparison comparison)
    {
        for (int i = startIndex; i <= source.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                // 不区分大小写比较
                char sc = (char)source[i + j];
                char pc = (char)pattern[j];
                if (sc >= 'A' && sc <= 'Z') sc = (char)(sc + 32);
                if (pc >= 'A' && pc <= 'Z') pc = (char)(pc + 32);
                if (sc != pc) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }

    private static byte[][] SplitBytes(byte[] source, byte[] separator)
    {
        var result = new List<byte[]>();
        int start = 0;
        while (start < source.Length)
        {
            int idx = IndexOfBytes(source, separator, start);
            if (idx < 0) break;
            var segment = new byte[idx - start];
            Buffer.BlockCopy(source, start, segment, 0, segment.Length);
            if (segment.Length > 0)
                result.Add(segment);
            start = idx + separator.Length;
        }
        return result.ToArray();
    }

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

    // ── IDisposable ────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}

