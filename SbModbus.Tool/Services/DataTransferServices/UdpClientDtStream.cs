using System;
using System.Buffers;
using System.Net;
using System.Threading.Tasks;
using NetCoreServer;

namespace SbModbus.Tool.Services.DataTransferServices;

public class UdpClientDtStream : UdpClient, IDtStream
{
  private IPEndPoint? _sendEndpoint;

  public UdpClientDtStream(IPAddress address, int port) : base(address, port)
  {
  }

  public UdpClientDtStream(string address, int port) : base(address, port)
  {
  }

  public UdpClientDtStream(DnsEndPoint endpoint) : base(endpoint)
  {
  }

  public UdpClientDtStream(IPEndPoint endpoint) : base(endpoint)
  {
  }

  public ReadOnlySpanAction<byte, IDtStream>? OnDataWrite { get; set; }
  public ReadOnlySpanAction<byte, IDtStream>? OnDataReceived { get; set; }
  public Action<bool>? OnConnectStateChanged { get; set; }

  public new void Disconnect()
  {
    base.Disconnect();
  }

  public void Write(ReadOnlySpan<byte> data)
  {
    if (_sendEndpoint is not null)
      SendAsync(_sendEndpoint, data);
    else
      SendAsync(data);
    OnDataWrite?.Invoke(data, this);
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

  protected override void OnReceived(EndPoint endpoint, byte[] buffer, long offset, long size)
  {
    var span = new ReadOnlySpan<byte>(buffer, (int)offset, (int)size);
    OnDataReceived?.Invoke(span, this);
    ReceiveAsync();
  }

  protected override void OnConnected()
  {
    OnConnectStateChanged?.Invoke(true);
    ReceiveAsync();
    base.OnConnected();
  }

  protected override void OnDisconnected()
  {
    OnConnectStateChanged?.Invoke(false);
    base.OnDisconnected();
  }
}