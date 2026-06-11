using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LCSync.Services;

public class ScreenCaptureService : IDisposable
{
    private bool _disposed;
    private Bitmap? _screenBitmap;
    private Graphics? _screenGraphics;
    private Bitmap? _resizedBitmap;
    private Graphics? _resizedGraphics;

    public event EventHandler<byte[]>? FrameCaptured;

    public int Width { get; private set; }
    public int Height { get; private set; }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    public ScreenCaptureService(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public void Start()
    {
        int screenWidth = GetSystemMetrics(SM_CXSCREEN);
        int screenHeight = GetSystemMetrics(SM_CYSCREEN);

        _screenBitmap = new Bitmap(screenWidth, screenHeight, PixelFormat.Format24bppRgb);
        _screenGraphics = Graphics.FromImage(_screenBitmap);

        _resizedBitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
        _resizedGraphics = Graphics.FromImage(_resizedBitmap);
        _resizedGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
    }

    public void CaptureFrame()
    {
        if (_disposed || _screenGraphics == null || _screenBitmap == null || 
            _resizedGraphics == null || _resizedBitmap == null)
            return;

        try
        {
            _screenGraphics.CopyFromScreen(0, 0, 0, 0, new Size(_screenBitmap.Width, _screenBitmap.Height), CopyPixelOperation.SourceCopy);
            _resizedGraphics.DrawImage(_screenBitmap, 0, 0, Width, Height);

            var data = _resizedBitmap.LockBits(
                new Rectangle(0, 0, Width, Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);

            var bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            _resizedBitmap.UnlockBits(data);

            FrameCaptured?.Invoke(this, bytes);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"捕获错误: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (_disposed)
            return;

        _disposed = true;
        _screenGraphics?.Dispose();
        _screenBitmap?.Dispose();
        _resizedGraphics?.Dispose();
        _resizedBitmap?.Dispose();
        _screenGraphics = null;
        _screenBitmap = null;
        _resizedGraphics = null;
        _resizedBitmap = null;
    }

    public void Dispose()
    {
        Stop();
    }
}