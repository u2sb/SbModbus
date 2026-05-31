using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SbModbus.Models;

namespace SbModbus.TcpStream;

/// <summary>
///   TCP 服务器流 — 基于 .NET 原生 Socket，管理监听和会话生命周期
/// </summary>
public class SbTcpStreamServer : IModbusStreamServer
{
  private static readonly LingerOption NoLinger = new(true, 0);
  private static readonly TimeSpan TaskWaitTimeout = TimeSpan.FromSeconds(3);
  private readonly int _backlog;
  private readonly IPEndPoint _endPoint;

  private readonly ConcurrentDictionary<Guid, SbTcpSessionStream> _sessions = new();

  private readonly ConcurrentDictionary<byte, ChannelWriter<ModbusFrameMessage>> _stationWriters = new();
  private CancellationTokenSource? _acceptCts;
  private Task? _acceptTask;

  private TryParseFrameDelegate _frameParser = Services.ModbusServer.FrameParser.TryParseTcp;

  private volatile bool _isDisposed;
  private volatile bool _isListening;

  private Socket? _listenSocket;

  /// <summary>
  ///   实际监听的本地端点（含端口号，port=0 自动分配时有用）
  /// </summary>
  public IPEndPoint? LocalEndPoint => _listenSocket?.LocalEndPoint as IPEndPoint;

  /// <summary>
  ///   当前活跃会话数
  /// </summary>
  public int SessionCount => _sessions.Count;

  /// <summary>
  ///   服务器是否正在监听
  /// </summary>
  public bool IsListening => _isListening && _listenSocket?.IsBound == true;

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
  ///   新会话连接时触发。调用方应在此事件中为每个会话创建 <see cref="Services.ModbusServer.BaseModbusServer" />
  /// </summary>
  public event Action<IModbusStream>? OnSessionConnected;

  /// <summary>
  ///   会话断开时触发
  /// </summary>
  public event Action<IModbusStream>? OnSessionDisconnected;

  #region 构造函数

  /// <summary>
  ///   创建 TCP 服务器
  /// </summary>
  /// <param name="address">监听地址</param>
  /// <param name="port">监听端口</param>
  /// <param name="backlog">等待队列最大长度，默认 100</param>
  public SbTcpStreamServer(IPAddress address, int port, int backlog = 100)
  {
    _endPoint = new IPEndPoint(address, port);
    _backlog = backlog;
  }

  /// <summary>
  ///   创建 TCP 服务器（监听所有地址）
  /// </summary>
  /// <param name="port">监听端口</param>
  /// <param name="backlog">等待队列最大长度，默认 100</param>
  public SbTcpStreamServer(int port, int backlog = 100)
    : this(IPAddress.Any, port, backlog)
  {
  }

  /// <summary>
  ///   创建 TCP 服务器
  /// </summary>
  /// <param name="endpoint">监听端点</param>
  /// <param name="backlog">等待队列最大长度，默认 100</param>
  public SbTcpStreamServer(IPEndPoint endpoint, int backlog = 100)
  {
    _endPoint = endpoint;
    _backlog = backlog;
  }

  #endregion

  #region Start / Stop / Dispose

  /// <summary>
  ///   启动监听
  /// </summary>
  public void Start()
  {
    if (_isDisposed) throw new SbModbusException("TCP server stream has been disposed");
    if (_isListening) return;

    try
    {
      _listenSocket = new Socket(_endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
      _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
      _listenSocket.Bind(_endPoint);
      _listenSocket.Listen(_backlog);

      _isListening = true;

      _acceptCts = new CancellationTokenSource();
      _acceptTask = Task.Factory.StartNew(
        () => RunAcceptLoopAsync(_acceptCts.Token),
        _acceptCts.Token,
        TaskCreationOptions.LongRunning,
        TaskScheduler.Default
      ).Unwrap();

      Logger.Information($"TCP server started on {_endPoint}");
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "Failed to start TCP server");
      _listenSocket?.Dispose();
      _listenSocket = null;
      throw;
    }
  }

