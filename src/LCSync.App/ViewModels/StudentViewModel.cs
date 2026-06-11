using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Text;
using LCSync.Services;
using LCSync.Models;

namespace LCSync.ViewModels;

public class StudentViewModel : ViewModelBase, IDisposable
{
    public event EventHandler? Disconnected;
    
    private readonly Window _window;
    private readonly SignalingClient _client;

    private string _serverIp = "";
    public string ServerIp
    {
        get => _serverIp;
        set => SetProperty(ref _serverIp, value);
    }

    private string _statusText = "未连接";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private bool _isConnected = false;
    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    private int _peerCount = 0;
    public int PeerCount
    {
        get => _peerCount;
        set => SetProperty(ref _peerCount, value);
    }

    private ImageSource? _videoFrame;
    public ImageSource? VideoFrame
    {
        get => _videoFrame;
        set => SetProperty(ref _videoFrame, value);
    }

    private string _diagnosticInfo = "";
    public string DiagnosticInfo
    {
        get => _diagnosticInfo;
        set => SetProperty(ref _diagnosticInfo, value);
    }

    private int _videoWidth = 1280;
    private int _videoHeight = 720;
    private long _frameCount = 0;
    private long _totalBytes = 0;
    private DateTime _lastUpdateTime = DateTime.Now;
    private long _lastBytes = 0;

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

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand RefreshFileListCommand { get; }
    public ICommand DownloadFileCommand { get; }
    public ICommand SelectFileCommand { get; }
    public ICommand SubmitFileCommand { get; }
    public ICommand SaveStudentInfoCommand { get; }
    public ICommand OpenHelpCommand { get; }

