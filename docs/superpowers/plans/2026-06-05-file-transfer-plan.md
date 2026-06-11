# 文件传输与作业提交功能 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 LCSync 中增加教师文件共享、学生自助下载、学生实名提交作业功能

**Architecture:** 在教师端新增 HttpListener 文件服务（端口 9457），独立于现有 WebSocket 视频流（9456）。文件控制通知通过现有 WebSocket 通道传递，实际文件数据走 HTTP。教师端和学生端 UI 改为标签页布局。

**Tech Stack:** .NET Framework 4.7.2 / WPF / WebSocketSharp / HttpListener（内置）

---

### Task 1: 新增数据模型

**Files:**
- Create: `src/LCSync.App/Models/FileItem.cs`
- Create: `src/LCSync.App/Models/SubmissionItem.cs`
- Create: `src/LCSync.App/Models/StorageConfig.cs`
- Modify: `src/LCSync.App/GlobalUsings.cs`

- [ ] **Step 1: 创建 FileItem.cs**

```csharp
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
```

- [ ] **Step 2: 创建 SubmissionItem.cs**

```csharp
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
```

- [ ] **Step 3: 创建 StorageConfig.cs**

```csharp
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
```

- [ ] **Step 4: 验证编译**

```bash
cd /sessions/adoring-eager-galileo/mnt/LCSync
dotnet build src/LCSync.App/LCSync.App.csproj 2>&1 | tail -20
```
Expected: Build succeeded (三个新模型类已编译通过)

- [ ] **Step 5: Commit**

```bash
git add src/LCSync.App/Models/FileItem.cs src/LCSync.App/Models/SubmissionItem.cs src/LCSync.App/Models/StorageConfig.cs
git commit -m "feat: add file transfer and submission data models"
```

---

### Task 2: 扩展消息协议和通知通道

**Files:**
- Modify: `src/LCSync.App/Models/Messages.cs`
- Modify: `src/LCSync.App/Services/SignalingServer.cs`
- Modify: `src/LCSync.App/Services/SignalingClient.cs`

- [ ] **Step 1: 在 Messages.cs 中新增消息类型**

在 `MessageType` 枚举末尾增加：
```csharp
    FileNotify = 0x30,      // 教师通知学生有新文件
    SubmissionNotify = 0x31, // 学生通知教师已提交
```

- [ ] **Step 2: 在 SignalingServer.cs 中新增广播方法**

在 `SignalingServer` 类末尾、`Dispose` 之前增加：

```csharp
public void BroadcastFileNotify(string fileName)
{
    if (!IsRunning)
        return;

    var payload = System.Text.Encoding.UTF8.GetBytes(fileName);
    byte[] msg = new byte[5 + payload.Length];
    msg[0] = (byte)MessageType.FileNotify;
    Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, msg, 1, 4);
    Buffer.BlockCopy(payload, 0, msg, 5, payload.Length);

    foreach (var student in _students.Values)
    {
        try
        {
            var ws = student.Context.WebSocket;
            if (ws.IsAlive)
                ws.Send(msg);
        }
        catch { }
    }
}

public void SendSubmissionNotify(string studentName)
{
    if (!IsRunning)
        return;

    var payload = System.Text.Encoding.UTF8.GetBytes(studentName);
    byte[] msg = new byte[5 + payload.Length];
    msg[0] = (byte)MessageType.SubmissionNotify;
    Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, msg, 1, 4);
    Buffer.BlockCopy(payload, 0, msg, 5, payload.Length);

    foreach (var student in _students.Values)
    {
        try
        {
            var ws = student.Context.WebSocket;
            if (ws.IsAlive)
                ws.Send(msg);
        }
        catch { }
    }
}
```

- [ ] **Step 3: 在 SignalingClient.cs 中新增事件和处理**

在 `SignalingClient` 类的事件声明区域增加：
```csharp
public event EventHandler<string>? FileNotifyReceived;
public event EventHandler<string>? SubmissionNotifyReceived;
```

在 `HandleMessage` 方法的 `switch` 中、`case VideoFrame` 之后增加：
```csharp
                case MessageType.FileNotify:
                    if (payload.Length > 0)
                    {
                        var fileName = System.Text.Encoding.UTF8.GetString(payload);
                        FileNotifyReceived?.Invoke(this, fileName);
                    }
                    break;

                case MessageType.SubmissionNotify:
                    if (payload.Length > 0)
                    {
                        var studentName = System.Text.Encoding.UTF8.GetString(payload);
                        SubmissionNotifyReceived?.Invoke(this, studentName);
                    }
                    break;
```

- [ ] **Step 4: 验证编译**

```bash
cd /sessions/adoring-eager-galileo/mnt/LCSync
dotnet build src/LCSync.App/LCSync.App.csproj 2>&1 | tail -20
```
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/LCSync.App/Models/Messages.cs src/LCSync.App/Services/SignalingServer.cs src/LCSync.App/Services/SignalingClient.cs
git commit -m "feat: add file notify and submission notify message types"
```

---

### Task 3: 实现 ConfigManager 配置管理工具

**Files:**
- Create: `src/LCSync.App/Utils/ConfigManager.cs`

- [ ] **Step 1: 编写 ConfigManager.cs**

```csharp
using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using LCSync.Models;

namespace LCSync.Utils;

public static class ConfigManager
{
    private static StorageConfig? _cached;
    private static readonly object _lock = new();

    public static StorageConfig Load()
    {
        if (_cached != null)
            return _cached;

        lock (_lock)
        {
            if (_cached != null)
                return _cached;

            var configPath = StorageConfig.GetDefaultConfigPath();
            var dir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath, Encoding.UTF8);
                    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
                    var serializer = new DataContractJsonSerializer(typeof(StorageConfig));
                    var config = (StorageConfig)serializer.ReadObject(ms)!;
                    _cached = config;
                    return config;
                }
                catch
                {
                    // fall through to default
                }
            }

            _cached = new StorageConfig
            {
                SharedDirectory = StorageConfig.GetDefaultSharedDir(),
                SubmissionDirectory = StorageConfig.GetDefaultSubmissionDir(),
                MaxBandwidthBytesPerSecond = 0
            };
            return _cached;
        }
    }

    public static void Save(StorageConfig config)
    {
        lock (_lock)
        {
            _cached = config;

            var configPath = StorageConfig.GetDefaultConfigPath();
            var dir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var ms = new MemoryStream();
            var serializer = new DataContractJsonSerializer(typeof(StorageConfig));
            serializer.WriteObject(ms, config);
            var json = Encoding.UTF8.GetString(ms.ToArray());
            File.WriteAllText(configPath, json, Encoding.UTF8);
        }
    }

    public static void EnsureDirectories(StorageConfig config)
    {
        if (!string.IsNullOrEmpty(config.SharedDirectory) && !Directory.Exists(config.SharedDirectory))
            Directory.CreateDirectory(config.SharedDirectory);

        if (!string.IsNullOrEmpty(config.SubmissionDirectory) && !Directory.Exists(config.SubmissionDirectory))
            Directory.CreateDirectory(config.SubmissionDirectory);
    }
}
```

- [ ] **Step 2: 验证编译**

```bash
cd /sessions/adoring-eager-galileo/mnt/LCSync
dotnet build src/LCSync.App/LCSync.App.csproj 2>&1 | tail -20
```
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/LCSync.App/Utils/ConfigManager.cs
git commit -m "feat: add ConfigManager for settings persistence"
```

---

### Task 4: 实现 FileServerService 核心框架

**Files:**
- Create: `src/LCSync.App/Services/FileServerService.cs`

这是最核心的新服务。Task 4 实现启动/停止、路由分发和带宽控制。

- [ ] **Step 1: 编写 FileServerService 核心框架**

```csharp
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

    // ---- 辅助方法 ----

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

    // ---- 令牌桶带宽控制 ----

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

    // ---- 元数据持久化 ----

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        SaveMetadata();
    }
}

// DataContractJsonSerializer 需要 [Serializable] 或 DataContract
[System.Runtime.Serialization.DataContract]
internal class MetadataWrapper
{
    [System.Runtime.Serialization.DataMember]
    public List<FileItem> Files { get; set; } = new();

    [System.Runtime.Serialization.DataMember]
    public List<SubmissionItem> Submissions { get; set; } = new();
}
```

- [ ] **Step 2: 验证编译**

