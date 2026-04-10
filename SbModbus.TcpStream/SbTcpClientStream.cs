using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NetCoreServer;
using SbModbus.Models;

namespace SbModbus.TcpStream;

/// <summary>
/// </summary>
public class SbTcpClientStream : ModbusStream, IModbusStream
{
  private readonly SbTcpClient _tcpClient;

  /// <inheritdoc />
  public SbTcpClientStream(IPAddress address, int port)
  {
    _tcpClient = new SbTcpClient(address, port)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public SbTcpClientStream(string address, int port)
  {
    _tcpClient = new SbTcpClient(address, port)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public SbTcpClientStream(DnsEndPoint endpoint)
  {
    _tcpClient = new SbTcpClient(endpoint)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public SbTcpClientStream(IPEndPoint endpoint)
  {
    _tcpClient = new SbTcpClient(endpoint)
    {
      ModbusStream = this
    };
  }

  /// <inheritdoc />
  public override void Dispose()
  {
    _tcpClient.Dispose();
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
    return _tcpClient.ConnectAsync();
  }

  /// <inheritdoc />
  public override bool Disconnect()
  {
    return _tcpClient.Disconnect();
  }

  /// <inheritdoc />
  protected override void Write(ReadOnlySpan<byte> buffer)
  {
    _tcpClient.SendAsync(buffer);
    DataSent(buffer);
  }

  /// <inheritdoc />
  protected override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    Write(buffer.Span);
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
      ModbusStream.DataReceived(span);
    }

    protected override void OnConnected()
    {
      ModbusStream.ConnectStateChanged(true);
    }

    protected override void OnDisconnected()
    {
      ModbusStream.ConnectStateChanged(false);
    }
  }
}
