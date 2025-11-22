using System.Buffers;
using System.Net;
using NetCoreServer;

namespace SbModbus.Tool.Services.DataTransferServices;

public class TcpServerDtStream : TcpServer, IDtStream
{
  public TcpServerDtStream(IPAddress address, int port) : base(address, port)
  {
  }

  public TcpServerDtStream(string address, int port) : base(address, port)
  {
  }

  public TcpServerDtStream(DnsEndPoint endpoint) : base(endpoint)
  {
  }

  public TcpServerDtStream(IPEndPoint endpoint) : base(endpoint)
  {
  }

  /// <summary>
  ///   选中的GUID
  /// </summary>
  public int SessionIndex { get; set; } = -1;

  /// <summary>
  ///   客户端连接和断开连接时
  /// </summary>
  public Action<IEnumerable<string>>? OnSessionStateChanged { get; set; }

  public ReadOnlySpanAction<byte, IDtStream>? OnDataWrite { get; set; }
  public ReadOnlySpanAction<byte, IDtStream>? OnDataReceived { get; set; }
  public Action<bool>? OnConnectStateChanged { get; set; }

  public bool IsConnected => IsStarted;

  public bool Connect()
  {
    return Start();
  }

  public void Disconnect()
  {
    Stop();
  }

  public void Write(ReadOnlySpan<byte> data)
  {
    if (SessionIndex >= 0 && SessionIndex < Sessions.Count)
    {
      var sessionKey = Sessions.Keys.ToArray()[SessionIndex];
      if (Sessions.TryGetValue(sessionKey, out var session))
      {
        session.SendAsync(data);
        OnDataWrite?.Invoke(data, this);
      }
    }
  }

  public ValueTask WriteAsync(ReadOnlyMemory<byte> data)
  {
    Write(data.Span);
    return ValueTask.CompletedTask;
  }

  protected override TcpSession CreateSession()
  {
    return new DtStreamTcpSession(this)
    {
      OnDataReceived = OnSessionDataReceived
    };
  }

  private void OnSessionDataReceived(ReadOnlySpan<byte> span, TcpSession session)
  {
    OnDataReceived?.Invoke(span, this);
  }

  protected override void OnStarted()
  {
    OnConnectStateChanged?.Invoke(true);
    base.OnStarted();
  }

  protected override void OnStopped()
  {
    OnConnectStateChanged?.Invoke(false);
    OnSessionStateChanged?.Invoke([]);
    base.OnStopped();
  }

  protected override void OnConnected(TcpSession session)
  {
    base.OnConnected(session);
    OnSessionStateChanged?.Invoke(Sessions.Values.Select(s =>
    {
      if (s.Socket.RemoteEndPoint is IPEndPoint ipEndPoint)
      {
        return $"{ipEndPoint!.Address}:{ipEndPoint!.Port}";
      }

      return null;
    }).Where(s => s is not null)!);
  }

  protected override void OnDisconnected(TcpSession session)
  {
    base.OnDisconnected(session);

    if (Sessions.ContainsKey(session.Id))
    {
      OnSessionStateChanged?.Invoke(Sessions.Values.Except([session]).Select(s =>
      {
        if (s.Socket.RemoteEndPoint is IPEndPoint ipEndPoint)
        {
          return $"{ipEndPoint!.Address}:{ipEndPoint!.Port}";
        }

        return null;
      }).Where(s => s is not null)!);
    }
  }
}

public class DtStreamTcpSession(TcpServer server) : TcpSession(server)
{
  public required ReadOnlySpanAction<byte, TcpSession> OnDataReceived { get; init; }

  protected override void OnReceived(byte[] buffer, long offset, long size)
  {
    var span = new ReadOnlySpan<byte>(buffer, (int)offset, (int)size);
    OnDataReceived.Invoke(span, this);
  }
}