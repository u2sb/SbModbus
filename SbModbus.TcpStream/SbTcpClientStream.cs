using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CircularBuffer;
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
    _tcpClient = new SbTcpClient(address, port);
  }

  /// <inheritdoc />
  public SbTcpClientStream(string address, int port)
  {
    _tcpClient = new SbTcpClient(address, port);
  }

  /// <inheritdoc />
  public SbTcpClientStream(DnsEndPoint endpoint)
  {
    _tcpClient = new SbTcpClient(endpoint);
  }

  /// <inheritdoc />
  public SbTcpClientStream(IPEndPoint endpoint)
  {
    _tcpClient = new SbTcpClient(endpoint);
  }

  public override void Dispose()
  {
    _tcpClient.Dispose();
  }

  /// <inheritdoc />
  public override Stream? BaseStream { get; protected set; } = null;

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
  public override ValueTask ClearReadBufferAsync(CancellationToken ct = default)
  {
    _tcpClient.CircularBuffer.Clear();
    return ValueTask.CompletedTask;
  }

  /// <inheritdoc />
  public override void Write(ReadOnlySpan<byte> buffer)
  {
    _tcpClient.SendAsync(buffer);
    OnWrite?.Invoke(buffer, this);
  }

  /// <inheritdoc />
  public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    Write(buffer.Span);
    return ValueTask.CompletedTask;
  }

  /// <inheritdoc />
  public override int Read(Span<byte> buffer)
  {
    var bs = new byte[buffer.Length];
    var l = ReadAsync(bs).AsTask().Result;
    bs.CopyTo(buffer);
    return l;
  }

  /// <inheritdoc />
  public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
  {
    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(ReadTimeout);

    var l = buffer.Length;
    var circularBuffer = _tcpClient.CircularBuffer;

    await Task.Run(async () =>
    {
      try
      {
        for (var i = 0; i < buffer.Length; i++)
        {
          while (circularBuffer.IsEmpty)
          {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Yield();
          }

          buffer.Span[i] = circularBuffer[0];
          circularBuffer.PopFront();
          l = i;
        }
      }
      catch (OperationCanceledException)
      {
        // 忽略这个异常
      }
    }, cts.Token);

    OnRead?.Invoke(buffer.Span[..l], this);
    return l;
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

    /// <summary>
    ///   缓冲区
    /// </summary>
    public CircularBuffer<byte> CircularBuffer { get; } = new(4096);

    /// <inheritdoc />
    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
      var end = offset + size;
      for (var i = offset; i < end; i++)
      {
        if (i >= buffer.Length) return;
        CircularBuffer.PushBack(buffer[i]);
      }
    }
  }
}