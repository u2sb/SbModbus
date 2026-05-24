using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace SbModbus.Tool.Services.DataTransferServices;

public class UdpServerDtStream : IDtStream
{
  private readonly IPEndPoint _endPoint;
  private UdpClient? _udpClient;
  private CancellationTokenSource? _receiveCts;
  private Task? _receiveTask;
  private volatile bool _isDisposed;
  private volatile bool _isListening;

  private readonly List<EndPoint> _sessions = [];
  private readonly object _lock = new();

  public UdpServerDtStream(IPAddress address, int port)
    => _endPoint = new IPEndPoint(address, port);

  public UdpServerDtStream(string address, int port)
    => _endPoint = new IPEndPoint(IPAddress.Parse(address), port);

  public UdpServerDtStream(DnsEndPoint endpoint)
    => _endPoint = new IPEndPoint(IPAddress.Any, endpoint.Port);

  public UdpServerDtStream(IPEndPoint endpoint)
    => _endPoint = endpoint;

  public Action<IEnumerable<string>>? OnSessionStateChanged { get; set; }
  public ReadOnlySpanAction<byte, IDtStream>? OnDataWrite { get; set; }
  public ReadOnlySpanAction<byte, IDtStream>? OnDataReceived { get; set; }
  public Action<bool>? OnConnectStateChanged { get; set; }

  public bool IsConnected => _isListening;
  public int SelectedSessionIndex { get; set; } = -1;

  public bool Connect()
  {
    if (_isDisposed || _isListening) return _isListening;

    try
    {
      _udpClient = new UdpClient(_endPoint);
      _isListening = true;
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
    _isListening = false;

    lock (_lock) { _sessions.Clear(); }

    OnSessionStateChanged?.Invoke([]);
    OnConnectStateChanged?.Invoke(false);
  }

  public void Write(ReadOnlySpan<byte> data)
  {
    if (_udpClient == null) return;

    EndPoint? target = null;
    lock (_lock)
    {
      if (SelectedSessionIndex >= 0 && SelectedSessionIndex < _sessions.Count)
        target = _sessions[SelectedSessionIndex];
    }

    if (target == null || target is not IPEndPoint ipTarget) return;

    try
    {
      var bytes = data.ToArray();
      _udpClient.Send(bytes, bytes.Length, ipTarget);
      OnDataWrite?.Invoke(data, this);
    }
    catch (SocketException) { }
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
    _udpClient?.Dispose();
    _udpClient = null;
    _receiveCts?.Dispose();
    _receiveCts = null;
    OnSessionStateChanged = null;
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

        lock (_lock)
        {
          if (!_sessions.Contains(result.RemoteEndPoint))
          {
            _sessions.Add(result.RemoteEndPoint);
            NotifySessionStateChanged();
          }
        }

        OnDataReceived?.Invoke(result.Buffer, this);
      }
    }
    catch { /* receive loop ended */ }
  }

  private void NotifySessionStateChanged()
  {
    lock (_lock)
    {
      OnSessionStateChanged?.Invoke(
        _sessions.OfType<IPEndPoint>().Select(s => $"{s.Address}:{s.Port}")
      );
    }
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
