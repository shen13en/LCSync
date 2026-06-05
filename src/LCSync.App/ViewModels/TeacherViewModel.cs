using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using System.Threading;
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
                var sessionElapsed = (now -