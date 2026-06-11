using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using System.Threading;
using System.IO;
using LCSync.Models;
using LCSync.Services;
using LCSync.Utils;

namespace LCSync.ViewModels;

public class TeacherViewModel : ViewModelBase, IDisposable
{
    private readonly Window _window;
    private readonly SignalingServer _signalingServer;
    private readonly DispatcherTimer _captureTimer;
    private CancellationTokenSource? _broadcastCts;

    private ScreenCaptureService? _captureService;
    private VideoEncoderService? _encoderService;

    private string _statusText = "未开始";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private int _peerCount = 0;
    public int PeerCount
    {
        get => _peerCount;
        set => SetProperty(ref _peerCount, value);
    }

    private string _ipAddress = "";
    public string IpAddress
    {
        get => _ipAddress;
        set => SetProperty(ref _ipAddress, value);
    }

    private bool _isBroadcasting = false;
    public bool IsBroadcasting
    {
        get => _isBroadcasting;
        set => SetProperty(ref _isBroadcasting, value);
    }

    private VideoConfig _selectedConfig = VideoConfig.Medium;
    public VideoConfig SelectedConfig
    {
        get => _selectedConfig;
        set
        {
            if (SetProperty(ref _selectedConfig, value))
            {
                UpdateCaptureInterval();
            }
        }
    }

    private ImageSource? _previewImage;
    public ImageSource? PreviewImage
    {
        get => _previewImage;
        set => SetProperty(ref _previewImage, value);
    }

    private string _diagnosticInfo = "";
    public string DiagnosticInfo
    {
        get => _diagnosticInfo;
        set => SetProperty(ref _diagnosticInfo, value);
    }

    private long _frameCount = 0;
    private long _bytesSent = 0;
    private long _sessionBytesSent = 0;
    private DateTime _lastReportTime = DateTime.Now;
    private DateTime _sessionStartTime = DateTime.Now;

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

    private bool _isFileSharingEnabled = false;
    public bool IsFileSharingEnabled
    {
        get => _isFileSharingEnabled;
        set
        {
            if (SetProperty(ref _isFileSharingEnabled, value))
            {
                if (_fileServer != null)
                {
                    _fileServer._isFileSharingActive = value;
                    if (value && !_fileServer.IsRunning)
                        _fileServer.Start();
                    else if (!value && _fileServer.IsRunning)
                        _fileServer.Stop();
                }
                OnPropertyChanged(nameof(FileSharingStatusText));
            }
        }
    }

    public string FileSharingStatusText => IsFileSharingEnabled ? "🟢 已开启" : "🔴 已关闭";

    private bool _isSubmissionEnabled = false;
    public bool IsSubmissionEnabled
    {
        get => _isSubmissionEnabled;
        set
        {
            if (SetProperty(ref _isSubmissionEnabled, value))
            {
                if (_fileServer != null)
                {
                    _fileServer._isSubmissionActive = value;
                }
                OnPropertyChanged(nameof(SubmissionStatusText));
            }
        }
    }

    public string SubmissionStatusText => IsSubmissionEnabled ? "🟢 已开启" : "🔴 已关闭";

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

    public System.Collections.Generic.List<VideoConfig> Presets { get; } = new()
    {
        VideoConfig.UltraLow,
        VideoConfig.Low360p,
        VideoConfig.Low480p,
        VideoConfig.Medium,
        VideoConfig.High,
        VideoConfig.FullHD
    };

    public ICommand StartBroadcastCommand { get; }
    public ICommand StopBroadcastCommand { get; }
    public ICommand UploadSharedFileCommand { get; }
    public ICommand DeleteSharedFileCommand { get; }
    public ICommand DownloadSubmissionCommand { get; }
    public ICommand OpenSharedDirCommand { get; }
    public ICommand OpenSubmissionDirCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand ToggleFileSharingCommand { get; }
    public ICommand ToggleSubmissionCommand { get; }
    public ICommand OpenHelpCommand { get; }

