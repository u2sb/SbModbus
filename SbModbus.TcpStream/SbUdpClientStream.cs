using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SbModbus.Protocol;
using SbModbus.Transport;
using SbModbus.Utils;

namespace SbModbus.TcpStream;

/// <summary>
///   UDP 客户端流 — 基于 .NET 原生 UdpClient
/// </summary>
public class SbUdpClientStream : ModbusStream, IModbusStream
{
  private const int ReceiveBufferSize = 4096;
  private static readonly TimeSpan TaskWaitTimeout = TimeSpan.FromSeconds(3);
  private readonly EndPoint _endPoint;
  private readonly string _transportInfo;
  private volatile bool _isConnected;

  private volatile bool _isDisposedLocal;
  private CancellationTokenSource? _receiveCts;
  private Task? _receiveTask;

  private UdpClient? _udpClient;

  /// <inheritdoc />
  public override string GetTransportInfo()
  {
    return _transportInfo;
  }

  /// <inheritdoc />
  protected override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    var client = _udpClient;
    if (client == null)
      throw new SbModbusException("Not connected");

    try
    {
#if NET6_0_OR_GREATER
      await client.SendAsync(buffer, ct).ConfigureAwait(false);
#else
      var bytes = buffer.ToArray();
      await client.SendAsync(bytes, bytes.Length).ConfigureAwait(false);
#endif
    }
    catch (SocketException ex)
    {
      Logger.Log(LogLevel.Error, $"UDP send error: {ex.SocketErrorCode} - {ex.Message}");
      throw new SbModbusException("UDP send failed", ex);
    }
  }

  #region 构造函数

  /// <inheritdoc />
  public SbUdpClientStream(IPAddress address, int port)
  {
    _transportInfo = $"UDP:{address}:{port}";
    _endPoint = new IPEndPoint(address, port);
  }

  /// <inheritdoc />
  public SbUdpClientStream(string address, int port)
  {
    _transportInfo = $"UDP:{address}:{port}";
    _endPoint = new DnsEndPoint(address, port);
  }

  /// <inheritdoc />
  public SbUdpClientStream(DnsEndPoint endpoint)
  {
    _transportInfo = $"UDP:{endpoint.Host}:{endpoint.Port}";
    _endPoint = endpoint;
  }

  /// <inheritdoc />
  public SbUdpClientStream(IPEndPoint endpoint)
  {
    _transportInfo = $"UDP:{endpoint.Address}:{endpoint.Port}";
    _endPoint = endpoint;
  }

  #endregion

  #region 属性

  /// <inheritdoc />
  public override bool IsConnected => _isConnected && _udpClient != null;

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
      _udpClient = new UdpClient();

      switch (_endPoint)
      {
        case IPEndPoint ipEp:
          _udpClient.Connect(ipEp);
          break;
        case DnsEndPoint dnsEp:
          _udpClient.Connect(dnsEp.Host, dnsEp.Port);
          break;
        default:
          throw new SbModbusException($"Unsupported endpoint type: {_endPoint.GetType()}");
      }

      _isConnected = true;
      StartReceiveLoop();
      ConnectStateChanged(true);
      Logger.Information("UDP client connected");
      return true;
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "UDP connection failed");
      _udpClient?.Dispose();
      _udpClient = null;
      _isConnected = false;
      return false;
    }
  }

  /// <inheritdoc />
  public override Task<bool> ConnectAsync(CancellationToken ct = default)
  {
    return Task.FromResult(Connect());
  }

  /// <inheritdoc />
  public override bool Disconnect()
  {
    Logger.Information("UDP client disconnecting");

    CancelAndDispose(ref _receiveCts);
    WaitTask(_receiveTask, "receive");
    _receiveTask = null;

    try
    {
      _udpClient?.Close();
    }
    catch (Exception ex)
    {
      Logger.Log(LogLevel.Debug, $"UDP client close warning: {ex.Message}");
    }

    _udpClient = null;
    _isConnected = false;
    ConnectStateChanged(false);
    return true;
  }

  /// <inheritdoc />
  public override async Task<bool> DisconnectAsync(CancellationToken ct = default)
  {
    Logger.Information("UDP client disconnecting");

    CancelAndDispose(ref _receiveCts);
    await WaitTaskAsync(_receiveTask, "receive").ConfigureAwait(false);
    _receiveTask = null;

    try
    {
      _udpClient?.Close();
    }
    catch (Exception ex)
    {
      Logger.Log(LogLevel.Debug, $"UDP client close warning: {ex.Message}");
    }

    _udpClient = null;
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

    _udpClient?.Dispose();
    _udpClient = null;

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

    _udpClient?.Dispose();
    _udpClient = null;

    _receiveCts?.Dispose();
    _receiveCts = null;

    await base.DisposeAsync().ConfigureAwait(false);
    StreamLock.Dispose();
    GC.SuppressFinalize(this);
  }

  #endregion

  #region 内部实现

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
          Logger.Log(LogLevel.Warning, $"UDP receive socket error: {ex.SocketErrorCode} - {ex.Message}");
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

        WriteBuffer(result.Buffer);
      }
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "UDP receive loop unexpected error");
    }
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
