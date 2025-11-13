using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
#if NETSTANDARD2_0
using CommunityToolkit.HighPerformance;
#endif


namespace SbModbus.Models;

/// <inheritdoc />
public abstract class ModbusStream : IModbusStream
{
  /// <inheritdoc />
  public abstract void Dispose();

  /// <inheritdoc />
  public abstract Stream? BaseStream { get; protected set; }

  /// <inheritdoc />
  public abstract bool IsConnected { get; }

  /// <inheritdoc />
  public abstract int ReadTimeout { get; set; }

  /// <inheritdoc />
  public abstract int WriteTimeout { get; set; }

  /// <inheritdoc />
  public abstract bool Connect();

  /// <inheritdoc />
  public abstract bool Disconnect();

  /// <inheritdoc />
  public abstract ValueTask ClearReadBufferAsync(CancellationToken ct = default);

  /// <inheritdoc />
  public virtual void Write(ReadOnlySpan<byte> buffer)
  {
    if (IsConnected && BaseStream is not null)
    {
      BaseStream.Write(buffer);
      BaseStream.Flush();
    }
  }

  /// <inheritdoc />
  public virtual async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    if (IsConnected && BaseStream is not null)
    {
      await BaseStream.WriteAsync(buffer, ct);
      await BaseStream.FlushAsync(ct);
    }
  }

  /// <inheritdoc />
  public virtual int Read(Span<byte> buffer)
  {
    if (IsConnected && BaseStream is not null)
    {
#if NET8_0_OR_GREATER
      BaseStream.ReadExactly(buffer);
      return buffer.Length;
#else
      var tb = 0;

      // 循环读取，直到填满整个buffer或遇到流结束
      while (tb < buffer.Length)
      {
        // 读取剩余部分的数据
        var len = BaseStream.Read(buffer[tb..]);

        if (len == 0)
          // 流提前结束，抛出异常
          throw new EndOfStreamException(
            $"Unexpected end of stream. Expected {buffer.Length} bytes, but only got {tb} bytes.");

        tb += len;
      }

      return tb;
#endif
    }

    return 0;
  }

  /// <inheritdoc />
  public virtual async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
  {
    if (IsConnected && BaseStream is not null)
    {
#if NET8_0_OR_GREATER
      await BaseStream.ReadExactlyAsync(buffer, ct);
      return buffer.Length;
#else
      var tb = 0;

      // 循环读取，直到填满整个buffer或遇到流结束
      while (tb < buffer.Length)
      {
        // 读取剩余部分的数据
        var len = await BaseStream.ReadAsync(buffer[tb..], ct);

        if (len == 0)
          // 流提前结束，抛出异常
          throw new EndOfStreamException(
            $"Unexpected end of stream. Expected {buffer.Length} bytes, but only got {tb} bytes.");

        tb += len;
      }

      return tb;
#endif
    }

    return 0;
  }
}