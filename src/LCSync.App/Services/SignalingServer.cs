using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Threading;
using LCSync.Models;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace LCSync.Services;

public class SignalingServer : IDisposable
{
    private bool _disposed;
    private WebSocketServer? _webSocketServer;
    internal readonly ConcurrentDictionary<string, IWebSocketSession> _students = new ConcurrentDictionary<string, IWebSocketSession>();
    private int _port;
    private byte[]? _broadcastBuffer;
    private int _broadcastBufferSize = 0;

    public event EventHandler<int>? PeerCountChanged;

    public bool IsRunning { get; private set; }

    public SignalingServer()
    {
    }

    public void Start(int port)
    {
        if (IsRunning)
            return;

        _port = port;
        _webSocketServer = new WebSocketServer(port);
        _webSocketServer.AddWebSocketService<VideoRelayBehavior>("/", () => new VideoRelayBehavior(this));
        _webSocketServer.Start();
        IsRunning = true;
    }

    internal void OnPeerConnected(string studentId, IWebSocketSession session)
    {
        _students.TryAdd(studentId, session);
        PeerCountChanged?.Invoke(this, _students.Count);
    }

    internal void OnPeerDisconnected(string studentId)
    {
        IWebSocketSession removed;
        _students.TryRemove(studentId, out removed);
        PeerCountChanged?.Invoke(this, _students.Count);
    }

    public void BroadcastVideoFrame(byte[] frameData)
    {
        if (!IsRunning)
            return;

        int requiredSize = 5 + frameData.Length;
        if (_broadcastBuffer == null || _broadcastBufferSize < requiredSize)
        {
            _broadcastBuffer = new byte[Math.Max(requiredSize, 256 * 1024)];
            _broadcastBufferSize = _broadcastBuffer.Length;
        }

        _broadcastBuffer[0] = (byte)MessageType.VideoFrame;
        Buffer.BlockCopy(BitConverter.GetBytes(frameData.Length), 0, _broadcastBuffer, 1, 4);
        Buffer.BlockCopy(frameData, 0, _broadcastBuffer, 5, frameData.Length);

        foreach (var student in _students.Values)
        {
            try
            {
                var ws = student.Context.WebSocket;
                if (ws.IsAlive)
                {
                    byte[] sendBuffer = new byte[requiredSize];
                    Buffer.BlockCopy(_broadcastBuffer, 0, sendBuffer, 0, requiredSize);
                    ws.Send(sendBuffer);
                }
            }
            catch
            {
            }
        }
    }

    public async System.Threading.Tasks.Task BroadcastVideoFrameAsync(byte[] frameData, CancellationToken cancellationToken)
    {
        BroadcastVideoFrame(frameData);
    }

    public async System.Threading.Tasks.Task StartAsync(int port)
    {
        Start(port);
    }

    public async System.Threading.Tasks.Task StopAsync()
    {
        Stop();
    }

    public void Stop()
    {
        if (!IsRunning)
            return;

        IsRunning = false;

        _students.Clear();

        try
        {
            _webSocketServer?.Stop();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
    }
}

internal class VideoRelayBehavior : WebSocketBehavior
{
    private readonly SignalingServer _server;
    private string? _studentId;
    private byte[]? _messageBuffer;
    private int _messageBufferSize = 0;

    public VideoRelayBehavior(SignalingServer server)
    {
        _server = server;
    }

    protected override void OnOpen()
    {
        _studentId = Guid.NewGuid().ToString("N");
        _server.OnPeerConnected(_studentId, this);
    }

    protected override void OnMessage(MessageEventArgs e)
    {
        try
        {
            if (!e.IsBinary || e.RawData.Length < 5)
                return;

            var messageType = (MessageType)e.RawData[0];
            var dataLength = BitConverter.ToInt32(e.RawData, 1);

            if (e.RawData.Length < 5 + dataLength)
                return;

            switch (messageType)
            {
                case MessageType.Join:
                    var joinedPayload = CreateStudentIdPayload(_studentId ?? "");
                    Send(CreateMessage(MessageType.Joined, joinedPayload));

                    var peerCountPayload = BitConverter.GetBytes(_server._students.Count);
                    _server.BroadcastVideoFrame(CreateMessage(MessageType.PeerCount, peerCountPayload));
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"处理消息错误: {ex.Message}");
        }
    }

    protected override void OnClose(CloseEventArgs e)
    {
        if (_studentId != null)
        {
            _server.OnPeerDisconnected(_studentId);
        }
    }

    private byte[] CreateStudentIdPayload(string studentId)
    {
        var idBytes = Encoding.UTF8.GetBytes(studentId);
        var lengthBytes = BitConverter.GetBytes(idBytes.Length);
        var result = new byte[4 + idBytes.Length];
        Buffer.BlockCopy(lengthBytes, 0, result, 0, 4);
        Buffer.BlockCopy(idBytes, 0, result, 4, idBytes.Length);
        return result;
    }

    private byte[] CreateMessage(MessageType type, byte[] data)
    {
        int requiredSize = 5 + data.Length;
        if (_messageBuffer == null || _messageBufferSize < requiredSize)
        {
            _messageBuffer = new byte[Math.Max(requiredSize, 4096)];
            _messageBufferSize = _messageBuffer.Length;
        }

        _messageBuffer[0] = (byte)type;
        Buffer.BlockCopy(BitConverter.GetBytes(data.Length), 0, _messageBuffer, 1, 4);
        Buffer.BlockCopy(data, 0, _messageBuffer, 5, data.Length);
        
        byte[] result = new byte[requiredSize];
        Buffer.BlockCopy(_messageBuffer, 0, result, 0, requiredSize);
        return result;
    }
}