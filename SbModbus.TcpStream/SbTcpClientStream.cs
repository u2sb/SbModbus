using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SbModbus.Protocol;
using SbModbus.Transport;
using SbModbus.Utils;

namespace SbModbus.TcpStream;

/// <summary>
///   TCP 客户端流 — 基于 .NET 原生 Socket，支持自动重连
/// </summary>
public class SbTcpClientStream : ModbusStream, IModbusStream
{
  private const int InitialBackoffMs = 100;
  private const int MaxBackoffMs = 30000;

  private const int ReceiveBufferSize = 4096;
  private const int SocketBufferSize = 65536;
  private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
  private static readonly TimeSpan TaskWaitTimeout = TimeSpan.FromSeconds(3);
  private static readonly LingerOption NoLinger = new(false, 0);
  private readonly EndPoint _endPoint;
  private readonly string _transportInfo;

  // 重连退避
  private int _backoffMs = InitialBackoffMs;

  // 缓存 DNS 解析结果，避免每次重连都解析
  private IPEndPoint? _cachedResolvedEndPoint;
  private volatile bool _isConnected;
  private volatile bool _isDisposedLocal;

  // 用户是否主动断开（区分主动/被动断开，决定是否触发自动重连）
  private volatile bool _isUserDisconnect;
  private CancellationTokenSource? _receiveCts;
  private Task? _receiveTask;
  private CancellationTokenSource? _reconnectCts;
  private Task? _reconnectTask;

  private Socket? _socket;

  /// <inheritdoc />
  public override string GetTransportInfo()
  {
    return _transportInfo;
  }

  /// <inheritdoc />
  protected override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    var socket = _socket;
    if (socket == null || !_isConnected)
      throw new SbModbusException("Not connected");

