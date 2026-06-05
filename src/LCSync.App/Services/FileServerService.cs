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

        var filePath = Path.Combine(_config.SharedDirectory, item.Id + "_" + item.FileName);
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
                    SaveMetadata();
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

    private void HandleTeacherUpload(HttpListenerRequest request, HttpListenerResponse response)
    {
        var fileName = ExtractFileName(request);
        if (string.IsNullOrEmpty(fileName))
        {
            RespondError(response, 400, "No file provided");
            return;
        }

        var fileData = ExtractFileData(request);
        if (fileData == null || fileData.Length == 0)
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

        var savePath = Path.Combine(_config.SharedDirectory, item.Id + "_" + fileName);
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
            SaveMetadata();
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
        var studentName = ExtractFormField(request, "studentName");
        var studentId = ExtractFormField(request, "studentId");
        var fileName = ExtractFileName(request);
        var fileData = ExtractFileData(request);

        if (string.IsNullOrEmpty(studentName) || string.IsNullOrEmpty(studentId))
        {
            RespondError(response, 400, "studentName and studentId are required");
            return;
        }

        if (string.IsNullOrEmpty(fileName) || fileData == null || fileData.Length == 0)
        {
            RespondError(response, 400, "No file provided");
            return;
        }

        // 限制单文件 200MB
        if (fileData.Length > 200 * 1024 * 1024)
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
            Size = fileData.Length,
            SubmitTime = DateTime.Now,
            StoragePath = savePath
        };

        try
        {
            File.WriteAllBytes(savePath, fileData);
        }
        catch (Exception ex)
        {
            RespondError(response, 500, $"Failed to save: {ex.Message}");
            return;
        }

        lock (_submissionsLock)
        {
            _submissions.Add(it