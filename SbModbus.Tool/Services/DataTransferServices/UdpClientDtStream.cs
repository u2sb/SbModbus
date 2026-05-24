using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace SbModbus.Tool.Services.DataTransferServices;

public class UdpClientDtStream : IDtStream
{
  private readonly EndPoint _endPoint;
  private UdpClient? _udpClient;
  private IPEndPoint? _sendEndpoint;
  private CancellationTokenSource? _receiveCts;
  private Task? _receiveTask;
  private volatile bool _isDisposed;
  private volatile bool _isConnected;

  private const int ReceiveBufferSize = 4096;

  public UdpClientDtStream(IPAddress address, int port)
    => _endPoint = new IPEndPoint(address, port);

  public UdpClientDtStream(string address, int port)
    => _endPoint = new DnsEndPoint(address, port);

  public UdpClientDtStream(DnsEndPoint endpoint)
    => _endPoint = endpoint;

  public UdpClientDtStream(IPEndPoint endpoint)
    => _endPoint = endpoint;

  public ReadOnlySpanAction<byte, IDtStream>? OnDataWrite { get; set; }
  public ReadOnlySpanAction<byte, IDtStream>? OnDataReceived { get; set; }
  public Action<bool>? OnConnectStateChanged { get; set; }

  public bool IsConnected => _isConnected;

  public bool Connect()
  {
    if (_isDisposed || _isConnected) return _isConnected;

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
      }

      _isConnected = true;
      StartReceiveLoop();
      OnConnectStateChanged?.Invoke(true);
      return true;
    }
    catch
    {
      _udpClient?.Dispose();
      _udpClient = null;
      return false;
    }
  }

  public void Disconnect()
  {
    CancelAndDispose(ref _receiveCts);
    WaitTask(_receiveTask);
    _receiveTask = null;

    try { _udpClient?.Close(); } catch { }
    _udpClient = null;
    _isConnected = false;
    OnConnectStateChanged?.Invoke(false);
  }

  public void Write(ReadOnlySpan<byte> data)
  {
    if (_udpClient == null) return;

    try
    {
      var bytes = data.ToArray();
      if (_sendEndpoint != null)
        _udpClient.Send(bytes, bytes.Length, _sendEndpoint);
      else
        _udpClient.Send(bytes, bytes.Length);

      OnDataWrite?.Invoke(data, this);
    }
    catch (SocketException) { }
  }

  public ValueTask WriteAsync(ReadOnlyMemory<byte> data)
  {
    Write(data.Span);
    return ValueTask.CompletedTask;
  }

  public void SetEndpoint(IPEndPoint endpoint)
  {
    _sendEndpoint = endpoint;
  }

  public void Dispose()
  {
    if (_isDisposed) return;
    _isDisposed = true;
    Disconnect();
    _udpClient?.Dispose();
    _udpClient = null;
    _receiveCts?.Dispose();
    _receiveCts = null;
    OnDataWrite = null;
    OnDataReceived = null;
    OnConnectStateChanged = null;
    GC.SuppressFinalize(this);
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
    try
    {
      while (!ct.IsCancellationRequested)
      {
        var client = _udpClient;
        if (client == null) break;

        UdpReceiveResult result;
        try { result = await client.ReceiveAsync(ct); }
        catch (SocketException) { break; }
        catch (OperationCanceledException) { break; }
        catch (ObjectDisposedException) { break; }

        OnDataReceived?.Invoke(result.Buffer, this);
      }
    }
    catch { /* receive loop ended */ }
  }

  private static void CancelAndDispose(ref CancellationTokenSource? cts)
  {
    var s = Interlocked.Exchange(ref cts, null);
    if (s == null) return;
    try { s.Cancel(); } catch (ObjectDisposedException) { }
    s.Dispose();
  }

  private static void WaitTask(Task? task)
  {
    if (task == null || task.IsCompleted) return;
    try { task.Wait(TimeSpan.FromSeconds(2)); }
    catch (AggregateException ae) { ae.Handle(_ => true); }
    catch (OperationCanceledException) { }
  }
}