    public TeacherViewModel(Window window)
    {
        _window = window;
        _signalingServer = new SignalingServer();

        _captureService = new ScreenCaptureService(SelectedConfig.Width, SelectedConfig.Height);
        _encoderService = new VideoEncoderService(SelectedConfig);

        _captureTimer = new DispatcherTimer();
        UpdateCaptureInterval();

        _captureTimer.Tick += OnCaptureTimerTick;

        _signalingServer.PeerCountChanged += OnPeerCountChanged;
        _captureService.FrameCaptured += OnFrameCaptured;
        _encoderService.FrameEncoded += OnFrameEncoded;

        IpAddress = NetworkUtils.GetPrimaryLocalIP();
        _window.Closing += OnWindowClosing;

        StartBroadcastCommand = new AsyncRelayCommand(StartBroadcast, () => !IsBroadcasting);
        StopBroadcastCommand = new AsyncRelayCommand(StopBroadcast, () => IsBroadcasting);

        // 初始化文件服务（默认不启动，等用户开启文件共享）
        _fileServer = new FileServerService(NetworkConfig.FilePort);
        _fileServer._isFileSharingActive = false;
        _fileServer._isSubmissionActive = false;
        _fileServer.FileShared += OnFileShared;
        _fileServer.SubmissionReceived += OnSubmissionReceived;
        _fileServer.ErrorOccurred += (s, msg) =>
            _window.Dispatcher.Invoke(() => NotificationText = msg);

        // 加载配置
        var config = ConfigManager.Load();
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
        ToggleFileSharingCommand = new RelayCommand(ToggleFileSharing);
        ToggleSubmissionCommand = new RelayCommand(() => IsSubmissionEnabled = !IsSubmissionEnabled);
        OpenHelpCommand = new RelayCommand(OpenHelpDocument);

        DiagnosticInfo = "准备就绪，请设置画质后开始广播";
    }

