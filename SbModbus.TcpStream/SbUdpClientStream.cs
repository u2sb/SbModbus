using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NetCoreServer;
using SbModbus.Models;

namespace SbModbus.TcpStream;

/// <summary>
///   UDP客户端流
/// </summary>
public class SbUdpClientStream : ModbusStream, IModbusStream
{
  private readonly SbUdpClient _udpClient;
  private readonly string _transportInfo;

  /// <inheritdoc />
  public SbUdpClientStream(IPAddress address, int port)
  {
    _transportInfo = $"UDP:{address}:{port}";
    _udpClient = new SbUdpClient(address, port)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public SbUdpClientStream(string address, int port)
  {
    _transportInfo = $"UDP:{address}:{port}";
    _udpClient = new SbUdpClient(address, port)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public SbUdpClientStream(DnsEndPoint endpoint)
  {
    _transportInfo = $"UDP:{endpoint.Host}:{endpoint.Port}";
    _udpClient = new SbUdpClient(endpoint)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public SbUdpClientStream(IPEndPoint endpoint)
  {
    _transportInfo = $"UDP:{endpoint.Address}:{endpoint.Port}";
    _udpClient = new SbUdpClient(endpoint)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public override void Dispose()
  {
    _udpClient.Dispose();
    base.Dispose();
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
    Logger.Information("UDP client connecting...");
    return _udpClient.Connect();
  }

  /// <inheritdoc />
  public override string GetTransportInfo() => _transportInfo;

  /// <inheritdoc />
  public override bool Disconnect()
  {
    Logger.Information("UDP client disconnecting");
    return _udpClient.Disconnect();
  }

  /// <inheritdoc />
  protected override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    _udpClient.SendAsync(buffer.Span);
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

      ReceiveAsync();
    }

    protected override void OnConnected()
    {
      Logger.Information("UDP client connected");
      ModbusStream.ConnectStateChanged(true);
      ReceiveAsync();
    }

    protected override void OnDisconnected()
    {
      Logger.Warning("UDP client disconnected");
      ModbusStream.ConnectStateChanged(false);
    }

    protected override void OnError(System.Net.Sockets.SocketError error)
    {
      Logger.Log(LogLevel.Error, $"UDP client socket error: {error}");
    }
  }
}
