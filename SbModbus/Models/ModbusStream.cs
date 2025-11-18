using System;
using System.Buffers;
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

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER

  /// <summary>
  ///   发送事件
  /// </summary>
  public ReadOnlySpanAction<byte, IModbusStream>? OnWrite { get; set; }

  /// <summary>
  ///   接收事件
  /// </summary>
  public ReadOnlySpanAction<byte, IModbusStream>? OnRead { get; set; }

#else
  /// <summary>
  ///   发送事件
  /// </summary>
  public Action<byte[], IModbusStream>? OnWrite { get; set; }

  /// <summary>
  ///   接收事件
  /// </summary>
  public Action<byte[], IModbusStream>? OnRead { get; set; }

#endif

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
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
      OnWrite?.Invoke(buffer, this);
#else
      OnWrite?.Invoke(buffer.ToArray(), this);
#endif
      BaseStream.Flush();
    }
  }

  /// <inheritdoc />
  public virtual async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    if (IsConnected && BaseStream is not null)
    {
      await BaseStream.WriteAsync(buffer, ct);
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
      OnWrite?.Invoke(buffer.Span, this);
#else
      OnWrite?.Invoke(buffer.ToArray(), this);
#endif
      await BaseStream.FlushAsync(ct);
    }
  }

  /// <inheritdoc />
  public abstract int Read(Span<byte> buffer);

  /// <inheritdoc />
  public abstract ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default);
}