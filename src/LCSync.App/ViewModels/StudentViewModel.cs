using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.IO;
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
          