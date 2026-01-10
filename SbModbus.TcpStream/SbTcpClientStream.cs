using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NetCoreServer;
using Sb.Extensions.System.Buffers.RingBuffers;
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

  /// <inheritdoc />
  public override void Dispose()
  {
    _tcpClient.Dispose();
    GC.SuppressFinalize(this);
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
    _tcpClient.ClearBuffer();
    return ValueTask.CompletedTask;
  }

  /// <inheritdoc />
  public override void Write(ReadOnlySpan<byte> buffer)
  {
    _tcpClient.SendAsync(buffer);
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
    var len = _tcpClient.GetBuffer(buffer);
    return len;
  }

  /// <inheritdoc />
  public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
  {
    var len = Read(buffer.Span);
    return ValueTask.FromResult(len);
  }

  private class SbTcpClient : TcpClient
  {
    /// <summary>
    ///   缓冲区
    /// </summary>
    private readonly FixedSizeRingBuffer<byte> _circularBuffer = new(4096);


#if NET10_0_OR_GREATER
    private readonly Lock _locker = new();
#else
    private readonly object _locker = new();
#endif

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

    /// <inheritdoc />
    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
#if NET10_0_OR_GREATER
      using(_locker.EnterScope())
#else
      lock (_locker)
#endif
      {
        _circularBuffer.AddLastRange(buffer.AsSpan((int)offset, (int)size));
      }
    }

    /// <summary>
    ///   获取Buffer
    /// </summary>
    /// <param name="buffer"></param>
    public int GetBuffer(Span<byte> buffer)
    {
#if NET10_0_OR_GREATER
      using (_locker.EnterScope())
#else
      lock (_locker)
#endif
      {
        if (_circularBuffer.Count == 0)
        {
          return 0;
        }

        var len = Math.Min(_circularBuffer.Count, buffer.Length);

        _circularBuffer.WrittenSpan[..len].CopyTo(buffer);

        _circularBuffer.RemoveFirst(len);

        return len;
      }
    }

    public void ClearBuffer()
    {
#if NET10_0_OR_GREATER
      using (_locker.EnterScope())
#else
      lock (_locker)
#endif
      {
        _circularBuffer.Clear();
      }
    }
  }
}