```bash
cd /sessions/adoring-eager-galileo/mnt/LCSync
dotnet build src/LCSync.App/LCSync.App.csproj 2>&1 | tail -20
```
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/LCSync.App/Services/FileServerService.cs
git commit -m "feat: add FileServerService core with routing and throttle"
```

---

### Task 5: 实现 FileServerService API 端点

在同一个 FileServerService.cs 文件中补充各路由处理方法的实现。

**Files:**
- Modify: `src/LCSync.App/Services/FileServerService.cs`（追加到 `HandleRequest` 方法之后）

- [ ] **Step 1: 实现文件列表和下载处理**

在 `RespondError` 方法之后、`GetMimeType` 之前插入以下方法：

```csharp
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
```

- [ ] **Step 2: 实现教师上传和学生上传处理**

在 `HandleDownloadFile` 之后、`GetMimeType` 之前插入：

```csharp
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
            _submissions.Add(item);
            SaveMetadata();
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
            SaveMetadata();
        }

        var filePath = Path.Combine(_config.SharedDirectory, item.Id + "_" + item.FileName);
        try { if (File.Exists(filePath)) File.Delete(filePath); } catch { }

        RespondJson(response, 200, "{\"status\":\"deleted\"}");
    }

    private void HandleStudentInfo(HttpListenerResponse response, HttpListenerRequest request)
    {
        var remoteIp = request.RemoteEndPoint?.Address?.ToString() ?? "unknown";
        RespondJson(response, 200, $"{{\"remoteIp\":\"{remoteIp}\"}}");
    }
