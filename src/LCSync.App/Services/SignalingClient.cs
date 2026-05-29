using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using LCSync.Models;
using WebSocketSharp;

namespace LCSync.Services;

public class SignalingClient : IDisposable
{
    private bool _disposed;
    private WebSocket? _webSocket;
    private readonly string _studentId;
    private readonly List<byte> _messageBuffer = new List<byte>();
    private bool _isConnected;

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<int>? PeerCountUpdated;
    public event EventHandler<byte[]>? VideoFrameReceived;

    public bool IsConnected => _isConnected;

    public SignalingClient()
    {
        _studentId = Guid.NewGuid().ToString("N");
    }

    public async System.Threading.Tasks.Task ConnectAsync(string serverIp, int port)
    {
        if (IsConnected)
            return;

        _webSocket = new WebSocket($"ws://{serverIp}:{port}/");

        _webSocket.OnOpen += (sender, e) =>
        {
            _isConnected = true;
            Connected?.Invoke(this, EventArgs.Empty);
            var joinMsg = CreateSimpleMessage(MessageType.Join, _studentId);
            _webSocket.Send(joinMsg);
        };

        _webSocket.OnMessage += (sender, e) =>
        {
            try
            {
                if (!e.IsBinary)
                    return;

                _messageBuffer.AddRange(e.RawData);
                ProcessBuffer();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"接收错误: {ex.Message}");
            }
        };

        _webSocket.OnError += (sender, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"错误: {e.Message}");
        };

        _webSocket.OnClose += (sender, e) =>
        {
            _isConnected = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
        };

        _webSocket.Connect();
    }

    private void ProcessBuffer()
    {
        if (_messageBuffer.Count < 5)
            return;

        var buffer = _messageBuffer.ToArray();
        _messageBuffer.Clear();

        try
        {
            var messageType = (MessageType)buffer[0];
            var dataLength = BitConverter.ToInt32(buffer, 1);

            if (buffer.Length < 5 + dataLength)
            {
                _messageBuffer.AddRange(buffer);
                return;
            }

            var payload = new byte[dataLength];
            Buffer.BlockCopy(buffer, 5, payload, 0, dataLength);

            HandleMessage(messageType, payload);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"处理消息错误: {ex.Message}");
        }
    }

    private void HandleMessage(MessageType type, byte[] payload)
    {
        try
        {
            switch (type)
            {
                case MessageType.Joined:
                    if (payload.Length >= 4)
                    {
                        var idLength = BitConverter.ToInt32(payload, 0);
                        if (payload.Length >= 4 + idLength)
                        {
                            var id = Encoding.UTF8.GetString(payload, 4, idLength);
                        }
                    }
                    break;

                case MessageType.PeerCount:
                    if (payload.Length >= 4)
                    {
                        var count = BitConverter.ToInt32(payload, 0);
                        PeerCountUpdated?.Invoke(this, count);
                    }
                    break;

                case MessageType.VideoFrame:
                    VideoFrameReceived?.Invoke(this, payload);
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"处理消息类型 {type} 错误: {ex.Message}");
        }
    }

    private byte[] CreateSimpleMessage(MessageType type, string text)
    {
        var textBytes = Encoding.UTF8.GetBytes(text);
        var lengthBytes = BitConverter.GetBytes(textBytes.Length);
        var result = new byte[1 + 4 + textBytes.Length];
        result[0] = (byte)type;
        Buffer.BlockCopy(lengthBytes, 0, result, 1, 4);
        Buffer.BlockCopy(textBytes, 0, result, 5, textBytes.Length);
        return result;
    }

    public async System.Threading.Tasks.Task DisconnectAsync()
    {
        if (_webSocket == null)
            return;

        try
        {
            if (_webSocket.IsAlive)
            {
                _webSocket.Close();
            }
        }
        catch
        {
        }

        _webSocket = null;

        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            if (_webSocket?.IsAlive == true)
            {
                _webSocket.Close();
            }
        }
        catch
        {
        }
    }
}
