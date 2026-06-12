using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SbModbus.Protocol;
using SbModbus.Transport;
using SbModbus.Utils;

namespace SbModbus.TcpStream;

/// <summary>
///   UDP 服务器流 — 基于 .NET 原生 UdpClient，绑定端口监听并管理远程端点会话
/// </summary>
public class SbUdpStreamServer : IModbusStreamServer
{
  private static readonly TimeSpan TaskWaitTimeout = TimeSpan.FromSeconds(3);

  private readonly IPEndPoint _localEndPoint;

  private readonly ConcurrentDictionary<IPEndPoint, SbUdpSessionStream> _sessions = new();

  private readonly ConcurrentDictionary<byte, ChannelWriter<ModbusFrameMessage>> _stationWriters = new();

  private TryParseFrameDelegate _frameParser = Services.ModbusServer.FrameParser.TryParseTcp;

  private volatile bool _isDisposed;
  private volatile bool _isListening;
  private CancellationTokenSource? _receiveCts;
  private Task? _receiveTask;

  private UdpClient? _udpClient;

  /// <summary>
  ///   当前活跃会话数
  /// </summary>
  public int SessionCount => _sessions.Count;

  /// <summary>
  ///   服务器是否正在监听
  /// </summary>
  public bool IsListening => _isListening;

  /// <inheritdoc />
  public TryParseFrameDelegate FrameParser
  {
    set => _frameParser = value;
  }

  /// <inheritdoc />
  public void RegisterStation(byte stationId, ChannelWriter<ModbusFrameMessage> writer)
  {
    _stationWriters[stationId] = writer;
  }

  /// <inheritdoc />
  public void UnregisterStation(byte stationId)
  {
    _stationWriters.TryRemove(stationId, out _);
  }

  /// <summary>
  ///   新会话（远程端点首次发来数据）时触发。调用方应在此事件中为每个会话创建 <see cref="Services.ModbusServer.BaseModbusServer" />
  /// </summary>
  public event Action<IModbusStream>? OnSessionConnected;

  /// <summary>
  ///   会话断开时触发
  /// </summary>
  public event Action<IModbusStream>? OnSessionDisconnected;

  #region 接收循环

