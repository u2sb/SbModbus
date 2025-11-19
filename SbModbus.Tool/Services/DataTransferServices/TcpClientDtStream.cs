using System;
using System.Buffers;
using System.Net;
using System.Threading.Tasks;
using NetCoreServer;

namespace SbModbus.Tool.Services.DataTransferServices;

public class TcpClientDtStream : TcpClient, IDtStream
{
  public TcpClientDtStream(IPAddress address, int port) : base(address, port)
  {
  }

  public TcpClientDtStream(string address, int port) : base(address, port)
  {
  }

  public TcpClientDtStream(DnsEndPoint endpoint) : base(endpoint)
  {
  }

  public TcpClientDtStream(IPEndPoint endpoint) : base(endpoint)
  {
  }

  public ReadOnlySpanAction<byte, IDtStream>? OnDataWrite { get; set; }
  public ReadOnlySpanAction<byte, IDtStream>? OnDataReceived { get; set; }
  public Action<bool>? OnConnectStateChanged { get; set; }


  /// <summary>
  ///   这里使用异步
  /// </summary>
  public new bool Connect()
  {
    return base.ConnectAsync();
  }

  /// <summary>
  ///   使用异步
  /// </summary>
  public new void Disconnect()
  {
    base.DisconnectAsync();
  }

  public void Write(ReadOnlySpan<byte> data)
  {
    SendAsync(data);
    OnDataWrite?.Invoke(data, this);
  }

  public ValueTask WriteAsync(ReadOnlyMemory<byte> data)
  {
    Write(data.Span);
    return ValueTask.CompletedTask;
  }


  protected override void OnReceived(byte[] buffer, long offset, long size)
  {
    var span = new ReadOnlySpan<byte>(buffer, (int)offset, (int)size);
    OnDataReceived?.Invoke(span, this);
  }

  protected override void OnConnected()
  {
    OnConnectStateChanged?.Invoke(true);
    base.OnConnected();
  }

  protected override void OnDisconnected()
  {
    OnConnectStateChanged?.Invoke(false);
    base.OnDisconnected();
  }
}