```

- [ ] **Step 3: 实现 multipart/form-data 解析辅助方法**

在 `HandleStudentInfo` 之后、`GetMimeType` 之前插入：

```csharp
    private string ExtractFileName(HttpListenerRequest request)
    {
        var contentType = request.ContentType ?? "";
        if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            var boundary = GetBoundary(contentType);
            if (boundary == null) return "";
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            var body = reader.ReadToEnd();
            return ParseFileNameFromMultipart(body, boundary);
        }
        return "";
    }

    private byte[]? ExtractFileData(HttpListenerRequest request)
    {
        var contentType = request.ContentType ?? "";
        if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            var boundary = GetBoundary(contentType);
            if (boundary == null) return null;

            using var ms = new MemoryStream();
            request.InputStream.CopyTo(ms);
            var bodyBytes = ms.ToArray();
            return ParseFileDataFromMultipart(bodyBytes, boundary);
        }
        return null;
    }

    private string ExtractFormField(HttpListenerRequest request, string fieldName)
    {
        var contentType = request.ContentType ?? "";
        if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            var boundary = GetBoundary(contentType);
            if (boundary == null) return "";
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            var body = reader.ReadToEnd();
            return ParseFormFieldFromMultipart(body, boundary, fieldName);
        }
        return "";
    }

    private static string? GetBoundary(string contentType)
    {
        var parts = contentType.Split(';');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
            {
                var val = trimmed.Substring("boundary=".Length).Trim('"');
                return val;
            }
        }
        return null;
    }

    private static string ParseFormFieldFromMultipart(string body, string boundary, string fieldName)
    {
        var parts = body.Split(new[] { "--" + boundary }, StringSplitOptions.None);
        foreach (var part in parts)
        {
            if (part.Contains($"name=\"{fieldName}\"", StringComparison.OrdinalIgnoreCase))
            {
                var lines = part.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]) && i + 1 < lines.Length)
                        return lines[i + 1].Trim();
                }
            }
        }
        return "";
    }

    private static string ParseFileNameFromMultipart(string body, string boundary)
    {
        var parts = body.Split(new[] { "--" + boundary }, StringSplitOptions.None);
        foreach (var part in parts)
        {
            var filenameMatch = System.Text.RegularExpressions.Regex.Match(part, 
                @"filename\s*=\s*""([^""]*)""", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (filenameMatch.Success)
                return filenameMatch.Groups[1].Value;
        }
        return "";
    }

    private static byte[]? ParseFileDataFromMultipart(byte[] body, string boundary)
    {
        var boundaryBytes = Encoding.UTF8.GetBytes("--" + boundary);
        var parts = SplitBytes(body, boundaryBytes);

        foreach (var part in parts)
        {
            var headerEnd = IndexOfBytes(part, Encoding.UTF8.GetBytes("\r\n\r\n"));
            if (headerEnd < 0)
                headerEnd = IndexOfBytes(part, Encoding.UTF8.GetBytes("\n\n"));
            if (headerEnd < 0) continue;

            var header = Encoding.UTF8.GetString(part, 0, headerEnd);
            if (header.Contains("filename", StringComparison.OrdinalIgnoreCase))
            {
                // 跳过头部空白行
                int dataStart = headerEnd;
                while (dataStart < part.Length && (part[dataStart] == '\r' || part[dataStart] == '\n'))
                    dataStart++;

                int dataEnd = part.Length;
                // 去掉尾部的 \r\n--boundary--
                if (dataEnd > 2 && part[dataEnd - 1] == '\n')
                    dataEnd--;
                if (dataEnd > 1 && part[dataEnd - 1] == '\r')
                    dataEnd--;

                var data = new byte[dataEnd - dataStart];
                Buffer.BlockCopy(part, dataStart, data, 0, data.Length);
                return data;
            }
        }
        return null;
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
```

- [ ] **Step 4: 验证编译**

```bash
cd /sessions/adoring-eager-galileo/mnt/LCSync
dotnet build src/LCSync.App/LCSync.App.csproj 2>&1 | tail -20
```
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/LCSync.App/Services/FileServerService.cs
git commit -m "feat: implement FileServerService API endpoints"
```

---

### Task 6: 改造 TeacherViewModel（增加文件/作业/设置功能）

**Files:**
- Modify: `src/LCSync.App/ViewModels/TeacherViewModel.cs`

- [ ] **Step 1: 在 TeacherViewModel 类中新增字段和属性**

在现有的字段声明区域后增加：

```csharp
    // ---- 文件共享相关 ----
    private FileServerService? _fileServer;
    private System.Collections.ObjectModel.ObservableCollection<FileItem> _sharedFiles = new();
    public System.Collections.ObjectModel.ObservableCollection<FileItem> SharedFiles
    {
        get => _sharedFiles;
        set => SetProperty(ref _sharedFiles, value);
    }

    private System.Collections.ObjectModel.ObservableCollection<SubmissionItem> _submissions = new();
    public System.Collections.ObjectModel.ObservableCollection<SubmissionItem> Submissions
    {
        get => _submissions;
        set => SetProperty(ref _submissions, value);
    }

    // ---- 设置相关 ----
    private string _sharedDir = "";
    public string SharedDir
    {
        get => _sharedDir;
        set => SetProperty(ref _sharedDir, value);
    }

    private string _submissionDir = "";
    public string SubmissionDir
    {
        get => _submissionDir;
        set => SetProperty(ref _submissionDir, value);
    }

    private string _maxBandwidthText = "0";
    public string MaxBandwidthText
    {
        get => _maxBandwidthText;
        set => SetProperty(ref _maxBandwidthText, value);
    }

    private string _notificationText = "";
    public string NotificationText
    {
        get => _notificationText;
        set => SetProperty(ref _notificationText, value);
    }

    private int _selectedTabIndex = 0;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    // ---- 文件共享统计 ----
    public string SharedFilesSummary
    {
        get
        {
            long total = 0;
            foreach (var f in _sharedFiles) total += f.Size;
            return $"共 {_sharedFiles.Count} 个文件 · {FormatSize(total)}";
        }
    }

    public string SubmissionsSummary
    {
        get => $"已提交: {_submissions.Count} 人";
    }
```

- [ ] **Step 2: 新增命令声明**

在 `StopBroadcastCommand` 声明之后增加：

```csharp
    public ICommand UploadSharedFileCommand { get; }
    public ICommand DeleteSharedFileCommand { get; }
    public ICommand DownloadSubmissionCommand { get; }
    public ICommand OpenSharedDirCommand { get; }
    public ICommand OpenSubmissionDirCommand { get; }
    public ICommand SaveSettingsCommand { get; }
```

- [ ] **Step 3: 在构造函数中初始化文件服务和命令**

在构造函数末尾、`DiagnosticInfo = "准备就绪，请设置画质后开始广播";` 之前增加：

```csharp
            // 初始化文件服务
            _fileServer = new FileServerService(NetworkConfig.FilePort);
            _fileServer.FileShared += OnFileShared;
            _fileServer.SubmissionReceived += OnSubmissionReceived;
            _fileServer.ErrorOccurred += (s, msg) =>
                _window.Dispatcher.Invoke(() => NotificationText = msg);
            _fileServer.Start();

            // 加载配置
            var config = Utils.ConfigManager.Load();
            SharedDir = config.SharedDirectory;
            SubmissionDir = config.SubmissionDirectory;
            MaxBandwidthText = config.MaxBandwidthBytesPerSecond > 0
                ? (config.MaxBandwidthBytesPerSecond / 1024 / 1024).ToString()
                : "0";

            // 刷新文件列表
            RefreshFileList();
            RefreshSubmissionList();

            // 初始化命令
            UploadSharedFileCommand = new AsyncRelayCommand(UploadSharedFile);
            DeleteSharedFileCommand = new RelayCommand<string>(DeleteSharedFile);
            DownloadSubmissionCommand = new RelayCommand<string>(DownloadSubmission);
            OpenSharedDirCommand = new RelayCommand(OpenSharedDir);
            OpenSubmissionDirCommand = new RelayCommand(OpenSubmissionDir);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
```

同时将 `NetworkConfig` 中增加 `FilePort` 常量，在 Step 7 处理。

- [ ] **Step 4: 新增文件/作业处理方法**

在 `Dispose` 方法之前增加：

```csharp
    private void OnFileShared(object? sender, FileItem item)
    {
        _window.Dispatcher.Invoke(() =>
        {
            RefreshFileList();
            NotificationText = $"已共享文件: {item.FileName}";
            // 通过 WebSocket 广播通知所有学生
            if (_signalingServer.IsRunning)
                _signalingServer.BroadcastFileNotify(item.FileName);
        });
    }

    private void OnSubmissionReceived(object? sender, SubmissionItem item)
    {
        _window.Dispatcher.Invoke(() =>
        {
            RefreshSubmissionList();
            NotificationText = $"收到 {item.StudentName} 的作业: {item.FileName}";
            // 切换到提交箱标签页提示教师
        });
    }

    private void RefreshFileList()
    {
        if (_fileServer == null) return;
        SharedFiles.Clear();
        foreach (var f in _fileServer.SharedFiles)
            SharedFiles.Add(f);
        OnPropertyChanged(nameof(SharedFilesSummary));
    }

    private void RefreshSubmissionList()
    {
        if (_fileServer == null) return;
        Submissions.Clear();
        foreach (var s in _fileServer.Submissions)
            Submissions.Add(s);
        OnPropertyChanged(nameof(SubmissionsSummary));
    }

    private async System.Threading.Tasks.Task UploadSharedFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要共享的文件",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var fileBytes = System.IO.File.ReadAllBytes(dialog.FileName);
                var fileName = System.IO.Path.GetFileName(dialog.FileName);

                // 通过 HTTP 上传到本机的文件服务
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                using var content = new System.Net.Http.MultipartFormDataContent();
                content.Add(new System.Net.Http.ByteArrayContent(fileBytes), "file", fileName);
                var response = await client.PostAsync(
                    $"http://localhost:{NetworkConfig.FilePort}/api/teacher/upload", content);

                if (response.IsSuccessStatusCode)
                {
                    NotificationText = $"已上传: {fileName}";
                    RefreshFileList();
                }
                else
                {
                    var err = await response.Content.ReadAsStringAsync();
                    NotificationText = $"上传失败: {err}";
                }
            }
            catch (Exception ex)
            {
                NotificationText = $"上传失败: {ex.Message}";
            }
        }
    }

    private void DeleteSharedFile(string fileId)
    {
        if (_fileServer == null || string.IsNullOrEmpty(fileId)) return;

        using var client = new System.Net.Http.HttpClient();
        try
        {
            var response = client.DeleteAsync(
                $"http://localhost:{NetworkConfig.FilePort}/api/files/{fileId}").Result;
            if (response.IsSuccessStatusCode)
            {
                RefreshFileList();
                NotificationText = "文件已删除";
            }
        }
        catch (Exception ex)
        {
            NotificationText = $"删除失败: {ex.Message}";
        }
    }

    private void DownloadSubmission(string submissionId)
    {
        if (_fileServer == null || string.IsNullOrEmpty(submissionId)) return;

        SubmissionItem? item = null;
        foreach (var s in _submissions)
        {
            if (s.Id == submissionId) { item = s; break; }
        }
        if (item == null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = item.FileName,
            Title = "保存作业"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                var data = client.GetByteArrayAsync(
                    $"http://localhost:{NetworkConfig.FilePort}/api/submissions/{submissionId}").Result;
                System.IO.File.WriteAllBytes(dialog.FileName, data);
                NotificationText = $"已保存: {dialog.FileName}";
            }
            catch (Exception ex)
            {
                NotificationText = $"下载失败: {ex.Message}";
            }
        }
    }

    private void OpenSharedDir()
    {
        if (!string.IsNullOrEmpty(SharedDir) && System.IO.Directory.Exists(SharedDir))
            System.Diagnostics.Process.Start("explorer.exe", SharedDir);
    }

    private void OpenSubmissionDir()
    {
        if (!string.IsNullOrEmpty(SubmissionDir) && System.IO.Directory.Exists(SubmissionDir))
            System.Diagnostics.Process.Start("explorer.exe", SubmissionDir);
    }

    private void SaveSettings()
    {
        var config = new StorageConfig
        {
            SharedDirectory = SharedDir,
            SubmissionDirectory = SubmissionDir,
            MaxBandwidthBytesPerSecond = long.TryParse(MaxBandwidthText, out var mb) ? mb * 1024 * 1024 : 0
        };
        _fileServer?.UpdateConfig(config);
        NotificationText = "设置已保存";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
```

- [ ] **Step 5: 修改 Dispose 方法释放文件服务**

在 `Dispose` 方法末尾、`_broadcastCts?.Dispose();` 之后增加：
```csharp
            _fileServer?.Dispose();
```

- [ ] **Step 6: 在 NetworkConfig.cs 中增加 FilePort 常量**

```csharp
    public const int FilePort = 9457;
```

- [ ] **Step 7: 在 TeacherViewModel 中增加 StudentSummary 属性方法**

在 `FormatSize` 方法之后增加 Notify 相关属性和命令更新方法：
```csharp
    // 手动触发属性通知（不修改文件列表场景）
    public void NotifyFileListChanged() => OnPropertyChanged(nameof(SharedFilesSummary));
```

- [ ] **Step 8: 验证编译**

```bash
cd /sessions/adoring-eager-galileo/mnt/LCSync
dotnet build src/LCSync.App/LCSync.App.csproj 2>&1 | tail -20
```
Expected: Build succeeded

- [ ] **Step 9: Commit**

```bash
git add src/LCSync.App/ViewModels/TeacherViewModel.cs src/LCSync.App/Models/NetworkConfig.cs
git commit -m "feat: extend TeacherViewModel with file management and settings"
```

---

### Task 7: 改造 TeacherWindow XAML（标签页布局）

**Files:**
- Modify: `src/LCSync.App/Views/TeacherWindow.xaml`

这是最复杂的 XAML 改动。现有界面整体替换为 TabControl 布局，原有内容成为"屏幕广播"标签页。

- [ ] **Step 1: 重写 TeacherWindow.xaml**

整体替换为：

```xml
<Window x:Class="LCSync.Views.TeacherWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="LCSync - 教师模式"
        Width="1000"
        Height="700"
        WindowStartupLocation="CenterScreen"
        Background="#FFFFFF">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- 顶部导航标签 -->
        <Border Grid.Row="0" Background="#F8FAFC" BorderBrush="#E2E8F0" BorderThickness="0,0,0,1">
            <StackPanel Orientation="Horizontal" Margin="20,0">
                <RadioButton Content="🖥 屏幕广播"
                             IsChecked="{Binding SelectedTabIndex, Converter={StaticResource TabIndexConverter}, ConverterParameter=0}"
                             Style="{StaticResource TabRadioStyle}"/>
                <RadioButton Content="📁 文件共享"
                             IsChecked="{Binding SelectedTabIndex, Converter={StaticResource TabIndexConverter}, ConverterParameter=1}"
                             Style="{StaticResource TabRadioStyle}"/>
                <RadioButton Content="📝 作业提交箱"
                             IsChecked="{Binding SelectedTabIndex, Converter={StaticResource TabIndexConverter}, ConverterParameter=2}"
                             Style="{StaticResource TabRadioStyle}"/>
                <RadioButton Content="⚙ 设置"
                             IsChecked="{Binding SelectedTabIndex, Converter={StaticResource TabIndexConverter}, ConverterParameter=3}"
                             Style="{StaticResource TabRadioStyle}"/>
            </StackPanel>
        </Border>

        <!-- 内容区域 -->
        <Grid Grid.Row="1">
            <!-- Tab 0: 屏幕广播（原有内容） -->
            <Grid Visibility="{Binding SelectedTabIndex, Converter={StaticResource TabVisibilityConverter}, ConverterParameter=0}" Margin="40">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <StackPanel Grid.Row="0" HorizontalAlignment="Left">
                    <TextBlock Text="LCSync 教师端"
                               FontSize="28" FontWeight="700" Foreground="#1E293B"/>
                    <TextBlock Text="共享您的屏幕给所有学生"
                               FontSize="14" Foreground="#64748B" Margin="0,6,0,0"/>
                </StackPanel>

                <Grid Grid.Row="1" Margin="0,30,0,0">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>

                    <StackPanel Grid.Column="0" Orientation="Horizontal">
                        <TextBlock Text="画质：" FontSize="14" Foreground="#64748B" VerticalAlignment="Center"/>
                        <ComboBox ItemsSource="{Binding Presets}"
                                  SelectedItem="{Binding SelectedConfig}"
                                  Background="#F8FAFC" BorderBrush="#E2E8F0" BorderThickness="1"
                                  Padding="12,8" FontSize="14" MinWidth="220" Margin="10,0,0,0">
                            <ComboBox.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock>
                                        <Run Text="{Binding Name, Mode=OneWay}"/>
                                        <Run Text=" · "/>
                                        <Run Text="{Binding Width, Mode=OneWay}"/>
                                        <Run Text="x"/>
                                        <Run Text="{Binding Height, Mode=OneWay}"/>
                                        <Run Text=" · "/>
                                        <Run Text="{Binding Framerate, Mode=OneWay}"/>
                                        <Run Text="fps"/>
                                    </TextBlock>
                                </DataTemplate>
                            </ComboBox.ItemTemplate>
                        </ComboBox>
                    </StackPanel>

                    <StackPanel Grid.Column="2" Orientation="Horizontal">
                        <Button Content="开始广播"
                                Command="{Binding StartBroadcastCommand}"
                                IsEnabled="{Binding IsBroadcasting, Converter={StaticResource NotConverter}}"
                                Background="#3B82F6" Foreground="White"
                                Padding="24,12" FontSize="14" FontWeight="600" BorderThickness="0" Cursor="Hand">
                            <Button.Template>
                                <ControlTemplate TargetType="Button">
                                    <Border Background="{TemplateBinding Background}" CornerRadius="8" Padding="{TemplateBinding Padding}">
                                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                    </Border>
                                </ControlTemplate>
                            </Button.Template>
                        </Button>

                        <Button Content="停止广播"
                                Command="{Binding StopBroadcastCommand}"
                                IsEnabled="{Binding IsBroadcasting}"
                                Background="#EF4444" Foreground="White"
                                Padding="24,12" FontSize="14" FontWeight="600"
                                Margin="12,0,0,0" BorderThickness="0" Cursor="Hand">
                            <Button.Template>
                                <ControlTemplate TargetType="Button">
                                    <Border Background="{TemplateBinding Background}" CornerRadius="8" Padding="{TemplateBinding Padding}">
                                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                    </Border>
                                </ControlTemplate>
                            </Button.Template>
                        </Button>
                    </StackPanel>
                </Grid>

                <Grid Grid.Row="2" Margin="0,20,0,0">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="20"/>
                        <ColumnDefinition Width="280"/>
                    </Grid.ColumnDefinitions>

                    <Border Grid.Column="0" Background="#F8FAFC" BorderBrush="#E2E8F0" BorderThickness="1" CornerRadius="12">
                        <Grid>
                            <Image Source="{Binding PreviewImage}" Stretch="Uniform"
                                   HorizontalAlignment="Center" VerticalAlignment="Center" Margin="20"/>
                            <TextBlock Text="点击「开始广播」开始共享屏幕"
                                       Foreground="#94A3B8" FontSize="16"
                                       HorizontalAlignment="Center" VerticalAlignment="Center">
                                <TextBlock.Style>
                                    <Style TargetType="TextBlock">
                                        <Setter Property="Visibility" Value="Collapsed"/>
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding IsBroadcasting}" Value="False">
                                                <Setter Property="Visibility" Value="Visible"/>
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </TextBlock.Style>
                            </TextBlock>
                        </Grid>
                    </Border>

                    <Border Grid.Column="2" Background="#F8FAFC" BorderBrush="#E2E8F0" BorderThickness="1" CornerRadius="12" Padding="24">
                        <StackPanel>
                            <TextBlock Text="连接信息" FontSize="16" FontWeight="600" Foreground="#1E293B" Margin="0,0,0,20"/>
                            <Grid Margin="0,0,0,16">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <TextBlock Text="本机IP" Foreground="#64748B" FontSize="13"/>
                                <TextBlock Grid.Column="1" Text="{Binding IpAddress}" Foreground="#3B82F6" FontWeight="600" FontSize="13"/>
                            </Grid>
                            <Grid Margin="0,0,0,16">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <TextBlock Text="连接人数" Foreground="#64748B" FontSize="13"/>
                                <TextBlock Grid.Column="1" Foreground="#1E293B" FontWeight="600" FontSize="13">
                                    <Run Text="{Binding PeerCount}"/><Run Text=" 人"/>
                                </TextBlock>
                            </Grid>
                            <Grid Margin="0,0,0,16">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <TextBlock Text="状态" Foreground="#64748B" FontSize="13"/>
                                <TextBlock Grid.Column="1" Text="{Binding StatusText}" Foreground="#1E293B" FontSize="13"/>
                            </Grid>
                            <Separator Background="#E2E8F0" Margin="0,10,0,20"/>
                            <TextBlock Text="{Binding DiagnosticInfo}" FontSize="11" Foreground="#94A3B8" TextWrapping="Wrap" FontFamily="Consolas"/>
                        </StackPanel>
                    </Border>
                </Grid>
            </Grid>

            <!-- Tab 1: 文件共享 -->
            <Grid Visibility="{Binding SelectedTabIndex, Converter={StaticResource TabVisibilityConverter}, ConverterParameter=1}" Margin="40">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <StackPanel Grid.Row="0" Orientation="Horizontal" HorizontalAlignment="Left">
                    <TextBlock Text="📁 文件共享" FontSize="28" FontWeight="700" Foreground="#1E293B"/>
                    <Button Content="+ 上传共享文件"
                            Command="{Binding UploadSharedFileCommand}"
                            Background="#3B82F6" Foreground="White"
                            Padding="20,10" FontSize="14" FontWeight="600"
                            Margin="20,5,0,0" BorderThickness="0" Cursor="Hand"
                            VerticalAlignment="Center">
                        <Button.Template>
                            <ControlTemplate TargetType="Button">
                                <Border Background="{TemplateBinding Background}" CornerRadius="8" Padding="{TemplateBinding Padding}">
                                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                </Border>
                            </ControlTemplate>
                        </Button.Template>
                    </Button>
                </StackPanel>

                <TextBlock Grid.Row="1" Text="{Binding NotificationText}"
                           Foreground="#10B981" FontSize="13" Margin="0,8,0,0"/>

                <Border Grid.Row="2" Background="#F8FAFC" BorderBrush="#E2E8F0" BorderThickness="1" CornerRadius="12" Margin="0,12,0,0">
                    <ScrollViewer>
                        <ItemsControl ItemsSource="{Binding SharedFiles}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Border BorderBrush="#E2E8F0" BorderThickness="0,0,0,1" Padding="16,12">
                                        <Grid>
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="*"/>
                                                <ColumnDefinition Width="100"/>
                                                <ColumnDefinition Width="140"/>
                                                <ColumnDefinition Width="80"/>
                                                <ColumnDefinition Width="100"/>
                                            </Grid.ColumnDefinitions>
                                            <TextBlock Grid.Column="0" Text="{Binding FileName}" FontWeight="600" Foreground="#1E293B" VerticalAlignment="Center"/>
                                            <TextBlock Grid.Column="1" Foreground="#64748B" VerticalAlignment="Center">
                                                <Run Text="{Binding Size, Converter={StaticResource FileSizeConverter}}"/>
                                            </TextBlock>
                                            <TextBlock Grid.Column="2" Foreground="#64748B" FontSize="12" VerticalAlignment="Center">
                                                <Run Text="{Binding UploadTime, StringFormat={}{0:MM-dd HH:mm}}"/>
                                            </TextBlock>
                                            <TextBlock Grid.Column="3" Foreground="#64748B" VerticalAlignment="Center">
                                                <Run Text="{Binding DownloadCount}"/><Run Text=" 次"/>
                                            </TextBlock>
                                            <Button Grid.Column="4" Content="删除"
                                                    Command="{Binding Source={RelativeSource FindAncestor, AncestorType={x:Type Window}}, Path=DataContext.DeleteSharedFileCommand}"
                                                    CommandParameter="{Binding Id}"
                                                    Background="#FEE2E2" Foreground="#EF4444"
                                                    Padding="10,6" FontSize="12" BorderThickness="0" Cursor="Hand"
                                                    HorizontalAlignment="Right">
                                                <Button.Template>
                                                    <ControlTemplate TargetType="Button">
                                                        <Border Background="{TemplateBinding Background}" CornerRadius="6" Padding="{TemplateBinding Padding}">
                                                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                                        </Border>
                                                    </ControlTemplate>
                                                </Button.Template>
                                            </Button>
                                        </Grid>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ScrollViewer>
                </Border>

                <TextBlock Grid.Row="3" Text="{Binding SharedFilesSummary}" Foreground="#94A3B8" FontSize="13" Margin="0,8,0,0"/>
            </Grid>

            <!-- Tab 2: 作业提交箱 -->
            <Grid Visibility="{Binding SelectedTabIndex, Converter={StaticResource TabVisibilityConverter}, ConverterParameter=2}" Margin="40">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <TextBlock Grid.Row="0" Text="📝 作业提交箱" FontSize="28" FontWeight="700" Foreground="#1E293B"/>

                <Border Grid.Row="1" Background="#F8FAFC" BorderBrush="#E2E8F0" BorderThickness="1" CornerRadius="12" Margin="0,12,0,0">
                    <ScrollViewer>
                        <ItemsControl ItemsSource="{Binding Submissions}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Border BorderBrush="#E2E8F0" BorderThickness="0,0,0,1" Padding="16,12">
                                        <Grid>
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="120"/>
                                                <ColumnDefinition Width="100"/>
                                                <ColumnDefinition Width="*"/>
                                                <ColumnDefinition Width="80"/>
                                                <ColumnDefinition Width="140"/>
                                                <ColumnDefinition Width="80"/>
                                            </Grid.ColumnDefinitions>
                                            <TextBlock Grid.Column="0" Text="{Binding StudentName}" FontWeight="600" Foreground="#1E293B" VerticalAlignment="Center"/>
                                            <TextBlock Grid.Column="1" Text="{Binding StudentId}" Foreground="#64748B" FontSize="12" VerticalAlignment="Center"/>
                                            <TextBlock Grid.Column="2" Text="{Binding FileName}" Foreground="#334155" VerticalAlignment="Center"/>
                                            <TextBlock Grid.Column="3" Foreground="#64748B" VerticalAlignment="Center">
                                                <Run Text="{Binding Size, Converter={StaticResource FileSizeConverter}}"/>
                                            </TextBlock>
                                            <TextBlock Grid.Column="4" Foreground="#64748B" FontSize="12" VerticalAlignment="Center">
                                                <Run Text="{Binding SubmitTime, StringFormat={}{0:MM-dd HH:mm}}"/>
                                            </TextBlock>
                                            <Button Grid.Column="5" Content="下载"
                                                    Command="{Binding Source={RelativeSource FindAncestor, AncestorType={x:Type Window}}, Path=DataContext.DownloadSubmissionCommand}"
                                                    CommandParameter="{Binding Id}"
                                                    Background="#DBEAFE" Foreground="#2563EB"
                                                    Padding="10,6" FontSize="12" BorderThickness="0" Cursor="Hand"
                                                    HorizontalAlignment="Right">
                                                <Button.Template>
                                                    <ControlTemplate TargetType="Button">
                                                        <Border Background="{TemplateBinding Background}" CornerRadius="6" Padding="{TemplateBinding Padding}">
                                                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                                        </Border>
                                                    </ControlTemplate>
                                                </Button.Template>
                                            </Button>
                                        </Grid>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ScrollViewer>
                </Border>

                <TextBlock Grid.Row="2" Text="{Binding SubmissionsSummary}" Foreground="#94A3B8" FontSize="13" Margin="0,8,0,0"/>
            </Grid>

            <!-- Tab 3: 设置 -->
            <Grid Visibility="{Binding SelectedTabIndex, Converter={StaticResource TabVisibilityConverter}, ConverterParameter=3}" Margin="40">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>

                <TextBlock Grid.Row="0" Text="⚙ 设置" FontSize="28" FontWeight="700" Foreground="#1E293B"/>

                <Border Grid.Row="1" Background="#F8FAFC" BorderBrush="#E2E8F0" BorderThickness="1" CornerRadius="12" Padding="32" Margin="0,20,0,0" MaxWidth="600" HorizontalAlignment="Left">
                    <StackPanel>
                        <TextBlock Text="存储位置" FontSize="16" FontWeight="600" Foreground="#1E293B" Margin="0,0,0,20"/>

                        <Grid Margin="0,0,0,12">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="100"/>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column="0" Text="共享文件目录" Foreground="#475569" FontSize="13" VerticalAlignment="Center"/>
                            <TextBox Grid.Column="1" Text="{Binding SharedDir}" Background="White" BorderBrush="#E2E8F0" Padding="10,8" FontSize="13"/>
                            <Button Grid.Column="2" Content="浏览..." Command="{Binding OpenSharedDirCommand}"
                                    Background="#E2E8F0" Foreground="#475569" Padding="12,8" FontSize="12" Margin="8,0,0,0" BorderThickness="0" Cursor="Hand"/>
                        </Grid>

                        <Grid Margin="0,0,0,24">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="100"/>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column="0" Text="作业保存目录" Foreground="#475569" FontSize="13" VerticalAlignment="Center"/>
                            <TextBox Grid.Column="1" Text="{Binding SubmissionDir}" Background="White" BorderBrush="#E2E8F0" Padding="10,8" FontSize="13"/>
                            <Button Grid.Column="2" Content="浏览..." Command="{Binding OpenSubmissionDirCommand}"
                                    Background="#E2E8F0" Foreground="#475569" Padding="12,8" FontSize="12" Margin="8,0,0,0" BorderThickness="0" Cursor="Hand"/>
                        </Grid>

                        <Separator Background="#E2E8F0" Margin="0,0,0,20"/>

                        <TextBlock Text="带宽控制" FontSize="16" FontWeight="600" Foreground="#1E293B" Margin="0,0,0,20"/>

                        <Grid Margin="0,0,0,24">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="100"/>
                                <ColumnDefinition Width="80"/>
                                <ColumnDefinition Width="*"/>
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column="0" Text="下载限速" Foreground="#475569" FontSize="13" VerticalAlignment="Center"/>
                            <TextBox Grid.Column="1" Text="{Binding MaxBandwidthText}" Background="White" BorderBrush="#E2E8F0" Padding="10,8" FontSize="13" HorizontalAlignment="Left" Width="70"/>
                            <TextBlock Grid.Column="2" Text="MB/s（0 = 不限速）" Foreground="#94A3B8" FontSize="12" VerticalAlignment="Center" Margin="8,0,0,0"/>
                        </Grid>

                        <Button Content="保存设置"
                                Command="{Binding SaveSettingsCommand}"
                                Background="#3B82F6" Foreground="White"
                                Padding="24,12" FontSize="14" FontWeight="600" BorderThickness="0" Cursor="Hand"
                                HorizontalAlignment="Left">
                            <Button.Template>
                                <ControlTemplate TargetType="Button">
                                    <Border Background="{TemplateBinding Background}" CornerRadius="8" Padding="{TemplateBinding Padding}">
                                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                    </Border>
                                </ControlTemplate>
                            </Button.Template>
                        </Button>

                        <TextBlock Text="{Binding NotificationText}" Foreground="#10B981" FontSize="13" Margin="0,12,0,0"/>
                    </StackPanel>
                </Border>
            </Grid>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: 在 App.xaml 中注册新转换器和样式**

在 `App.xaml` 的 `<Application.Resources>` 中新增：

```xml
                    <lcsync:TabIndexConverter x:Key="TabIndexConverter"/>
                    <lcsync:TabVisibilityConverter x:Key="TabVisibilityConverter"/>
                    <lcsync:FileSizeConverter x:Key="FileSizeConverter"/>
```

并在现有 Button/TextBlock/TextBox 样式之后增加 TabRadioButton 样式：

```xml
            <Style x:Key="TabRadioStyle" TargetType="RadioButton">
                <Setter Property="Padding" Value="20,14"/>
                <Setter Property="FontSize" Value="14"/>
                <Setter Property="Foreground" Value="#64748B"/>
                <Setter Property="Background" Value="Transparent"/>
                <Setter Property="BorderThickness" Value="0"/>
                <Setter Property="Cursor" Value="Hand"/>
                <Style.Triggers>
                    <Trigger Property="IsChecked" Value="True">
                        <Setter Property="Foreground" Value="#3B82F6"/>
                        <Setter Property="FontWeight" Value="600"/>
                    </Trigger>
                </Style.Triggers>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="RadioButton">
                            <Border Background="{TemplateBinding Background}" BorderThickness="0,0,0,2" BorderBrush="{TemplateBinding Foreground}" Padding="{TemplateBinding Padding}">
                                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                            </Border>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
```

- [ ] **Step 3: 在 App.xaml.cs 中新增三个转换器**

在 `ReverseBoolToVisibilityConverter` 类之后、文件末尾增加：

```csharp
public class TabIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int tabIndex && parameter is string paramStr && int.TryParse(paramStr, out int target))
            return tabIndex == target;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is string paramStr && int.TryParse(paramStr, out int target))
            return target;
        return Binding.DoNothing;
    }
}

