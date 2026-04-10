using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NetCoreServer;
using SbModbus.Models;

namespace SbModbus.TcpStream;

/// <summary>
/// </summary>
public class SbUdpClientStream : ModbusStream, IModbusStream
{
  private readonly SbUdpClient _udpClient;

  /// <inheritdoc />
  public SbUdpClientStream(IPAddress address, int port)
  {
    _udpClient = new SbUdpClient(address, port)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public SbUdpClientStream(string address, int port)
  {
    _udpClient = new SbUdpClient(address, port)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public SbUdpClientStream(DnsEndPoint endpoint)
  {
    _udpClient = new SbUdpClient(endpoint)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public SbUdpClientStream(IPEndPoint endpoint)
  {
    _udpClient = new SbUdpClient(endpoint)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public override void Dispose()
  {
    _udpClient.Dispose();
    StreamLock.Dispose();
    GC.SuppressFinalize(this);
  }

  /// <inheritdoc />
  public override bool IsConnected => _udpClient.IsConnected;

  /// <inheritdoc />
  public override int ReadTimeout { get; set; } = 2000;

  /// <inheritdoc />
  public override int WriteTimeout { get; set; } = 2000;

  /// <inheritdoc />
  public override bool Connect()
  {
    return _udpClient.Connect();
  }

  /// <inheritdoc />
  public override bool Disconnect()
  {
    return _udpClient.Disconnect();
  }

  /// <inheritdoc />
  protected override void Write(ReadOnlySpan<byte> buffer)
  {
    _udpClient.SendAsync(buffer);
    DataSent(buffer);
  }

  /// <inheritdoc />
  protected override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    Write(buffer.Span);
    return ValueTask.CompletedTask;
  }

  private class SbUdpClient : UdpClient
  {
    public SbUdpClient(IPAddress address, int port) : base(address, port)
    {
    }

    public SbUdpClient(string address, int port) : base(address, port)
    {
    }

    public SbUdpClient(DnsEndPoint endpoint) : base(endpoint)
    {
    }

    public SbUdpClient(IPEndPoint endpoint) : base(endpoint)
    {
    }

    public required SbUdpClientStream ModbusStream { get; init; }

    protected override void OnReceived(EndPoint endpoint, byte[] buffer, long offset, long size)
    {
      var span = buffer.AsSpan((int)offset, (int)size);
      ModbusStream.WriteBuffer(span);
      ModbusStream.DataReceived(span);

      ReceiveAsync();
    }

    protected override void OnConnected()
    {
      ModbusStream.ConnectStateChanged(true);
      ReceiveAsync();
    }

    protected override void OnDisconnected()
    {
      ModbusStream.ConnectStateChanged(false);
    }
  }
}