  /// <summary>
  ///   停止监听并断开所有会话
  /// </summary>
  public void Stop()
  {
    if (!_isListening) return;

    Logger.Information("TCP server stopping...");

    // 取消接受循环
    CancelAndDispose(ref _acceptCts);
    WaitTask(_acceptTask, "accept");
    _acceptTask = null;

    // 关闭监听 socket
    try
    {
      _listenSocket?.Close();
    }
    catch (Exception ex)
    {
      Logger.Log(LogLevel.Debug, $"Listen socket close warning: {ex.Message}");
    }

    _listenSocket?.Dispose();
    _listenSocket = null;
    _isListening = false;

    // 断开所有活跃会话
    DisconnectAllSessions();

    Logger.Information("TCP server stopped");
  }

  /// <inheritdoc />
  public void Dispose()
  {
    if (_isDisposed) return;
    _isDisposed = true;

    Stop();

    _acceptCts?.Dispose();
    _acceptCts = null;

    // 释放所有还存活的会话
    foreach (var kv in _sessions)
      try
      {
        kv.Value.Dispose();
      }
      catch
      {
        /* ignore */
      }

    _sessions.Clear();

    _stationWriters.Clear();

    OnSessionConnected = null;
    OnSessionDisconnected = null;

    GC.SuppressFinalize(this);
  }

  #endregion

  #region 内部实现

  /// <summary>
  ///   接受连接循环
  /// </summary>
  private async Task RunAcceptLoopAsync(CancellationToken ct)
  {
    try
    {
      while (!ct.IsCancellationRequested)
      {
        var listenSocket = _listenSocket;
        if (listenSocket == null) break;

        Socket accepted;
        try
        {
#if NET6_0_OR_GREATER
          accepted = await listenSocket.AcceptAsync(ct).ConfigureAwait(false);
#else
          accepted = await listenSocket.AcceptAsync().ConfigureAwait(false);
#endif
        }
        catch (OperationCanceledException)
        {
          break;
        }
        catch (ObjectDisposedException)
        {
          break;
        }
        catch (SocketException ex)
        {
          Logger.Log(LogLevel.Error, $"Accept error: {ex.SocketErrorCode} - {ex.Message}");
          break;
        }

        // 配置已接受的 socket
        accepted.NoDelay = true;
        accepted.LingerState = NoLinger;

        OnSocketAccepted(accepted);
      }
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "Accept loop unexpected error");
    }
  }

  /// <summary>
  ///   Socket 被接受后的处理
  /// </summary>
  private void OnSocketAccepted(Socket socket)
  {
    var remoteEp = socket.RemoteEndPoint?.ToString() ?? "unknown";

    Logger.Information($"TCP session accepted from {remoteEp}");

    var session = new SbTcpSessionStream(socket, remoteEp, this);
    if (!_sessions.TryAdd(session.SessionId, session))
    {
      Logger.Warning($"Failed to add session {session.SessionId} to collection");
      session.Dispose();
      return;
    }

    session.Start();

    // 设置 AutoReceive：数据到达时触发 OnDataReceived，再解析帧写入 Channel
    session.AutoReceive = true;
    var sessionId = session.SessionId;
    session.OnDataReceived += (_, data) => OnSessionDataReceived(sessionId, session, data);

    try
    {
      OnSessionConnected?.Invoke(session);
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "OnSessionConnected callback threw an exception");
    }
  }

  /// <summary>
  ///   从会话列表中移除（由 SbTcpSessionStream 在断开时调用）
  /// </summary>
  internal void RemoveSession(Guid sessionId, SbTcpSessionStream session)
  {
    session.AutoReceive = false;

    if (_sessions.TryRemove(sessionId, out _))
      try
      {
        OnSessionDisconnected?.Invoke(session);
      }
      catch (Exception ex)
      {
        Logger.Error(ex, "OnSessionDisconnected callback threw an exception");
      }
  }

  /// <summary>
  ///   会话数据到达 → 直接在 RingBuffer 上解析 TCP 帧，写入 Channel
  /// </summary>
  private void OnSessionDataReceived(Guid sessionId, SbTcpSessionStream session, ReadOnlyMemory<byte> data)
  {
    using var lb = session.GetLockedBuffer();
    if (lb.Buffer.Count == 0) return;
    var remaining = lb.Buffer.WrittenSpan;
    var len = remaining.Length;
    while (_frameParser(session, ref remaining, out var message))
      if (_stationWriters.TryGetValue(message.UnitId, out var writer))
        writer.TryWrite(message);

    var consumed = len - remaining.Length;
    if (consumed > 0)
      lb.Buffer.RemoveFirst(consumed);
  }

  /// <summary>
  ///   断开所有活跃会话
  /// </summary>
  private void DisconnectAllSessions()
  {
    foreach (var kv in _sessions)
      try
      {
        kv.Value.Disconnect();
      }
      catch (Exception ex)
      {
        Logger.Log(LogLevel.Debug, $"Session disconnect warning: {ex.Message}");
      }
  }

  #endregion

  #region 静态工具方法

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
      task.Wait(TimeSpan.FromSeconds(3));
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
///   TCP 会话流 — 每个客户端连接对应一个会话，实现 <see cref="IModbusStream" />
/// </summary>
public class SbTcpSessionStream : ModbusStream, IModbusStream
{
  private const int ReceiveBufferSize = 4096;
  private static readonly TimeSpan TaskWaitTimeout = TimeSpan.FromSeconds(3);
  private readonly Socket _socket;
  private readonly SbTcpStreamServer _streamServer;
  private volatile bool _isConnected;