    public StudentViewModel(Window window)
    {
        _window = window;
        _client = new SignalingClient();

        _client.Connected += OnConnected;
        _client.Disconnected += OnDisconnected;
        _client.FileNotifyReceived += (s, fileName) =>
        {
            _window.Dispatcher.Invoke(async () =>
            {
                FileNotificationText = $"教师更新了文件: {fileName}";
                await RefreshFileList();
            });
        };
        _client.PeerCountUpdated += OnPeerCountUpdated;
        _client.VideoFrameReceived += OnVideoFrameReceived;

        _window.Closing += OnWindowClosing;
        
        ConnectCommand = new AsyncRelayCommand(Connect, () => !IsConnected && !string.IsNullOrWhiteSpace(ServerIp));
        DisconnectCommand = new AsyncRelayCommand(Disconnect, () => IsConnected);
        
        // 初始化文件相关命令
        RefreshFileListCommand = new AsyncRelayCommand(RefreshFileList);
        DownloadFileCommand = new RelayCommand<string>(DownloadFile);
        SelectFileCommand = new RelayCommand(SelectFile);
        SubmitFileCommand = new AsyncRelayCommand(SubmitFile);
        SaveStudentInfoCommand = new RelayCommand(SaveStudentInfo);
        OpenHelpCommand = new RelayCommand(OpenHelpDocument);

        // 加载本地缓存的学生信息
        LoadStudentInfo();

        DiagnosticInfo = "等待连接...";
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _ = Disconnect();
        Dispose();
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        _window.Dispatcher.Invoke(() =>
        {
            IsConnected = true;
            StatusText = "已连接";
            _frameCount = 0;
            _totalBytes = 0;
            _lastUpdateTime = DateTime.Now;
            _lastBytes = 0;
            DiagnosticInfo = "已连接，等待视频数据...";
            ServerFileUrl = $"http://{_serverIp}:{NetworkConfig.FilePort}";
            _ = RefreshFileList();

            ((AsyncRelayCommand)ConnectCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
        });
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        _window.Dispatcher.Invoke(() =>
        {
            IsConnected = false;
            StatusText = "已断开";
            DiagnosticInfo = "连接已断开";
            Disconnected?.Invoke(this, EventArgs.Empty);
            SharedFiles.Clear();
            ServerFileUrl = "";

            ((AsyncRelayCommand)ConnectCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
        });
    }

    private void OnPeerCountUpdated(object? sender, int count)
    {
        _window.Dispatcher.Invoke(() => PeerCount = count);
    }

    private void OnVideoFrameReceived(object? sender, byte[] frameData)
    {
        try
        {
            _frameCount++;
            _totalBytes += frameData.Length;

            _window.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = new MemoryStream(frameData);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    // 替换旧的 ImageSource，强制刷新
                    var old = VideoFrame;
                    VideoFrame = bitmap;
                    if (old != null)
                    {
                        // 让 GC 回收旧的 BitmapSource
                        var stale = old as BitmapSource;
                        if (stale != null)
                        {
                            System.GC.KeepAlive(stale);
                        }
                    }

                    var now = DateTime.Now;
                    if ((now - _lastUpdateTime).TotalSeconds >= 1)
                    {
                        double bitrate = (_totalBytes - _lastBytes) * 8.0 / 1024.0 / 1024.0 / (now - _lastUpdateTime).TotalSeconds;
                        DiagnosticInfo = $"已接收 {_frameCount} 帧 · {_totalBytes / 1024.0 / 1024.0:F1} MB · {bitrate:F1} Mbps";
                        _lastUpdateTime = now;
                        _lastBytes = _totalBytes;
                    }
                }
                catch
                {
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
        catch
        {
        }
    }

    private async System.Threading.Tasks.Task Connect()
    {
        if (IsConnected || string.IsNullOrWhiteSpace(ServerIp))
            return;

        try
        {
            StatusText = "正在连接...";
            DiagnosticInfo = $"正在连接到 {ServerIp}:{NetworkConfig.SignalingPort}...";
            await _client.ConnectAsync(ServerIp, NetworkConfig.SignalingPort);
        }
        catch (Exception ex)
        {
            StatusText = $"连接失败: {ex.Message}";
            DiagnosticInfo = $"连接失败: {ex.Message}";
        }
    }

    private async System.Threading.Tasks.Task Disconnect()
    {
        if (!IsConnected)
            return;

        try
        {
            await _client.DisconnectAsync();
            VideoFrame = null;
        }
        catch
        {
        }
    }

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
                var files = (List<FileItem>)serializer.ReadObject(ms);
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

    public void DownloadFile(string fileId)
    {
        if (string.IsNullOrWhiteSpace(ServerFileUrl) || string.IsNullOrEmpty(fileId)) return;

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
                var url = $"{ServerFileUrl}/api/files/{fileId}";
                var savePath = dialog.FileName;
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                var data = client.GetByteArrayAsync(url).GetAwaiter().GetResult();
                System.IO.File.WriteAllBytes(savePath, data);
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
            using var content = new System.Net.Http.ByteArrayContent(fileBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            var encodedName = Uri.EscapeDataString(SelectedFileName);
            var encodedStudentName = Uri.EscapeDataString(StudentName);
            var encodedStudentId = Uri.EscapeDataString(StudentIdCard);
            var response = await client.PostAsync(
                $"{ServerFileUrl}/api/upload?filename={encodedName}&studentName={encodedStudentName}&studentId={encodedStudentId}", content);
            var result = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                FileNotificationText = $"✅ 提交成功: {SelectedFileName}";
                SelectedFilePath = "";
                SelectedFileName = "";
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
                var nameMatch = System.Text.RegularExpressions.Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]*)\"");
                var idMatch = System.Text.RegularExpressions.Regex.Match(json, "\"id\"\\s*:\\s*\"([^\"]*)\"");
                if (nameMatch.Success) StudentName = nameMatch.Groups[1].Value;
                if (idMatch.Success) StudentIdCard = idMatch.Groups[1].Value;
            }
        }
        catch { }
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private void OpenHelpDocument()
    {
        try
        {
            var exeDir = System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            var helpPath = System.IO.Path.Combine(exeDir, "使用帮助.docx");
            if (System.IO.File.Exists(helpPath))
                System.Diagnostics.Process.Start(helpPath);
            else
            {
                helpPath = System.IO.Path.Combine(
                    System.IO.Directory.GetCurrentDirectory(), "使用帮助.docx");
                if (System.IO.File.Exists(helpPath))
                    System.Diagnostics.Process.Start(helpPath);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
