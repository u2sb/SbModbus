using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace SbModbus.Tool.Services.DataTransferServices;

public class TcpClientDtStream : IDtStream
{
  private const int ReceiveBufferSize = 4096;
  private readonly EndPoint _endPoint;
  private volatile bool _isConnected;
  private volatile bool _isDisposed;
  private CancellationTokenSource? _receiveCts;
  private Task? _receiveTask;
  private Socket? _socket;

  public TcpClientDtStream(IPAddress address, int port)
  {
    _endPoint = new IPEndPoint(address, port);
  }

  public TcpClientDtStream(string address, int port)
  {
    _endPoint = new DnsEndPoint(address, port);
  }

  public TcpClientDtStream(DnsEndPoint endpoint)
  {
    _endPoint = endpoint;
  }

  public TcpClientDtStream(IPEndPoint endpoint)
  {
    _endPoint = endpoint;
  }

  public ReadOnlySpanAction<byte, IDtStream>? OnDataWrite { get; set; }
  public ReadOnlySpanAction<byte, IDtStream>? OnDataReceived { get; set; }
  public Action<bool>? OnConnectStateChanged { get; set; }

  public bool IsConnected => _isConnected && _socket?.Connected == true;

  public bool Connect()
  {
    if (_isDisposed || IsConnected) return IsConnected;

    try
    {
      var resolved = ResolveEndpoint();
      _socket = new Socket(resolved.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
      _socket.NoDelay = true;
      _socket.LingerState = new LingerOption(true, 0);
      _socket.Connect(resolved);

      _isConnected = true;
      StartReceiveLoop();
      OnConnectStateChanged?.Invoke(true);
      return true;
    }
    catch (Exception)
    {
      _socket?.Dispose();
      _socket = null;
      return false;
    }
  }

  public void Disconnect()
  {
    CancelAndDispose(ref _receiveCts);
    WaitTask(_receiveTask);
    _receiveTask = null;

    CloseSocket();
    _isConnected = false;
    OnConnectStateChanged?.Invoke(false);
  }

  public void Write(ReadOnlySpan<byte> data)
  {
    var socket = _socket;
    if (socket == null || !_isConnected) return;

    try
    {
      socket.Send(data);
      OnDataWrite?.Invoke(data, this);
    }
    catch (SocketException)
    {
      _isConnected = false;
    }
  }

  public ValueTask WriteAsync(ReadOnlyMemory<byte> data)
  {
    Write(data.Span);
    return ValueTask.CompletedTask;
  }

  public void Dispose()
  {
    if (_isDisposed) return;
    _isDisposed = true;
    Disconnect();
    _socket?.Dispose();
    _socket = null;
    _receiveCts?.Dispose();
    _receiveCts = null;
    OnDataWrite = null;
    OnDataReceived = null;
    OnConnectStateChanged = null;
    GC.SuppressFinalize(this);
  }

  private IPEndPoint ResolveEndpoint()
  {
    if (_endPoint is IPEndPoint ipEp) return ipEp;
    if (_endPoint is DnsEndPoint dnsEp)
    {
      var addresses = Dns.GetHostAddresses(dnsEp.Host);
      var ipv4 = Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork);
      if (ipv4 != null) return new IPEndPoint(ipv4, dnsEp.Port);
      var ipv6 = Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetworkV6);
      if (ipv6 != null) return new IPEndPoint(ipv6, dnsEp.Port);
    }

    throw new InvalidOperationException($"Cannot resolve endpoint: {_endPoint}");
  }

  private void StartReceiveLoop()
  {
    CancelAndDispose(ref _receiveCts);
    WaitTask(_receiveTask);
    _receiveTask = null;

    _receiveCts = new CancellationTokenSource();
    _receiveTask = Task.Factory.StartNew(
      () => RunReceiveLoopAsync(_receiveCts.Token),
      _receiveCts.Token,
      TaskCreationOptions.LongRunning,
      TaskScheduler.Default
    ).Unwrap();
  }

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
          received = await socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, ct);
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
        OnDataReceived?.Invoke(buffer.AsSpan(0, received), this);
      }
    }
    finally
    {
      if (!_isDisposed)
      {
        _isConnected = false;
        OnConnectStateChanged?.Invoke(false);
      }
    }
  }

  private void CloseSocket()
  {
    try
    {
      _socket?.Shutdown(SocketShutdown.Both);
    }
    catch
    {
    }

    try
    {
      _socket?.Close();
    }
    catch
    {
    }

    _socket = null;
  }

  private static void CancelAndDispose(ref CancellationTokenSource? cts)
  {
    var s = Interlocked.Exchange(ref cts, null);
    if (s == null) return;
    try
    {
      s.Cancel();
    }
    catch (ObjectDisposedException)
    {
    }

    s.Dispose();
  }

  private static void WaitTask(Task? task)
  {
    if (task == null || task.IsCompleted) return;
    try
    {
      task.Wait(TimeSpan.FromSeconds(2));
    }
    catch (AggregateException ae)
    {
      ae.Handle(_ => true);
    }
    catch (OperationCanceledException)
    {
    }
  }
}