    private void UpdateCaptureInterval()
    {
        _captureTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / SelectedConfig.Framerate);
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _ = StopBroadcast();
        Dispose();
    }

    private void OnPeerCountChanged(object? sender, int count)
    {
        _window.Dispatcher.Invoke(() =>
        {
            PeerCount = count;
            DiagnosticInfo = $"学生连接数: {count}";
        });
    }

    private void OnCaptureTimerTick(object? sender, EventArgs e)
    {
        try
        {
            _captureService?.CaptureFrame();
        }
        catch (Exception ex)
        {
            DiagnosticInfo = $"捕获错误: {ex.Message}";
        }
    }

    private void OnFrameCaptured(object? sender, byte[] frameData)
    {
        _frameCount++;

        try
        {
            if (_frameCount <= 3)
            {
                _window.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        int expectedStride = SelectedConfig.Width * 3;
                        var bitmap = BitmapSource.Create(
                            SelectedConfig.Width,
                            SelectedConfig.Height,
                            96,
                            96,
                            PixelFormats.Bgr24,
                            null,
                            frameData,
                            expectedStride);
                        bitmap.Freeze();
                        PreviewImage = bitmap;
                        DiagnosticInfo = $"预览帧 #{_frameCount}, 大小: {frameData.Length} 字节";
                    }
                    catch (Exception ex)
                    {
                        DiagnosticInfo = $"预览错误: {ex.Message}";
                    }
                });
            }

            _encoderService?.EncodeFrame(frameData);
        }
        catch (Exception ex)
        {
            _window.Dispatcher.Invoke(() => DiagnosticInfo = $"编码错误: {ex.Message}");
        }
    }

    private void OnFrameEncoded(object? sender, byte[] encodedData)
    {
        if (_broadcastCts != null && !_broadcastCts.Token.IsCancellationRequested)
        {
            try
            {
                _bytesSent += encodedData.Length;
                _sessionBytesSent += encodedData.Length;
                _ = _signalingServer.BroadcastVideoFrameAsync(encodedData, _broadcastCts.Token);

                var now = DateTime.Now;
                var elapsed = (now - _lastReportTime).TotalSeconds;
                var sessionElapsed = (now - _sessionStartTime).TotalSeconds;

                if (elapsed >= 2)
                {
                    double currentMbps = (_bytesSent * 8.0 / 1_000_000) / elapsed;
                    double avgMbps = (_sessionBytesSent * 8.0 / 1_000_000) / sessionElapsed;
                    double fps = _frameCount / elapsed;

                    DiagnosticInfo = $"总帧:{_frameCount} 平均:{avgMbps:F2}Mbps 当前:{currentMbps:F2}Mbps {fps:F1}fps 学生:{PeerCount}";

                    _bytesSent = 0;
                    _lastReportTime = now;
                }
            }
            catch
            {
            }
        }
    }

    private async System.Threading.Tasks.Task StartBroadcast()
    {
        if (IsBroadcasting)
            return;

        try
        {
            _broadcastCts = new CancellationTokenSource();
            _frameCount = 0;
            _bytesSent = 0;
            _sessionBytesSent = 0;
            _lastReportTime = DateTime.Now;
            _sessionStartTime = DateTime.Now;

            _captureService?.Dispose();
            _captureService = new ScreenCaptureService(SelectedConfig.Width, SelectedConfig.Height);
            _captureService.FrameCaptured += OnFrameCaptured;
            _captureService.Start();

            _encoderService?.Dispose();
            _encoderService = new VideoEncoderService(SelectedConfig);
            _encoderService.FrameEncoded += OnFrameEncoded;
            _encoderService.Start();

            await _signalingServer.StartAsync(NetworkConfig.SignalingPort);
            _captureTimer.Start();

            IsBroadcasting = true;
            StatusText = "正在广播";
            DiagnosticInfo = $"开始捕获 {SelectedConfig.Width}x{SelectedConfig.Height} @ {SelectedConfig.GetBitrateText()} {SelectedConfig.Framerate}fps";
            FirewallUtils.AddFirewallRule(NetworkConfig.SignalingPort);
            FirewallUtils.AddFirewallRule(NetworkConfig.FilePort);

            ((AsyncRelayCommand)StartBroadcastCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)StopBroadcastCommand).RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            StatusText = $"启动失败: {ex.Message}";
            DiagnosticInfo = $"启动失败: {ex.Message}";
        }
    }

    private async System.Threading.Tasks.Task StopBroadcast()
    {
        if (!IsBroadcasting)
            return;

        try
        {
            _broadcastCts?.Cancel();
            _captureTimer.Stop();
            _encoderService?.Stop();
            _captureService?.Stop();
            await _signalingServer.StopAsync();

            IsBroadcasting = false;
            StatusText = "已停止";
            PeerCount = 0;
            PreviewImage = null;
            double totalMb = _sessionBytesSent * 8.0 / 1_000_000;
            DiagnosticInfo = $"共{_frameCount}帧 总流量:{totalMb:F2}MB 平均码率:{totalMb * 8 / Math.Max(1, (DateTime.Now - _sessionStartTime).TotalSeconds):F2}Mbps";

            ((AsyncRelayCommand)StartBroadcastCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)StopBroadcastCommand).RaiseCanExecuteChanged();
        }
        catch
        {
        }
    }

    // ── 文件共享事件处理方法 ─────────────────────────────────

    private void OnFileShared(object? sender, FileItem item)
    {
        _window.Dispatcher.Invoke(() =>
        {
            RefreshFileList();
            NotificationText = $"已共享文件: {item.FileName}";
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

                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                using var content = new System.Net.Http.ByteArrayContent(fileBytes);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                var encodedName = Uri.EscapeDataString(fileName);
                var response = await client.PostAsync(
                    $"http://localhost:{NetworkConfig.FilePort}/api/teacher/upload?filename={encodedName}", content);

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

    public void DeleteSharedFile(string fileId)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"DeleteSharedFile called with id: {fileId}");
            if (_fileServer == null || string.IsNullOrEmpty(fileId))
            {
                NotificationText = "删除失败: 无效参数";
                return;
            }

            if (_fileServer.RemoveFileById(fileId))
            {
                RefreshFileList();
                NotificationText = "文件已删除";
            }
            else
            {
                NotificationText = "文件不存在";
            }
        }
        catch (Exception ex)
        {
            NotificationText = $"删除异常: {ex.Message}";
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

    private void ToggleFileSharing()
    {
        IsFileSharingEnabled = !IsFileSharingEnabled;
        if (IsFileSharingEnabled)
        {
            _fileServer._isFileSharingActive = true;
            _fileServer.Start();
            RefreshFileList();
            RefreshSubmissionList();
        }
        else
        {
            _fileServer._isFileSharingActive = false;
            _fileServer.Stop();
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

    private void OpenHelpDocument()
    {
        try
        {
            // 帮助文档放在程序运行目录下，命名为 使用帮助.docx
            var exeDir = System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            var helpPath = System.IO.Path.Combine(exeDir, "使用帮助.docx");
            if (System.IO.File.Exists(helpPath))
            {
                System.Diagnostics.Process.Start(helpPath);
            }
            else
            {
                // 也检查项目根目录
                helpPath = System.IO.Path.Combine(
                    System.IO.Directory.GetCurrentDirectory(), "使用帮助.docx");
                if (System.IO.File.Exists(helpPath))
                    System.Diagnostics.Process.Start(helpPath);
                else
                    NotificationText = $"未找到使用帮助文档，请将文件放在 {exeDir} 目录下";
            }
        }
        catch (Exception ex)
        {
            NotificationText = $"打开帮助失败: {ex.Message}";
        }
    }

    public void Dispose()
    {
        _captureTimer.Stop();
        _signalingServer.Dispose();
        _captureService?.Dispose();
        _encoderService?.Dispose();
        _broadcastCts?.Dispose();
        _fileServer?.Dispose();
    }
}