  private volatile bool _isDisposedLocal;

  private CancellationTokenSource? _receiveCts;
  private Task? _receiveTask;

  internal SbTcpSessionStream(Socket socket, string remoteEndPoint, SbTcpStreamServer streamServer)
  {
    _socket = socket;
    _streamServer = streamServer;
    RemoteEndPoint = $"TCP-Session:{remoteEndPoint}";
    _isConnected = true;
  }

  /// <summary>
  ///   会话唯一标识
  /// </summary>
  public Guid SessionId { get; } = Guid.NewGuid();

  /// <summary>
  ///   获取会话的远程端点信息
  /// </summary>
  public string RemoteEndPoint { get; }

  /// <inheritdoc />
  public override string GetTransportInfo()
  {
    return RemoteEndPoint;
  }

  /// <inheritdoc />
  protected override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    if (!_isConnected)
      throw new SbModbusException("Session not connected");

    var totalSent = 0;
    while (totalSent < buffer.Length)
    {
      ct.ThrowIfCancellationRequested();

      int sent;
      try
      {
#if NET6_0_OR_GREATER
        sent = await _socket.SendAsync(buffer.Slice(totalSent), SocketFlags.None, ct).ConfigureAwait(false);
#else
        var arr = buffer.ToArray();
        sent = await _socket.SendAsync(new ArraySegment<byte>(arr, totalSent, arr.Length - totalSent), SocketFlags.None)
          .ConfigureAwait(false);
#endif
      }
      catch (SocketException ex)
      {
        HandleSocketError(ex);
        throw new SbModbusException("Send failed - session may be closed", ex);
      }

      if (sent == 0)
        throw new SbModbusException("Session closed during send");

      totalSent += sent;
    }
  }

  #region 属性

  /// <inheritdoc />
  public override bool IsConnected => _isConnected && _socket.Connected;

  /// <inheritdoc />
  public override int ReadTimeout { get; set; } = 2000;

  /// <inheritdoc />
  public override int WriteTimeout { get; set; } = 2000;

  #endregion

  #region Connect / Disconnect / Dispose

  /// <summary>
  ///   对于已接受的会话，Connect 为 no-op（已在连接状态）
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
    Logger.Information($"TCP session {SessionId} disconnecting");

    // 取消接收循环
    CancelAndDispose(ref _receiveCts);
    WaitTask(_receiveTask, "receive");
    _receiveTask = null;

    CloseSocket();
    _isConnected = false;

    _streamServer.RemoveSession(SessionId, this);
    ConnectStateChanged(false);
    return true;
  }

  /// <inheritdoc />
  public override async Task<bool> DisconnectAsync(CancellationToken ct = default)
  {
    Logger.Information($"TCP session {SessionId} disconnecting");

    CancelAndDispose(ref _receiveCts);
    await WaitTaskAsync(_receiveTask, "receive").ConfigureAwait(false);
    _receiveTask = null;

    CloseSocket();
    _isConnected = false;

    _streamServer.RemoveSession(SessionId, this);
    ConnectStateChanged(false);
    return true;
  }

  /// <inheritdoc />
  public override void Dispose()
  {
    if (_isDisposedLocal) return;
    _isDisposedLocal = true;

    Disconnect();

    try
    {
      _socket.Dispose();
    }
    catch
    {
      /* ignore */
    }

    _receiveCts?.Dispose();
    _receiveCts = null;

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

    try
    {
      _socket.Dispose();
    }
    catch
    {
      /* ignore */
    }

    _receiveCts?.Dispose();
    _receiveCts = null;

    await base.DisposeAsync().ConfigureAwait(false);
    StreamLock.Dispose();
    GC.SuppressFinalize(this);
  }

  #endregion

  #region 内部实现

  /// <summary>
  ///   启动接收循环（由服务器在接受连接后调用）
  /// </summary>
  internal void Start()
  {
    _receiveCts = new CancellationTokenSource();
    _receiveTask = Task.Factory.StartNew(
      () => RunReceiveLoopAsync(_receiveCts.Token),
      _receiveCts.Token,
      TaskCreationOptions.LongRunning,
      TaskScheduler.Default
    ).Unwrap();

    ConnectStateChanged(true);
  }

  /// <summary>
  ///   后台接收循环
  /// </summary>
  private async Task RunReceiveLoopAsync(CancellationToken ct)
  {
    var buffer = new byte[ReceiveBufferSize];

    try
    {
      while (!ct.IsCancellationRequested)
      {
        if (!_isConnected) break;

        int received;
        try
        {
#if NET6_0_OR_GREATER
          received = await _socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, ct).ConfigureAwait(false);
#else
          received = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), SocketFlags.None).ConfigureAwait(false);
#endif
        }
        catch (SocketException)
        {
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

        if (received == 0) break;

        WriteBuffer(buffer.AsSpan(0, received));
      }
    }
    catch (Exception ex)
    {
      Logger.Error(ex, $"TCP session {SessionId} receive loop unexpected error");
    }
    finally
    {
      // 被动断开时清理
      if (!_isDisposedLocal) HandleDisconnected();
    }
  }

  /// <summary>
  ///   处理被动断开
  /// </summary>
  private void HandleDisconnected()
  {
    var wasConnected = _isConnected;
    _isConnected = false;
    CloseSocket();

    _streamServer.RemoveSession(SessionId, this);

    if (wasConnected) ConnectStateChanged(false);
  }

  /// <summary>
  ///   安全关闭 socket
  /// </summary>
  private void CloseSocket()
  {
    try
    {
      if (_socket.Connected) _socket.Shutdown(SocketShutdown.Both);
    }
    catch (Exception ex)
    {
      Logger.Log(LogLevel.Debug, $"Session socket shutdown warning: {ex.Message}");
    }

    try
    {
      _socket.Close();
    }
    catch (Exception ex)
    {
      Logger.Log(LogLevel.Debug, $"Session socket close warning: {ex.Message}");
    }
  }

  /// <summary>
  ///   Socket 错误处理
  /// </summary>
  private void HandleSocketError(SocketException ex)
  {
    Logger.Log(LogLevel.Error, $"TCP session {SessionId} socket error: {ex.SocketErrorCode} - {ex.Message}");
  }

  #endregion

  #region 静态工具方法

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
#pragma warning disable VSTHRD002 // 安全的同步等待：任务已收到取消信号，会快速完成
      task.Wait(TimeSpan.FromSeconds(3));
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