    var totalSent = 0;
    while (totalSent < buffer.Length)
    {
      ct.ThrowIfCancellationRequested();

      int sent;
      try
      {
#if NET6_0_OR_GREATER
        sent = await socket.SendAsync(buffer.Slice(totalSent), SocketFlags.None, ct).ConfigureAwait(false);
#else
        var arr = buffer.ToArray();
        sent = await socket.SendAsync(new ArraySegment<byte>(arr, totalSent, arr.Length - totalSent), SocketFlags.None)
          .ConfigureAwait(false);
#endif
      }
      catch (SocketException ex)
      {
        HandleSocketError(ex);
        throw new SbModbusException("Send failed - connection may be lost", ex);
      }

      if (sent == 0)
        throw new SbModbusException("Connection closed during send");

      totalSent += sent;
    }
  }

  #region 构造函数

  /// <inheritdoc />
  public SbTcpClientStream(IPAddress address, int port)
  {
    _transportInfo = $"TCP:{address}:{port}";
    _endPoint = new IPEndPoint(address, port);
  }

  /// <inheritdoc />
  public SbTcpClientStream(string address, int port)
  {
    _transportInfo = $"TCP:{address}:{port}";
    _endPoint = new DnsEndPoint(address, port);
  }

  /// <inheritdoc />
  public SbTcpClientStream(DnsEndPoint endpoint)
  {
    _transportInfo = $"TCP:{endpoint.Host}:{endpoint.Port}";
    _endPoint = endpoint;
  }

  /// <inheritdoc />
  public SbTcpClientStream(IPEndPoint endpoint)
  {
    _transportInfo = $"TCP:{endpoint.Address}:{endpoint.Port}";
    _endPoint = endpoint;
  }

  #endregion

  #region 属性

  /// <inheritdoc />
  public override bool IsConnected
  {
    get
    {
      var s = _socket;
      return s != null && _isConnected && s.Connected;
    }
  }

  /// <inheritdoc />
  public override int ReadTimeout { get; set; } = 2000;

  /// <inheritdoc />
  public override int WriteTimeout { get; set; } = 2000;

  #endregion

  #region Connect / Disconnect / Dispose

  /// <inheritdoc />
  public override bool Connect()
  {
    if (_isDisposedLocal) return false;
    if (IsConnected) return true;

    try
    {
      _isUserDisconnect = false;

      // 同步等待连接完成
      using var cts = new CancellationTokenSource(ConnectTimeout);
      var connectTask = TryConnectOnceAsync(cts.Token);
#pragma warning disable VSTHRD002 // 此方法本身是同步的，需要等待连接完成
      connectTask.Wait(cts.Token);
#pragma warning restore VSTHRD002
      return IsConnected;
    }
    catch (OperationCanceledException)
    {
      Logger.Error("TCP connection timed out");
      CleanupSocket();
      return false;
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "TCP connection failed");
      CleanupSocket();
      return false;
    }
  }

  /// <inheritdoc />
  public override async Task<bool> ConnectAsync(CancellationToken ct = default)
  {
    if (_isDisposedLocal) return false;
    if (IsConnected) return true;

    _isUserDisconnect = false;

    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    linkedCts.CancelAfter(ConnectTimeout);

    try
    {
      await TryConnectOnceAsync(linkedCts.Token).ConfigureAwait(false);
      return IsConnected;
    }
    catch (OperationCanceledException)
    {
      Logger.Error("TCP connection timed out");
      CleanupSocket();
      return false;
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "TCP connection failed");
      CleanupSocket();
      return false;
    }
  }

  /// <inheritdoc />
  public override bool Disconnect()
  {
    Logger.Information("TCP client disconnecting");

    _isUserDisconnect = true;

    // 1. 取消重连
    CancelAndDispose(ref _reconnectCts);

    // 2. 取消接收循环
    CancelAndDispose(ref _receiveCts);

    // 3. 等待后台任务结束
    WaitTask(_reconnectTask, "reconnect");
    WaitTask(_receiveTask, "receive");

    _reconnectTask = null;
    _receiveTask = null;

    // 4. 关闭 socket
    CloseSocket();

    _isConnected = false;
    ConnectStateChanged(false);
    return true;
  }

  /// <inheritdoc />
  public override async Task<bool> DisconnectAsync(CancellationToken ct = default)
  {
    Logger.Information("TCP client disconnecting");

    _isUserDisconnect = true;

    // 1. 取消重连
    CancelAndDispose(ref _reconnectCts);

    // 2. 取消接收循环
    CancelAndDispose(ref _receiveCts);

    // 3. 等待后台任务结束
    await WaitTaskAsync(_reconnectTask, "reconnect").ConfigureAwait(false);
    await WaitTaskAsync(_receiveTask, "receive").ConfigureAwait(false);

    _reconnectTask = null;
    _receiveTask = null;

    // 4. 关闭 socket
    CloseSocket();

    _isConnected = false;
    ConnectStateChanged(false);
    return true;
  }

  /// <inheritdoc />
  public override void Dispose()
  {
    if (_isDisposedLocal) return;
    _isDisposedLocal = true;

    Disconnect();

    _socket?.Dispose();
    _socket = null;

    _reconnectCts?.Dispose();
    _reconnectCts = null;
    _receiveCts?.Dispose();
    _receiveCts = null;

    base.Dispose();
    GC.SuppressFinalize(this);
  }

  /// <inheritdoc />
  public override async ValueTask DisposeAsync()
  {
    if (_isDisposedLocal) return;
    _isDisposedLocal = true;

    await DisconnectAsync().ConfigureAwait(false);

    _socket?.Dispose();
    _socket = null;

    _reconnectCts?.Dispose();
    _reconnectCts = null;
    _receiveCts?.Dispose();
    _receiveCts = null;

    await base.DisposeAsync().ConfigureAwait(false);
    GC.SuppressFinalize(this);
  }

  #endregion

  #region 内部实现

  /// <summary>
  ///   单次连接尝试（解析 DNS + ConnectAsync）
  /// </summary>
  private async Task TryConnectOnceAsync(CancellationToken ct)
  {
    var resolvedEndPoint = await ResolveEndpointAsync(ct).ConfigureAwait(false);

    var socket = new Socket(resolvedEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
    socket.NoDelay = true;
    socket.LingerState = NoLinger;
    socket.ReceiveBufferSize = SocketBufferSize;
    socket.SendBufferSize = SocketBufferSize;
    socket.ReceiveTimeout = ReadTimeout;
    socket.SendTimeout = WriteTimeout;

    try
    {
#if NET6_0_OR_GREATER
      await socket.ConnectAsync(resolvedEndPoint, ct).ConfigureAwait(false);
#else
      await socket.ConnectAsync(resolvedEndPoint).ConfigureAwait(false);
#endif
    }
    catch
    {
      socket.Dispose();
      throw;
    }

    _socket = socket;
    _isConnected = true;

    StartReceiveLoop();
    _backoffMs = InitialBackoffMs;
    ConnectStateChanged(true);
  }

  /// <summary>
  ///   DNS 解析（如需要）
  /// </summary>
  private async Task<IPEndPoint> ResolveEndpointAsync(CancellationToken ct)
  {
    switch (_endPoint)
    {
      case IPEndPoint ipEp:
        _cachedResolvedEndPoint = ipEp;
        return ipEp;
      case DnsEndPoint dnsEp:
      {
        if (_cachedResolvedEndPoint != null) return _cachedResolvedEndPoint;

#if NET6_0_OR_GREATER
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(dnsEp.Host, ct).ConfigureAwait(false);
#else
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(dnsEp.Host).ConfigureAwait(false);
#endif
        var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        if (ipv4 != null) return _cachedResolvedEndPoint = new IPEndPoint(ipv4, dnsEp.Port);
        var ipv6 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6);
        if (ipv6 != null) return _cachedResolvedEndPoint = new IPEndPoint(ipv6, dnsEp.Port);
        throw new SbModbusException($"Could not resolve host: {dnsEp.Host}");
      }
      default:
        throw new SbModbusException($"Unsupported endpoint type: {_endPoint.GetType()}");
    }
  }

  /// <summary>
  ///   启动后台接收循环
  /// </summary>
  private void StartReceiveLoop()
  {
    CancelAndDispose(ref _receiveCts);
    WaitTask(_receiveTask, "receive");
    _receiveTask = null;

    _receiveCts = new CancellationTokenSource();
    _receiveTask = Task.Factory.StartNew(
      () => RunReceiveLoopAsync(_receiveCts.Token),
      _receiveCts.Token,
      TaskCreationOptions.LongRunning,
      TaskScheduler.Default
    ).Unwrap();
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
        var socket = _socket;
        if (socket == null || !_isConnected) break;

        int received;
        try
        {
#if NET6_0_OR_GREATER
          received = await socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, ct).ConfigureAwait(false);
#else
          received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), SocketFlags.None).ConfigureAwait(false);
