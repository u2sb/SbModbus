using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NetCoreServer;

namespace SbModbus.Tool.Services.DataTransferServices;

public class UdpServerDtStream : UdpServer, IDtStream
{
  public UdpServerDtStream(IPAddress address, int port) : base(address, port)
  {
  }

  public UdpServerDtStream(string address, int port) : base(address, port)
  {
  }

  public UdpServerDtStream(DnsEndPoint endpoint) : base(endpoint)
  {
  }

  public UdpServerDtStream(IPEndPoint endpoint) : base(endpoint)
  {
  }

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
    if (SelectedSessionIndex >= 0 && SelectedSessionIndex < Sessions.Count)
    {
      var sessions = Sessions[SelectedSessionIndex];
      Send(sessions, data);
      OnDataWrite?.Invoke(data, this);
    }
  }

  public ValueTask WriteAsync(ReadOnlyMemory<byte> data)
  {
    Write(data.Span);
    return ValueTask.CompletedTask;
  }

  #region 管理客户端

  protected override void OnStarted()
  {
    OnConnectStateChanged?.Invoke(true);
    ReceiveAsync();
  }

  private List<EndPoint> Sessions { get; } = [];

  public int SelectedSessionIndex { get; set; } = -1;

  protected override void OnReceived(EndPoint endpoint, byte[] buffer, long offset, long size)
  {
    if (!Sessions.Contains(endpoint))
    {
      Sessions.Add(endpoint);
      OnSessionStateChanged?.Invoke(Sessions.Where(w => w is IPEndPoint).Select(s =>
      {
        var ipEndPoint = s as IPEndPoint;
        return $"{ipEndPoint!.Address}:{ipEndPoint!.Port}";
      }));
    }

    var span = new ReadOnlySpan<byte>(buffer, (int)offset, (int)size);
    OnDataReceived?.Invoke(span, this);
    ReceiveAsync();
  }

  protected override void OnSent(EndPoint endpoint, long sent)
  {
    ReceiveAsync();
  }

  protected override void OnStopped()
  {
    OnSessionStateChanged?.Invoke([]);
    OnConnectStateChanged?.Invoke(false);
    base.OnStopped();
  }

  #endregion
}