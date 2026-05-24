using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace SbModbus.Tool.Services.DataTransferServices;

public class TcpServerDtStream : IDtStream
{
  private readonly IPEndPoint _endPoint;
  private Socket? _listenSocket;
  private CancellationTokenSource? _acceptCts;
  private Task? _acceptTask;

  private readonly ConcurrentDictionary<Guid, DtStreamTcpSession> _sessions = new();
  private readonly List<Guid> _sessionOrder = [];

  private volatile bool _isDisposed;
  private volatile bool _isListening;

  private readonly object _lock = new();

  public TcpServerDtStream(IPAddress address, int port)
    => _endPoint = new IPEndPoint(address, port);

  public TcpServerDtStream(string address, int port)
    => _endPoint = new IPEndPoint(IPAddress.Parse(address), port);

  public TcpServerDtStream(DnsEndPoint endpoint)
    => _endPoint = new IPEndPoint(Dns.GetHostAddresses(endpoint.Host).First(a => a.AddressFamily == AddressFamily.InterNetwork), endpoint.Port);

  public TcpServerDtStream(IPEndPoint endpoint)
    => _endPoint = endpoint;

  public int SessionIndex { get; set; } = -1;

  public Action<IEnumerable<string>>? OnSessionStateChanged { get; set; }
  public ReadOnlySpanAction<byte, IDtStream>? OnDataWrite { get; set; }
  public ReadOnlySpanAction<byte, IDtStream>? OnDataReceived { get; set; }
  public Action<bool>? OnConnectStateChanged { get; set; }

  public bool IsConnected => _isListening;

  public bool Connect()
  {
    if (_isDisposed || _isListening) return _isListening;

    try
    {
      _listenSocket = new Socket(_endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
      _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
      _listenSocket.Bind(_endPoint);
      _listenSocket.Listen(100);

      _isListening = true;

      _acceptCts = new CancellationTokenSource();
      _acceptTask = Task.Factory.StartNew(
        () => RunAcceptLoopAsync(_acceptCts.Token),
        _acceptCts.Token,
        TaskCreationOptions.LongRunning,
        TaskScheduler.Default
      ).Unwrap();

      OnConnectStateChanged?.Invoke(true);
      return true;
    }
    catch
    {
      _listenSocket?.Dispose();
      _listenSocket = null;
      return false;
    }
  }

  public void Disconnect()
  {
    CancelAndDispose(ref _acceptCts);
    WaitTask(_acceptTask);
    _acceptTask = null;

    try { _listenSocket?.Close(); } catch { }
    _listenSocket?.Dispose();
    _listenSocket = null;
    _isListening = false;

    lock (_lock)
    {
      foreach (var (_, session) in _sessions)
      {
        try { session.Dispose(); } catch { }
      }
      _sessions.Clear();
      _sessionOrder.Clear();
    }

    OnSessionStateChanged?.Invoke([]);
    OnConnectStateChanged?.Invoke(false);
  }

  public void Write(ReadOnlySpan<byte> data)
  {
    DtStreamTcpSession? target = null;
    lock (_lock)
    {
      if (SessionIndex >= 0 && SessionIndex < _sessionOrder.Count)
        _sessions.TryGetValue(_sessionOrder[SessionIndex], out target);
    }

    target?.Write(data);
    OnDataWrite?.Invoke(data, this);
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
    _acceptCts?.Dispose();
    _acceptCts = null;
    OnSessionStateChanged = null;
    OnDataWrite = null;
    OnDataReceived = null;
    OnConnectStateChanged = null;
    GC.SuppressFinalize(this);
  }

  private async Task RunAcceptLoopAsync(CancellationToken ct)
  {
    try
    {
      while (!ct.IsCancellationRequested)
      {
        var ls = _listenSocket;
        if (ls == null) break;

        Socket accepted;
        try { accepted = await ls.AcceptAsync(ct); }
        catch (OperationCanceledException) { break; }
        catch (ObjectDisposedException) { break; }
        catch (SocketException) { break; }

        accepted.NoDelay = true;
        accepted.LingerState = new LingerOption(true, 0);

        var session = new DtStreamTcpSession(accepted, this);
        session.OnDataReceived = (span, _) => OnDataReceived?.Invoke(span, this);

        lock (_lock)
        {
          _sessions.TryAdd(session.Id, session);
          _sessionOrder.Add(session.Id);
        }

        session.OnDisconnected = () =>
        {
          lock (_lock)
          {
            _sessions.TryRemove(session.Id, out _);
            _sessionOrder.Remove(session.Id);
          }
          NotifySessionStateChanged();
        };

        session.Start();
        NotifySessionStateChanged();
      }
    }
    catch { /* accept loop ended */ }
  }

  private void NotifySessionStateChanged()
  {
    lock (_lock)
    {
      OnSessionStateChanged?.Invoke(
        _sessionOrder.Select(id =>
        {
          if (_sessions.TryGetValue(id, out var s) && s.RemoteEndPoint is IPEndPoint ipEp)
            return $"{ipEp.Address}:{ipEp.Port}";
          return null;
        }).Where(s => s != null)!
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

public class DtStreamTcpSession : IDisposable
{
  private readonly Socket _socket;
  private CancellationTokenSource? _receiveCts;
  private Task? _receiveTask;
  private volatile bool _isDisposed;

  private const int ReceiveBufferSize = 4096;

  public Guid Id { get; } = Guid.NewGuid();
  public EndPoint? RemoteEndPoint => _socket.RemoteEndPoint;

  internal Action? OnDisconnected;
  internal ReadOnlySpanAction<byte, DtStreamTcpSession>? OnDataReceived;

  internal DtStreamTcpSession(Socket socket, TcpServerDtStream _)
  {
    _socket = socket;
  }

  internal void Start()
  {
    _receiveCts = new CancellationTokenSource();
    _receiveTask = Task.Factory.StartNew(
      () => RunReceiveLoopAsync(_receiveCts.Token),
      _receiveCts.Token,
      TaskCreationOptions.LongRunning,
      TaskScheduler.Default
    ).Unwrap();
  }

  internal void Write(ReadOnlySpan<byte> data)
  {
    try
    {
      _socket.Send(data);
    }
    catch { /* ignore write errors */ }
  }

  private async Task RunReceiveLoopAsync(CancellationToken ct)
  {
    var buffer = new byte[ReceiveBufferSize];
    try
    {
      while (!ct.IsCancellationRequested)
      {
        int received;
        try { received = await _socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, ct); }
        catch (SocketException) { break; }
        catch (OperationCanceledException) { break; }
        catch (ObjectDisposedException) { break; }

        if (received == 0) break;
        OnDataReceived?.Invoke(buffer.AsSpan(0, received), this);
      }
    }
    finally
    {
      if (!_isDisposed)
      {
        _isDisposed = true;
        OnDisconnected?.Invoke();
      }
    }
  }

  public void Dispose()
  {
    if (_isDisposed) return;
    _isDisposed = true;

    try { _receiveCts?.Cancel(); } catch { }
    try { _receiveTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
    _receiveCts?.Dispose();
    _receiveCts = null;

    try { _socket.Shutdown(SocketShutdown.Both); } catch { }
    try { _socket.Close(); } catch { }
    _socket.Dispose();

    OnDataReceived = null;
    OnDisconnected = null;
  }
}