  /// <summary>
  ///   后台接收循环 — 接收数据并路由到对应的会话
  /// </summary>
  private async Task RunReceiveLoopAsync(CancellationToken ct)
  {
    try
    {
      while (!ct.IsCancellationRequested)
      {
        var client = _udpClient;
        if (client == null) break;

        UdpReceiveResult result;
        try
        {
#if NET6_0_OR_GREATER
          result = await client.ReceiveAsync(ct).ConfigureAwait(false);
#else
          result = await client.ReceiveAsync().ConfigureAwait(false);
#endif
        }
        catch (SocketException ex)
        {
          Logger.Log(LogLevel.Warning, $"UDP server receive error: {ex.SocketErrorCode} - {ex.Message}");
          break;
        }
        catch (OperationCanceledException)
        {
          break;
        }
        catch (ObjectDisposedException)
        {
          break;
        }

        var remoteEp = result.RemoteEndPoint;

        // 路由到已有会话或创建新会话
        if (_sessions.TryGetValue(remoteEp, out var existingSession))
        {
          existingSession.EnqueueReceivedData(result.Buffer);
        }
        else
        {
          var newSession = new SbUdpSessionStream(this, remoteEp);
          if (_sessions.TryAdd(remoteEp, newSession))
          {
            newSession.AutoReceive = true;
            newSession.OnDataReceived += (_, d) => OnSessionDataReceived(remoteEp, newSession, d);
            newSession.EnqueueReceivedData(result.Buffer);
            Logger.Information($"UDP new session from {remoteEp}");

            try
            {
              OnSessionConnected?.Invoke(newSession);
            }
            catch (Exception ex)
            {
              Logger.Error(ex, "OnSessionConnected callback threw an exception");
            }
          }
          else
          {
            // 并发添加失败，使用已有会话
            await newSession.DisposeAsync();
            if (_sessions.TryGetValue(remoteEp, out var retrySession)) retrySession.EnqueueReceivedData(result.Buffer);
          }
        }
      }
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "UDP server receive loop unexpected error");
    }
  }

  #endregion

  #region 构造函数

  /// <summary>
  ///   创建 UDP 服务器
  /// </summary>
  /// <param name="address">监听地址（通常为 IPAddress.Any）</param>
  /// <param name="port">监听端口</param>
  public SbUdpStreamServer(IPAddress address, int port)
  {
    _localEndPoint = new IPEndPoint(address, port);
  }

  /// <summary>
  ///   创建 UDP 服务器（监听所有地址）
  /// </summary>
  /// <param name="port">监听端口</param>
  public SbUdpStreamServer(int port)
    : this(IPAddress.Any, port)
  {
  }

  /// <summary>
  ///   创建 UDP 服务器
  /// </summary>
  /// <param name="endpoint">监听端点</param>
  public SbUdpStreamServer(IPEndPoint endpoint)
  {
    _localEndPoint = endpoint;
  }

  #endregion

  #region Start / Stop / Dispose

  /// <summary>
  ///   启动监听
  /// </summary>
  public void Start()
  {
    if (_isDisposed) throw new SbModbusException("UDP server stream has been disposed");
    if (_isListening) return;

    try
    {
      _udpClient = new UdpClient(_localEndPoint);
      _isListening = true;

      _receiveCts = new CancellationTokenSource();
      _receiveTask = Task.Factory.StartNew(
        () => RunReceiveLoopAsync(_receiveCts.Token),
        _receiveCts.Token,
        TaskCreationOptions.LongRunning,
        TaskScheduler.Default
      ).Unwrap();

      Logger.Information($"UDP server started on {_localEndPoint}");
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "Failed to start UDP server");
      _udpClient?.Dispose();
      _udpClient = null;
      throw;
    }
  }

  /// <summary>
  ///   停止监听并断开所有会话
  /// </summary>
  public void Stop()
  {
    if (!_isListening) return;

    Logger.Information("UDP server stopping...");

    // 取消接收循环
    CancelAndDispose(ref _receiveCts);
    WaitTask(_receiveTask, "receive");
    _receiveTask = null;

    // 关闭 UDP 客户端
    try
    {
      _udpClient?.Close();
    }
    catch (Exception ex)
    {
      Logger.Log(LogLevel.Debug, $"UDP close warning: {ex.Message}");
    }

    _udpClient?.Dispose();
    _udpClient = null;
    _isListening = false;

    // 断开所有活跃会话
    DisconnectAllSessions();

    Logger.Information("UDP server stopped");
  }

  /// <inheritdoc />
  public void Dispose()
  {
    if (_isDisposed) return;
    _isDisposed = true;

    Stop();

    _receiveCts?.Dispose();
    _receiveCts = null;

    foreach (var kv in _sessions)
    {
      try
      {
        kv.Value.Dispose();
      }
      catch
      {
        /* ignore */
      }
    }

    _sessions.Clear();

    _stationWriters.Clear();

    OnSessionConnected = null;
    OnSessionDisconnected = null;

    GC.SuppressFinalize(this);
  }

  #endregion

  #region 内部 API（供 SbUdpSessionStream 调用）

  /// <summary>
  ///   向指定远程端点发送数据
  /// </summary>
  internal async ValueTask SendAsync(ReadOnlyMemory<byte> data, IPEndPoint target, CancellationToken ct)
  {
    var client = _udpClient;
    if (client == null)
      throw new SbModbusException("UDP server is not running");

    try
    {
#if NET6_0_OR_GREATER
      await client.SendAsync(data, target, ct).ConfigureAwait(false);
#else
      var bytes = data.ToArray();
      await client.SendAsync(bytes, bytes.Length, target).ConfigureAwait(false);
#endif
    }
    catch (SocketException ex)
    {
      Logger.Log(LogLevel.Error, $"UDP send error to {target}: {ex.SocketErrorCode} - {ex.Message}");
      throw new SbModbusException($"UDP send to {target} failed", ex);
    }
  }

  /// <summary>
  ///   从会话列表中移除（由 SbUdpSessionStream 在断开时调用）
  /// </summary>
  internal void RemoveSession(IPEndPoint endpoint, SbUdpSessionStream session)
  {
    session.AutoReceive = false;

    if (_sessions.TryRemove(endpoint, out _))
      try
      {
        OnSessionDisconnected?.Invoke(session);
      }
      catch (Exception ex)
      {
        Logger.Error(ex, "OnSessionDisconnected callback threw an exception");
      }
  }

  private void OnSessionDataReceived(IPEndPoint ep, SbUdpSessionStream session, ReadOnlyMemory<byte> data)
  {
    using var lb = session.GetLockedBuffer();
    if (lb.WrittenSpan.Length == 0) return;
    var remaining = lb.WrittenSpan;
    var len = remaining.Length;
    while (_frameParser(session, ref remaining, out var message))
      if (_stationWriters.TryGetValue(message.UnitId, out var writer))
        writer.TryWrite(message);

    var consumed = len - remaining.Length;
    if (consumed > 0)
      lb.RemoveFirst(consumed);
  }

  #endregion

  #region 辅助方法

  private void DisconnectAllSessions()
  {
    foreach (var kv in _sessions)
    {
      try
      {
        kv.Value.Disconnect();
      }
      catch (Exception ex)
      {
        Logger.Log(LogLevel.Debug, $"Session disconnect warning: {ex.Message}");
      }
    }
  }

  private static void CancelAndDispose(ref CancellationTokenSource? cts)
  {
    var source = Interlocked.Exchange(ref cts, null);
    if (source == null) return;
    try
    {
      source.Cancel();
    }
    catch (ObjectDisposedException)
    {
    }

    source.Dispose();
  }

  private static void WaitTask(Task? task, string name)
  {
    if (task == null || task.IsCompleted) return;
    try
    {
#pragma warning disable VSTHRD002
      task.Wait(TaskWaitTimeout);
#pragma warning restore VSTHRD002
    }
    catch (AggregateException ae)
    {
      ae.Handle(ex => ex is OperationCanceledException or ObjectDisposedException);
    }
    catch (OperationCanceledException)
    {
    }
  }

  private static async Task WaitTaskAsync(Task? task, string name)
  {
    if (task == null || task.IsCompleted) return;
    try
    {
#if NET6_0_OR_GREATER
      await task.WaitAsync(TaskWaitTimeout).ConfigureAwait(false);
#else
      using var timeoutCts = new CancellationTokenSource(TaskWaitTimeout);
      await Task.WhenAny(task, Task.Delay(Timeout.Infinite, timeoutCts.Token)).ConfigureAwait(false);
      timeoutCts.Cancel();
      try
      {
        await task.ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
      }
      catch (ObjectDisposedException)
      {
      }
#endif
    }
    catch (TimeoutException)
    {
    }
    catch (OperationCanceledException)
    {
    }
    catch (ObjectDisposedException)
    {
    }
  }

  #endregion
}