public class TabVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int tabIndex && parameter is string paramStr && int.TryParse(paramStr, out int target))
            return tabIndex == target ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            if (bytes >= 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F1} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} B";
        }
        return "0 B";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

- [ ] **Step 4: 验证编译**

```bash
cd /sessions/adoring-eager-galileo/mnt/LCSync
dotnet build src/LCSync.App/LCSync.App.csproj 2>&1 | tail -20
```
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/LCSync.App/Views/TeacherWindow.xaml src/LCSync.App/App.xaml src/LCSync.App/App.xaml.cs
git commit -m "feat: redesign TeacherWindow with tab layout and add converters"
```

---

### Task 8: 改造 StudentViewModel（增加文件浏览/下载/提交功能）

**Files:**
- Modify: `src/LCSync.App/ViewModels/StudentViewModel.cs`

- [ ] **Step 1: 新增字段和属性**

在现有字段声明区域后增加：

```csharp
    // ---- 文件共享相关 ----
    private System.Collections.ObjectModel.ObservableCollection<FileItem> _sharedFiles = new();
    public System.Collections.ObjectModel.ObservableCollection<FileItem> SharedFiles
    {
        get => _sharedFiles;
        set => SetProperty(ref _sharedFiles, value);
    }

    // ---- 学生信息 ----
    private string _studentName = "";
    public string StudentName
    {
        get => _studentName;
        set => SetProperty(ref _studentName, value);
    }

    private string _studentIdCard = "";
    public string StudentIdCard
    {
        get => _studentIdCard;
        set => SetProperty(ref _studentIdCard, value);
    }

    // ---- 文件上传 ----
    private string _selectedFilePath = "";
    public string SelectedFilePath
    {
        get => _selectedFilePath;
        set => SetProperty(ref _selectedFilePath, value);
    }

    private string _selectedFileName = "";
    public string SelectedFileName
    {
        get => _selectedFileName;
        set => SetProperty(ref _selectedFileName, value);
    }

    // ---- 下载状态 ----
    private double _downloadProgress;
    public double DownloadProgress
    {
        get => _downloadProgress;
        set => SetProperty(ref _downloadProgress, value);
    }

    private string _downloadStatus = "";
    public string DownloadStatus
    {
        get => _downloadStatus;
        set => SetProperty(ref _downloadStatus, value);
    }

    private string _fileNotificationText = "";
    public string FileNotificationText
    {
        get => _fileNotificationText;
        set => SetProperty(ref _fileNotificationText, value);
    }

    // ---- 已提交记录 ----
    private System.Collections.ObjectModel.ObservableCollection<SubmissionItem> _mySubmissions = new();
    public System.Collections.ObjectModel.ObservableCollection<SubmissionItem> MySubmissions
    {
        get => _mySubmissions;
        set => SetProperty(ref _mySubmissions, value);
    }

    private int _studentTabIndex = 0;
    public int StudentTabIndex
    {
        get => _studentTabIndex;
        set => SetProperty(ref _studentTabIndex, value);
    }

    private string _serverFileUrl = "";
    public string ServerFileUrl
    {
        get => _serverFileUrl;
        set => SetProperty(ref _serverFileUrl, value);
    }
