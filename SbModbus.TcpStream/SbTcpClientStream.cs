using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetCoreServer;
using SbModbus.Models;

namespace SbModbus.TcpStream;

/// <summary>
///   TCP客户端流
/// </summary>
public class SbTcpClientStream : ModbusStream, IModbusStream
{
  private readonly SbTcpClient _tcpClient;
  private readonly ILogger? _logger;

  /// <inheritdoc />
  public SbTcpClientStream(IPAddress address, int port, ILogger<SbTcpClientStream>? logger = null) : base(logger)
  {
    _logger = logger;
    _tcpClient = new SbTcpClient(address, port)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public SbTcpClientStream(string address, int port, ILogger<SbTcpClientStream>? logger = null) : base(logger)
  {
    _logger = logger;
    _tcpClient = new SbTcpClient(address, port)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public SbTcpClientStream(DnsEndPoint endpoint, ILogger<SbTcpClientStream>? logger = null) : base(logger)
  {
    _logger = logger;
    _tcpClient = new SbTcpClient(endpoint)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public SbTcpClientStream(IPEndPoint endpoint, ILogger<SbTcpClientStream>? logger = null) : base(logger)
  {
    _logger = logger;
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
    _logger?.LogInformation("TCP client connecting...");
    return _tcpClient.ConnectAsync();
  }

  /// <inheritdoc />
  public override bool Disconnect()
  {
    _logger?.LogInformation("TCP client disconnecting");
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
      ModbusStream._logger?.LogInformation("TCP client connected");
      ModbusStream.ConnectStateChanged(true);
    }

    protected override void OnDisconnected()
    {
      ModbusStream._logger?.LogWarning("TCP client disconnected");
      ModbusStream.ConnectStateChanged(false);
    }

    protected override void OnError(System.Net.Sockets.SocketError error)
    {
      ModbusStream._logger?.LogError("TCP client socket error: {Error}", error);
    }
  }
}