/// <summary>
///   UDP 会话流 — 每个远程端点对应一个会话，实现 <see cref="IModbusStream" />
/// </summary>
public class SbUdpSessionStream : ModbusStream, IModbusStream
{
  private readonly SbUdpStreamServer _streamServer;
  private readonly string _transportInfo;

  private volatile bool _isDisposedLocal;

  internal SbUdpSessionStream(SbUdpStreamServer streamServer, IPEndPoint remoteEndPoint)
  {
    _streamServer = streamServer;
    RemoteEndPoint = remoteEndPoint;
    _transportInfo = $"UDP-Session:{remoteEndPoint.Address}:{remoteEndPoint.Port}";
  }

  /// <summary>
  ///   会话的远程端点
  /// </summary>
  public IPEndPoint RemoteEndPoint { get; }

  /// <inheritdoc />
  public override string GetTransportInfo()
  {
    return _transportInfo;
  }

  /// <inheritdoc />
  protected override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    if (_isDisposedLocal)
      throw new SbModbusException("UDP session is disconnected");

    await _streamServer.SendAsync(buffer, RemoteEndPoint, ct).ConfigureAwait(false);
  }

  /// <summary>
  ///   将接收到的数据写入缓冲区（由 SbUdpStreamServer 接收循环调用）
  /// </summary>
  internal void EnqueueReceivedData(byte[] buffer)
  {
    WriteBuffer(buffer.AsSpan());
  }

  #region 属性

  /// <inheritdoc />
  public override bool IsConnected => !_isDisposedLocal;

  /// <inheritdoc />
  public override int ReadTimeout { get; set; } = 2000;

  /// <inheritdoc />
  public override int WriteTimeout { get; set; } = 2000;

  #endregion

  #region Connect / Disconnect / Dispose

  /// <summary>
  ///   对于已建立的 UDP 会话，Connect 为 no-op
  /// </summary>
  /// <inheritdoc />
  public override bool Connect()
  {
    return IsConnected;
  }

  /// <inheritdoc />
  public override Task<bool> ConnectAsync(CancellationToken ct = default)
  {
    return Task.FromResult(IsConnected);
  }

  /// <inheritdoc />
  public override bool Disconnect()
  {
    if (_isDisposedLocal) return true;

    Logger.Information($"UDP session {RemoteEndPoint} disconnecting");

    _isDisposedLocal = true;
    _streamServer.RemoveSession(RemoteEndPoint, this);
    ConnectStateChanged(false);
    return true;
  }

  /// <inheritdoc />
  public override Task<bool> DisconnectAsync(CancellationToken ct = default)
  {
    return Task.FromResult(Disconnect());
  }

  /// <inheritdoc />
  public override void Dispose()
  {
    if (_isDisposedLocal) return;
    _isDisposedLocal = true;

    Disconnect();

    base.Dispose();
    StreamLock.Dispose();
    GC.SuppressFinalize(this);
  }

  /// <inheritdoc />
  public override async ValueTask DisposeAsync()
  {
    if (_isDisposedLocal) return;
    _isDisposedLocal = true;

    await DisconnectAsync().ConfigureAwait(false);

    await base.DisposeAsync().ConfigureAwait(false);
    StreamLock.Dispose();
    GC.SuppressFinalize(this);
  }

  #endregion
}