```

- [ ] **Step 2: 新增命令声明**

在 `DisconnectCommand` 之后增加：

```csharp
    public ICommand RefreshFileListCommand { get; }
    public ICommand DownloadFileCommand { get; }
    public ICommand SelectFileCommand { get; }
    public ICommand SubmitFileCommand { get; }
    public ICommand SaveStudentInfoCommand { get; }
```

- [ ] **Step 3: 在构造函数中初始化**

在构造函数末尾、`DiagnosticInfo = "等待连接...";` 之前增加：

```csharp
            // 初始化命令
            RefreshFileListCommand = new AsyncRelayCommand(RefreshFileList);
            DownloadFileCommand = new RelayCommand<string>(DownloadFile);
            SelectFileCommand = new RelayCommand(SelectFile);
            SubmitFileCommand = new AsyncRelayCommand(SubmitFile);
            SaveStudentInfoCommand = new RelayCommand(SaveStudentInfo);

            // 加载本地缓存的学生信息
            LoadStudentInfo();
```

并在构造函数中增加对学生端通知事件的处理：

```csharp
            _client.FileNotifyReceived += (s, fileName) =>
            {
                _window.Dispatcher.Invoke(async () =>
                {
                    FileNotificationText = $"教师更新了文件: {fileName}";
                    await RefreshFileList();
                });
            };
