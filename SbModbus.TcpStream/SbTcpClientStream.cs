using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NetCoreServer;
using SbModbus.Models;

namespace SbModbus.TcpStream;

/// <summary>
///   TCP客户端流
/// </summary>
public class SbTcpClientStream : ModbusStream, IModbusStream
{
  private readonly SbTcpClient _tcpClient;
  private readonly string _transportInfo;

  /// <inheritdoc />
  public SbTcpClientStream(IPAddress address, int port)
  {
    _transportInfo = $"TCP:{address}:{port}";
    _tcpClient = new SbTcpClient(address, port)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public SbTcpClientStream(string address, int port)
  {
    _transportInfo = $"TCP:{address}:{port}";
    _tcpClient = new SbTcpClient(address, port)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public SbTcpClientStream(DnsEndPoint endpoint)
  {
    _transportInfo = $"TCP:{endpoint.Host}:{endpoint.Port}";
    _tcpClient = new SbTcpClient(endpoint)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public SbTcpClientStream(IPEndPoint endpoint)
  {
    _transportInfo = $"TCP:{endpoint.Address}:{endpoint.Port}";
    _tcpClient = new SbTcpClient(endpoint)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public override void Dispose()
  {
    _tcpClient.Dispose();
    base.Dispose();
    StreamLock.Dispose();
    GC.SuppressFinalize(this);
  }

  /// <inheritdoc />
  public override bool IsConnected => _tcpClient.IsConnected;

  /// <inheritdoc />
  public override int ReadTimeout { get; set; } = 2000;

  /// <inheritdoc />
  public override int WriteTimeout { get; set; } = 2000;

  /// <inheritdoc />
  public override bool Connect()
  {
    Logger.Information("TCP client connecting...");
    return _tcpClient.ConnectAsync();
  }

  /// <inheritdoc />
  public override string GetTransportInfo() => _transportInfo;

  /// <inheritdoc />
  public override bool Disconnect()
  {
    Logger.Information("TCP client disconnecting");
    return _tcpClient.Disconnect();
  }

  /// <inheritdoc />
  protected override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    _tcpClient.SendAsync(buffer.Span);
    return ValueTask.CompletedTask;
  }

  private class SbTcpClient : TcpClient
  {
    public SbTcpClient(IPAddress address, int port) : base(address, port)
    {
    }

    public SbTcpClient(string address, int port) : base(address, port)
    {
    }

    public SbTcpClient(DnsEndPoint endpoint) : base(endpoint)
    {
    }

    public SbTcpClient(IPEndPoint endpoint) : base(endpoint)
    {
    }

    public required SbTcpClientStream ModbusStream { get; init; }

    /// <inheritdoc />
    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
      var span = buffer.AsSpan((int)offset, (int)size);
      ModbusStream.WriteBuffer(span);
    }

    protected override void OnConnected()
    {
      Logger.Information("TCP client connected");
      ModbusStream.ConnectStateChanged(true);
    }

    protected override void OnDisconnected()
    {
      Logger.Warning("TCP client disconnected");
      ModbusStream.ConnectStateChanged(false);
    }

    protected override void OnError(System.Net.Sockets.SocketError error)
    {
      Logger.Log(LogLevel.Error, $"TCP client socket error: {error}");
    }
  }
}