#endif
        }
        catch (SocketException)
        {
          break; // 连接断开
        }
        catch (OperationCanceledException)
        {
          break; // 外部取消
        }
        catch (ObjectDisposedException)
        {
          break; // socket 已释放
        }

        if (received == 0) break; // 对端正常关闭

        WriteBuffer(buffer.AsSpan(0, received));
      }
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "TCP receive loop unexpected error");
    }
    finally
    {
      // 非用户主动断开 → 触发重连
      if (!_isUserDisconnect && !_isDisposedLocal) HandleDisconnected();
    }
  }

  /// <summary>
  ///   处理被动断开：清理 socket，启动自动重连
  /// </summary>
  private void HandleDisconnected()
  {
    var wasConnected = _isConnected;
    _isConnected = false;
    CloseSocket();

    if (wasConnected) ConnectStateChanged(false);

    if (_isUserDisconnect || _isDisposedLocal) return;

    // 启动重连循环
    StartReconnectLoop();
  }

  /// <summary>
  ///   启动自动重连循环（指数退避）
  /// </summary>
  private void StartReconnectLoop()
  {
    CancelAndDispose(ref _reconnectCts);
    WaitTask(_reconnectTask, "reconnect");
    _reconnectTask = null;

    _reconnectCts = new CancellationTokenSource();
    _reconnectTask = Task.Factory.StartNew(
      () => RunReconnectLoopAsync(_reconnectCts.Token),
      _reconnectCts.Token,
      TaskCreationOptions.LongRunning,
      TaskScheduler.Default
    ).Unwrap();
  }

  /// <summary>
  ///   重连循环（指数退避）
  /// </summary>
  private async Task RunReconnectLoopAsync(CancellationToken ct)
  {
    while (!ct.IsCancellationRequested && !_isDisposedLocal)
    {
      try
      {
        var delay = Math.Min(_backoffMs, MaxBackoffMs);
        Logger.Information($"TCP reconnecting in {delay}ms (backoff={_backoffMs}ms)...");
        await Task.Delay(delay, ct).ConfigureAwait(false);

        await TryConnectOnceAsync(ct).ConfigureAwait(false);
        // 重连成功，退出重连循环（接收循环已在 TryConnectOnceAsync 中启动）
        return;
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (Exception ex)
      {
        Logger.Error(ex, $"TCP reconnect attempt failed, backoff={_backoffMs}ms");
      }

      _backoffMs = Math.Min(_backoffMs * 2, MaxBackoffMs);
    }
  }

  /// <summary>
  ///   关闭 socket（安全清理）
  /// </summary>
  private void CloseSocket()
  {
    var socket = Interlocked.Exchange(ref _socket, null);
    if (socket == null) return;

    try
    {
      if (socket.Connected) socket.Shutdown(SocketShutdown.Both);
    }
    catch (Exception ex)
    {
      Logger.Log(LogLevel.Debug, $"Socket shutdown warning: {ex.Message}");
    }

    try
    {
      socket.Close();
    }
    catch (Exception ex)
    {
      Logger.Log(LogLevel.Debug, $"Socket close warning: {ex.Message}");
    }

    socket.Dispose();
  }

  /// <summary>
  ///   断开时清理 socket（不改变 _socket 引用）
  /// </summary>
  private void CleanupSocket()
  {
    try
    {
      _socket?.Dispose();
    }
    catch (Exception ex)
    {
      Logger.Log(LogLevel.Debug, $"Socket cleanup warning: {ex.Message}");
    }

    _socket = null;
    _isConnected = false;
  }

  /// <summary>
  ///   Socket 错误处理
  /// </summary>
  private void HandleSocketError(SocketException ex)
  {
    Logger.Log(LogLevel.Error, $"TCP socket error: {ex.SocketErrorCode} - {ex.Message}");
    _isConnected = false;
  }

  /// <summary>
  ///   安全取消并释放 CancellationTokenSource
  /// </summary>
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

  /// <summary>
  ///   等待 Task 完成（不抛异常）
  /// </summary>
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

  /// <summary>
  ///   异步等待 Task 完成（不抛异常）
  /// </summary>
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
