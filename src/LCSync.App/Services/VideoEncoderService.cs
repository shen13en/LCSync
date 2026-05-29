using System;
using LCSync.Models;

namespace LCSync.Services;

public class VideoEncoderService : IDisposable
{
    private bool _disposed;
    private readonly VideoConfig _config;
    private readonly LightweightVideoEncoder _encoder;

    public event EventHandler<byte[]>? FrameEncoded;

    public VideoEncoderService(VideoConfig config)
    {
        _config = config;
        _encoder = new LightweightVideoEncoder(config);
        _encoder.FrameEncoded += (s, data) => FrameEncoded?.Invoke(this, data);
    }

    public void Start()
    {
        _encoder.Start();
    }

    public void EncodeFrame(byte[] frameData)
    {
        if (_disposed)
            return;

        _encoder.EncodeFrame(frameData);
    }

    public void Stop()
    {
        _encoder.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _encoder.Dispose();
    }
}
