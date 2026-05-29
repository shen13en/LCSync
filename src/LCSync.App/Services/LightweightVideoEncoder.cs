using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using LCSync.Models;

namespace LCSync.Services;

public class LightweightVideoEncoder : IDisposable
{
    private bool _disposed;
    private readonly VideoConfig _config;
    private byte[]? _resizeBuffer;
    private GCHandle _resizeHandle;
    private static ImageCodecInfo? _jpegEncoder;

    public event EventHandler<byte[]>? FrameEncoded;

    public LightweightVideoEncoder(VideoConfig config)
    {
        _config = config;
    }

    public void Start()
    {
        if (_disposed)
            return;

        int bufferSize = _config.Width * _config.Height * 3;
        _resizeBuffer = new byte[bufferSize];
        _resizeHandle = GCHandle.Alloc(_resizeBuffer, GCHandleType.Pinned);

        if (_jpegEncoder == null)
        {
            _jpegEncoder = ImageCodecInfo.GetImageEncoders().First(e => e.MimeType == "image/jpeg");
        }
    }

    public void EncodeFrame(byte[] rgbData)
    {
        if (_disposed || _resizeBuffer == null || !_resizeHandle.IsAllocated || _jpegEncoder == null)
            return;

        try
        {
            int expectedSize = _config.Width * _config.Height * 3;
            if (rgbData.Length < expectedSize)
                return;

            Buffer.BlockCopy(rgbData, 0, _resizeBuffer, 0, Math.Min(rgbData.Length, expectedSize));

            using (var bitmap = new Bitmap(_config.Width, _config.Height, _config.Width * 3, 
                PixelFormat.Format24bppRgb, _resizeHandle.AddrOfPinnedObject()))
            {
                using (var ms = new MemoryStream())
                {
                    var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)_config.JpegQuality);
                    
                    bitmap.Save(ms, _jpegEncoder, encoderParams);
                    var jpegData = ms.ToArray();
                    
                    FrameEncoded?.Invoke(this, jpegData);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"编码失败: {ex.Message}");
        }
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_resizeHandle.IsAllocated)
            _resizeHandle.Free();
    }
}