```

在 `_client.Disconnected += OnDisconnected;` 之后增加。

- [ ] **Step 4: 新增文件操作方法**

在 `Dispose` 方法之前增加：

```csharp
    private async System.Threading.Tasks.Task RefreshFileList()
    {
        if (string.IsNullOrWhiteSpace(ServerFileUrl)) return;

        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetStringAsync($"{ServerFileUrl}/api/files");
            if (!string.IsNullOrEmpty(response))
            {
                using var ms = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(response));
                var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(List<FileItem>));
                var files = (List<FileItem>?)serializer.ReadObject(ms);
                _window.Dispatcher.Invoke(() =>
                {
                    SharedFiles.Clear();
                    if (files != null)
                    {
                        foreach (var f in files)
                            SharedFiles.Add(f);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"刷新文件列表失败: {ex.Message}");
        }
    }

    private void DownloadFile(string fileId)
    {
        if (string.IsNullOrWhiteSpace(ServerFileUrl) || string.IsNullOrEmpty(fileId)) return;

        // 查找文件信息
        FileItem? item = null;
        foreach (var f in _sharedFiles)
        {
            if (f.Id == fileId) { item = f; break; }
        }
        if (item == null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = item.FileName,
            Title = "保存文件"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                DownloadStatus = "正在下载...";
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                var data = client.GetByteArrayAsync($"{ServerFileUrl}/api/files/{fileId}").Result;
                System.IO.File.WriteAllBytes(dialog.FileName, data);
                DownloadStatus = $"✅ 下载完成: {item.FileName}";
                FileNotificationText = $"下载完成: {item.FileName}";
            }
            catch (Exception ex)
            {
                DownloadStatus = $"❌ 下载失败: {ex.Message}";
                FileNotificationText = $"下载失败: {ex.Message}";
            }
        }
    }

    private void SelectFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要提交的作业文件",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedFilePath = dialog.FileName;
            SelectedFileName = System.IO.Path.GetFileName(dialog.FileName);
        }
    }

    private async System.Threading.Tasks.Task SubmitFile()
    {
        if (string.IsNullOrWhiteSpace(ServerFileUrl))
        {
            FileNotificationText = "请先连接到教师端";
            return;
        }

        if (string.IsNullOrWhiteSpace(StudentName) || string.IsNullOrWhiteSpace(StudentIdCard))
        {
            FileNotificationText = "请先填写姓名和学号";
            return;
        }

        if (string.IsNullOrEmpty(SelectedFilePath) || !System.IO.File.Exists(SelectedFilePath))
        {
            FileNotificationText = "请选择要提交的文件";
            return;
        }

        try
        {
            FileNotificationText = "正在提交...";
            var fileBytes = System.IO.File.ReadAllBytes(SelectedFilePath);

            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using var content = new System.Net.Http.MultipartFormDataContent();
            content.Add(new System.Net.Http.StringContent(StudentName), "studentName");
            content.Add(new System.Net.Http.StringContent(StudentIdCard), "studentId");
            content.Add(new System.Net.Http.ByteArrayContent(fileBytes), "file", SelectedFileName);

            var response = await client.PostAsync($"{ServerFileUrl}/api/upload", content);
            var result = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                FileNotificationText = $"✅ 提交成功: {SelectedFileName}";
                SelectedFilePath = "";
                SelectedFileName = "";
                LoadMySubmissions();
            }
            else
            {
                FileNotificationText = $"❌ 提交失败: {result}";
            }
        }
        catch (Exception ex)
        {
            FileNotificationText = $"❌ 提交失败: {ex.Message}";
        }
    }

    private void SaveStudentInfo()
    {
        SaveStudentInfoToLocal();
        FileNotificationText = "信息已保存";
    }

    private void SaveStudentInfoToLocal()
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LCSync");
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            var json = $"{{\"name\":\"{EscapeJson(StudentName)}\",\"id\":\"{EscapeJson(StudentIdCard)}\"}}";
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "student.json"), json,
                System.Text.Encoding.UTF8);
        }
        catch { }
    }

    private void LoadStudentInfo()
    {
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LCSync", "student.json");
            if (System.IO.File.Exists(path))
            {
                var json = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
                // 简单 JSON 解析
                var nameMatch = System.Text.RegularExpressions.Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]*)\"");
                var idMatch = System.Text.RegularExpressions.Regex.Match(json, "\"id\"\\s*:\\s*\"([^\"]*)\"");
                if (nameMatch.Success) StudentName = nameMatch.Groups[1].Value;
                if (idMatch.Success) StudentIdCard = idMatch.Groups[1].Value;
            }
        }
        catch { }
    }

    private void LoadMySubmissions()
    {
        // 从教师端获取当前学生自己的提交记录
        // 简化方案：通过本地记录（实际可从教师端 filtered API 获取）
        MySubmissions.Clear();
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
```

- [ ] **Step 5: 修改 Connected 事件处理，记录服务器地址并在连接后刷新文件列表**

在 `OnConnected` 方法中、`DiagnosticInfo = "已连接，等待视频数据...";` 之前增加：

```csharp
            ServerFileUrl = $"http://{_serverIp}:{NetworkConfig.FilePort}";
            _ = RefreshFileList();
```

- [ ] **Step 6: 修改 Disconnected 事件处理**

在 `OnDisconnected` 方法的最后增加：

```csharp
            SharedFiles.Clear();
            ServerFileUrl = "";
```

- [ ] **Step 7: 修改 Connect 方法，保存 serverIp 供文件服务使用**

构造函数中已经有 `_serverIp = "";` 字段。在 `Connect` 方法中，连接前增加：
```csharp
            _serverIp = ServerIp; // 在调用 _client.ConnectAsync 之前
```

并且需要在类级别已经有一个 `_serverIp` 字符串字段。在现有字段区域添加：
```csharp
    private string _serverIp = "";
```

- [ ] **Step 8: 验证编译**

```bash
cd /sessions/adoring-eager-galileo/mnt/LCSync
dotnet build src/LCSync.App/LCSync.App.csproj 2>&1 | tail -20
```
Expected: Build succeeded

- [ ] **Step 9: Commit**

```bash
git add src/LCSync.App/ViewModels/StudentViewModel.cs
git commit -m "feat: extend StudentViewModel with file download and submission"
```

---

### Task 9: 改造 StudentWindow XAML（标签页布局）

**Files:**
- Modify: `src/LCSync.App/Views/StudentWindow.xaml`

- [ ] **Step 1: 重写 StudentWindow.xaml**

```xml
<Window x:Class="LCSync.Views.StudentWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="LCSync - 学生模式"
        Width="1100"
        Height="700"
        WindowStartupLocation="CenterScreen"
        Background="#FFFFFF">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- 顶部导航标签 -->
        <Border Grid.Row="0" Background="#F8FAFC" BorderBrush="#E2E8F0" BorderThickness="0,0,0,1">
            <StackPanel Orientation="Horizontal" Margin="20,0">
                <RadioButton Content="🖥 屏幕"
                             IsChecked="{Binding StudentTabIndex, Converter={StaticResource TabIndexConverter}, ConverterParameter=0}"
                             Style="{StaticResource TabRadioStyle}"/>
                <RadioButton Content="📁 共享文件"
                             IsChecked="{Binding StudentTabIndex, Converter={StaticResource TabIndexConverter}, ConverterParameter=1}"
                             Style="{StaticResource TabRadioStyle}"/>
                <RadioButton Content="📝 提交作业"
                             IsChecked="{Binding StudentTabIndex, Converter={StaticResource TabIndexConverter}, ConverterParameter=2}"
                             Style="{StaticResource TabRadioStyle}"/>
            </StackPanel>
        </Border>

        <!-- 内容区域 -->
        <Grid Grid.Row="1">
            <!-- Tab 0: 屏幕播放（原有内容） -->
            <Grid Visibility="{Binding StudentTabIndex, Converter={StaticResource TabVisibilityConverter}, ConverterParameter=0}">
                <Border Background="#1E293B">
                    <Grid>
                        <Viewbox Stretch="Uniform" StretchDirection="Both">
                            <Image Source="{Binding VideoFrame}" Stretch="Fill" Width="1920" Height="1080"/>
                        </Viewbox>

                        <!-- 未连接时的界面 -->
                        <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center">
                            <StackPanel.Style>
                                <Style TargetType="StackPanel">
                                    <Setter Property="Visibility" Value="Collapsed"/>
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding IsConnected}" Value="False">
                                            <Setter Property="Visibility" Value="Visible"/>
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </StackPanel.Style>

                            <Border Background="White" BorderBrush="#E2E8F0" BorderThickness="1" CornerRadius="16" Padding="60,50">
                                <StackPanel>
                                    <TextBlock Text="📺" FontSize="72" HorizontalAlignment="Center"/>
                                    <TextBlock Text="LCSync 学生端"
                                               FontSize="28" FontWeight="700" Foreground="#1E293B"
                                               HorizontalAlignment="Center" Margin="0,24,0,10"/>
                                    <TextBlock Text="观看教师屏幕共享"
                                               FontSize="15" Foreground="#64748B" HorizontalAlignment="Center"/>

                                    <StackPanel Margin="0,40,0,0">
                                        <TextBlock Text="教师IP地址" FontSize="13" Foreground="#475569" Margin="0,0,0,8"/>
                                        <TextBox Text="{Binding ServerIp, UpdateSourceTrigger=PropertyChanged}"
                                                 Background="#F8FAFC" BorderBrush="#E2E8F0" BorderThickness="1"
                                                 Padding="16,14" FontSize="15" MinWidth="280"/>

                                        <Button Content="连接"
                                                Command="{Binding ConnectCommand}"
                                                IsEnabled="{Binding IsConnected, Converter={StaticResource NotConverter}}"
                                                Background="#3B82F6" Foreground="White"
                                                Padding="0,14" FontSize="15" FontWeight="600"
                                                Margin="0,20,0,0" BorderThickness="0" Cursor="Hand">
                                            <Button.Template>
                                                <ControlTemplate TargetType="Button">
                                                    <Border Background="{TemplateBinding Background}" CornerRadius="10" Padding="{TemplateBinding Padding}">
                                                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                                    </Border>
                                                </ControlTemplate>
                                            </Button.Template>
                                        </Button>
                                    </StackPanel>
                                </StackPanel>
                            </Border>
                        </StackPanel>

                        <!-- 连接中界面 -->
                        <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center">
                            <StackPanel.Style>
                                <Style TargetType="StackPanel">
                                    <Setter Property="Visibility" Value="Collapsed"/>
                                    <Style.Triggers>
                                        <MultiDataTrigger>
                                            <MultiDataTrigger.Conditions>
                                                <Condition Binding="{Binding IsConnected}" Value="True"/>
                                                <Condition Binding="{Binding VideoFrame}" Value="{x:Null}"/>
                                            </MultiDataTrigger.Conditions>
                                            <Setter Property="Visibility" Value="Visible"/>
                                        </MultiDataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </StackPanel.Style>

                            <TextBlock Text="🔄" FontSize="48" HorizontalAlignment="Center"/>
                            <TextBlock Text="正在接收视频..." Foreground="#64748B" FontSize="16"
                                       HorizontalAlignment="Center" Margin="0,20,0,0"/>
                        </StackPanel>
                    </Grid>
                </Border>
            </Grid>

            <!-- Tab 1: 共享文件 -->
            <Grid Visibility="{Binding StudentTabIndex, Converter={StaticResource TabVisibilityConverter}, ConverterParameter=1}"
                  Margin="40">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <StackPanel Grid.Row="0" Orientation="Horizontal" HorizontalAlignment="Left">
                    <TextBlock Text="📁 教师共享文件" FontSize="28" FontWeight="700" Foreground="#1E293B"/>
                    <Button Content="🔄 刷新"
                            Command="{Binding RefreshFileListCommand}"
                            Background="#E2E8F0" Foreground="#475569"
                            Padding="16,8" FontSize="13" FontWeight="600"
                            Margin="20,5,0,0" BorderThickness="0" Cursor="Hand"
                            VerticalAlignment="Center">
                        <Button.Template>
                            <ControlTemplate TargetType="Button">
                                <Border Background="{TemplateBinding Background}" CornerRadius="8" Padding="{TemplateBinding Padding}">
                                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                </Border>
                            </ControlTemplate>
                        </Button.Template>
                    </Button>
                </StackPanel>

                <TextBlock Grid.Row="1" Text="{Binding FileNotificationText}"
                           Foreground="#10B981" FontSize="13" Margin="0,8,0,0"/>

                <Border Grid.Row="2" Background="#F8FAFC" BorderBrush="#E2E8F0" BorderThickness="1" CornerRadius="12" Margin="0,12,0,0">
                    <ScrollViewer>
                        <ItemsControl ItemsSource="{Binding SharedFiles}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Border BorderBrush="#E2E8F0" BorderThickness="0,0,0,1" Padding="16,12">
                                        <Grid>
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="*"/>
                                                <ColumnDefinition Width="100"/>
                                                <ColumnDefinition Width="Auto"/>
                                            </Grid.ColumnDefinitions>
                                            <StackPanel Grid.Column="0" Orientation="Horizontal">
                                                <TextBlock Text="{Binding FileName}" FontWeight="600" Foreground="#1E293B" VerticalAlignment="Center"/>
                                            </StackPanel>
                                            <TextBlock Grid.Column="1" Foreground="#64748B" VerticalAlignment="Center">
                                                <Run Text="{Binding Size, Converter={StaticResource FileSizeConverter}}"/>
                                            </TextBlock>
                                            <Button Grid.Column="2" Content="下载"
                                                    Command="{Binding Source={RelativeSource FindAncestor, AncestorType={x:Type Window}}, Path=DataContext.DownloadFileCommand}"
                                                    CommandParameter="{Binding Id}"
                                                    Background="#DBEAFE" Foreground="#2563EB"
                                                    Padding="16,8" FontSize="13" FontWeight="600" BorderThickness="0" Cursor="Hand">
                                                <Button.Template>
                                                    <ControlTemplate TargetType="Button">
                                                        <Border Background="{TemplateBinding Background}" CornerRadius="8" Padding="{TemplateBinding Padding}">
                                                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                                        </Border>
                                                    </ControlTemplate>
                                                </Button.Template>
                                            </Button>
                                        </Grid>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ScrollViewer>
                </Border>

                <TextBlock Grid.Row="3" Text="{Binding DownloadStatus}" Foreground="#64748B" FontSize="12" Margin="0,8,0,0"/>
            </Grid>

            <!-- Tab 2: 提交作业 -->
            <Grid Visibility="{Binding StudentTabIndex, Converter={StaticResource TabVisibilityConverter}, ConverterParameter=2}"
                  Margin="40">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>

                <TextBlock Grid.Row="0" Text="📝 提交作业" FontSize="28" FontWeight="700" Foreground="#1E293B"
                           Margin="0,0,0,20"/>

                <!-- 学生信息 -->
                <Border Grid.Row="1" Background="#F8FAFC" BorderBrush="#E2E8F0" BorderThickness="1" CornerRadius="12" Padding="20" Margin="0,0,0,16">
                    <StackPanel>
                        <TextBlock Text="您的信息" FontSize="14" FontWeight="600" Foreground="#475569" Margin="0,0,0,12"/>
                        <Grid Margin="0,0,0,0">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto"/>
                                <ColumnDefinition Width="200"/>
                                <ColumnDefinition Width="20"/>
                                <ColumnDefinition Width="Auto"/>
                                <ColumnDefinition Width="200"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column="0" Text="姓名：" Foreground="#64748B" VerticalAlignment="Center" FontSize="14"/>
                            <TextBox Grid.Column="1" Text="{Binding StudentName}" Background="White" BorderBrush="#E2E8F0" Padding="10,8" FontSize="14"/>
                            <TextBlock Grid.Column="3" Text="学号：" Foreground="#64748B" VerticalAlignment="Center" FontSize="14"/>
                            <TextBox Grid.Column="4" Text="{Binding StudentIdCard}" Background="White" BorderBrush="#E2E8F0" Padding="10,8" FontSize="14"/>
                            <Button Grid.Column="5" Content="保存信息"
                                    Command="{Binding SaveStudentInfoCommand}"
                                    Background="#E2E8F0" Foreground="#475569"
                                    Padding="12,8" FontSize="12" Margin="8,0,0,0" BorderThickness="0" Cursor="Hand"
                                    VerticalAlignment="Center"/>
                        </Grid>
                    </StackPanel>
                </Border>

                <!-- 文件选择与提交 -->
                <Border Grid.Row="2" Background="#F8FAFC" BorderBrush="#E2E8F0" BorderThickness="1" CornerRadius="12" Padding="20" Margin="0,0,0,16">
                    <StackPanel>
                        <TextBlock Text="提交作业" FontSize="14" FontWeight="600" Foreground="#475569" Margin="0,0,0,12"/>
                        <StackPanel Orientation="Horizontal">
                            <Button Content="选择文件"
                                    Command="{Binding SelectFileCommand}"
                                    Background="#E2E8F0" Foreground="#475569"
                                    Padding="16,10" FontSize="14" BorderThickness="0" Cursor="Hand"/>
                            <TextBlock Text="{Binding SelectedFileName}" Foreground="#64748B" VerticalAlignment="Center" Margin="12,0,20,0" FontSize="14"/>
                            <Button Content="📤 提交作业"
                                    Command="{Binding SubmitFileCommand}"
                                    Background="#3B82F6" Foreground="White"
                                    Padding="20,10" FontSize="14" FontWeight="600" BorderThickness="0" Cursor="Hand">
                                <Button.Template>
                                    <ControlTemplate TargetType="Button">
                                        <Border Background="{TemplateBinding Background}" CornerRadius="8" Padding="{TemplateBinding Padding}">
                                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                        </Border>
                                    </ControlTemplate>
                                </Button.Template>
                            </Button>
                        </StackPanel>
                    </StackPanel>
                </Border>

                <!-- 通知信息 -->
                <TextBlock Grid.Row="3" Text="{Binding FileNotificationText}"
                           Foreground="#10B981" FontSize="13" Margin="0,0,0,12"/>

                <!-- 已提交记录 -->
                <Border Grid.Row="4" Background="#F8FAFC" BorderBrush="#E2E8F0" BorderThickness="1" CornerRadius="12" Padding="20">
                    <StackPanel>
                        <TextBlock Text="我提交的记录" FontSize="14" FontWeight="600" Foreground="#475569" Margin="0,0,0,12"/>
                        <TextBlock Text="提交后刷新查看" Foreground="#94A3B8" FontSize="13"/>
                    </StackPanel>
                </Border>
            </Grid>
        </Grid>

        <!-- 底部状态栏 -->
        <Border Grid.Row="2" Background="White" BorderBrush="#E2E8F0" BorderThickness="0,1,0,0">
            <Grid Margin="24">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>

                <StackPanel Grid.Column="0" Orientation="Horizontal">
                    <Border Background="#F8FAFC" BorderBrush="#E2E8F0" BorderThickness="1" CornerRadius="8" Padding="14,10">
                        <TextBlock Foreground="#64748B" FontSize="13">
                            <Run Text="状态："/>
                            <Run Text="{Binding StatusText}" Foreground="#10B981" FontWeight="600"/>
                        </TextBlock>
                    </Border>

                    <Border Background="#F8FAFC" BorderBrush="#E2E8F0" BorderThickness="1" CornerRadius="8" Padding="14,10" Margin="12,0,0,0">
                        <TextBlock Text="{Binding DiagnosticInfo}" FontSize="11" Foreground="#64748B" FontFamily="Consolas"/>
                    </Border>
                </StackPanel>

                <StackPanel Grid.Column="2" Orientation="Horizontal">
                    <Button Content="⛶ 全屏"
                            Click="ToggleFullscreen"
                            Background="#3B82F6" Foreground="White"
                            Padding="20,10" FontSize="13" FontWeight="600" BorderThickness="0" Cursor="Hand">
                        <Button.Template>
                            <ControlTemplate TargetType="Button">
                                <Border Background="{TemplateBinding Background}" CornerRadius="8" Padding="{TemplateBinding Padding}">
                                    <StackPanel Orientation="Horizontal">
                                        <TextBlock Text="⛶" FontSize="16" Margin="0,0,8,0"/>
                                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                    </StackPanel>
                                </Border>
                            </ControlTemplate>
                        </Button.Template>
                    </Button>

                    <Button Content="断开"
                            Command="{Binding DisconnectCommand}"
                            IsEnabled="{Binding IsConnected}"
                            Background="#EF4444" Foreground="White"
                            Padding="20,10" FontSize="13" FontWeight="600"
                            Margin="12,0,0,0" BorderThickness="0" Cursor="Hand">
                        <Button.Template>
                            <ControlTemplate TargetType="Button">
                                <Border Background="{TemplateBinding Background}" CornerRadius="8" Padding="{TemplateBinding Padding}">
                                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                </Border>
                            </ControlTemplate>
                        </Button.Template>
                    </Button>
                </StackPanel>
            </Grid>
        </Border>
    </Grid>
