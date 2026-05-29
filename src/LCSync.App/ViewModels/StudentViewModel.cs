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

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }

    public StudentViewModel(Window window)
    {
        _window = window;
        _client = new SignalingClient();

        _client.Connected += OnConnected;
        _client.Disconnected += OnDisconnected;
        _client.PeerCountUpdated += OnPeerCountUpdated;
        _client.VideoFrameReceived += OnVideoFrameReceived;

        _window.Closing += OnWindowClosing;
        
        ConnectCommand = new AsyncRelayCommand(Connect, () => !IsConnected && !string.IsNullOrWhiteSpace(ServerIp));
        DisconnectCommand = new AsyncRelayCommand(Disconnect, () => IsConnected);
        
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
                    using (var stream = new MemoryStream(frameData))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = stream;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        VideoFrame = bitmap;
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

    public void Dispose()
    {
        _client.Dispose();
    }
}
