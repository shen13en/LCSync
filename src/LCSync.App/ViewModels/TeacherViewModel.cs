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

    public void Dispose()
    {
        _captureTimer.Stop();
        _signalingServer.Dispose();
        _captureService?.Dispose();
        _encoderService?.Dispose();
        _broadcastCts?.Dispose();
    }
}