</Window>
```

- [ ] **Step 2: 验证编译**

```bash
cd /sessions/adoring-eager-galileo/mnt/LCSync
dotnet build src/LCSync.App/LCSync.App.csproj 2>&1 | tail -20
```
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/LCSync.App/Views/StudentWindow.xaml
git commit -m "feat: redesign StudentWindow with tab layout"
```

---

### Task 10: 集成防火墙规则和验证完整功能

- [ ] **Step 1: 在 TeacherViewModel.StartBroadcast 中增加 9457 端口防火墙规则**

在 `FirewallUtils.AddFirewallRule(NetworkConfig.SignalingPort);` 行之后增加：

```csharp
            FirewallUtils.AddFirewallRule(NetworkConfig.FilePort);
```

- [ ] **Step 2: 完整构建验证**

```bash
cd /sessions/adoring-eager-galileo/mnt/LCSync
dotnet build src/LCSync.App/LCSync.App.csproj 2>&1
```
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/LCSync.App/ViewModels/TeacherViewModel.cs src/LCSync.App/Utils/FirewallUtils.cs
git commit -m "chore: add firewall rule for file service port 9457"
```

---

### 验证清单

构建通过后，手动验证以下场景：

| # | 场景 | 操作 | 预期结果 |
|---|------|------|----------|
| 1 | 教师端启动 | 选择教师模式 | 标签页正常显示，文件服务启动 |
| 2 | 教师上传文件 | 点击"上传共享文件"，选择一个文件 | 文件出现在列表中，通知显示成功 |
| 3 | 学生连接 | 输入教师 IP，点击连接 | 视频正常播放，文件标签可用 |
| 4 | 学生查看文件 | 切换到"共享文件"标签 | 看到教师上传的文件列表 |
| 5 | 学生下载文件 | 点击下载按钮 | 文件下载到本地 |
| 6 | 学生提交作业 | 填写姓名学号，选择文件，点击提交 | 通知"提交成功" |
| 7 | 教师查看作业 | 切换到"作业提交箱"标签 | 看到学生的提交记录 |
| 8 | 教师下载作业 | 点击下载按钮 | 文件保存到本地 |
| 9 | 设置页修改路径 | 修改共享目录路径，保存 | 路径持久化，新上传文件到新目录 |
| 10 | 学生信息持久化 | 填写姓名学号后关闭学生端，重新打开 | 姓名学号自动加载 |
