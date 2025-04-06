using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NetCoreServer;
using SbModbus.Services;
using Buffer = NetCoreServer.Buffer;

namespace SbModbus.TcpStream;

/// <summary>
/// </summary>
public class SbTcpClientStream : TcpClient, IModbusStream
{
  private readonly Buffer _buffer = new(4096);

  /// <inheritdoc />
  public SbTcpClientStream(IPAddress address, int port) : base(address, port)
  {
  }

  /// <inheritdoc />
  public SbTcpClientStream(string address, int port) : base(address, port)
  {
  }

  /// <inheritdoc />
  public SbTcpClientStream(DnsEndPoint endpoint) : base(endpoint)
  {
  }

  /// <inheritdoc />
  public SbTcpClientStream(IPEndPoint endpoint) : base(endpoint)
  {
  }

  /// <inheritdoc />
  public int ReadTimeout { get; set; } = 1000;

  /// <inheritdoc />
  public int WriteTimeout { get; set; } = 1000;


  /// <inheritdoc />
  public ValueTask ClearReadBufferAsync(CancellationToken ct = default)
  {
    _buffer.Clear();
    return ValueTask.CompletedTask;
  }

  /// <inheritdoc />
  public void Write(ReadOnlySpan<byte> buffer)
  {
    Send(buffer);
  }

  /// <inheritdoc />
  public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    SendAsync(buffer.Span);
    return ValueTask.CompletedTask;
  }

  /// <inheritdoc />
  public int Read(Span<byte> buffer)
  {
    _buffer.AsSpan()[..Math.Min((int)_buffer.Size, buffer.Length)].CopyTo(buffer);
    return (int)_buffer.Size;
  }

  /// <inheritdoc />
  public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
  {
    return await Task.Run(() => Read(buffer.Span), ct);
  }

  /// <inheritdoc />
  protected override void OnReceived(byte[] buffer, long offset, long size)
  {
    _buffer.Clear();
    _buffer.Append(buffer, offset, size);
  }
}