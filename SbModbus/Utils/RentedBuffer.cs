using System;
using System.Buffers;

namespace SbModbus.Utils;

/// <summary>
///   基于 <see cref="ArrayPool{T}.Shared" /> 的 <see cref="IBufferWriter{T}" /> 实现，
///   替代 <c>ArrayBufferWriter&lt;byte&gt;</c> 以避免每次帧构建时的数组分配。
///   Dispose 时自动归还数组到池。
/// </summary>
public sealed class RentedBuffer : IBufferWriter<byte>, IDisposable
{
  /// <summary>
  ///   从 ArrayPool 租用指定最小长度的缓冲区。
  /// </summary>
  public RentedBuffer(int minimumLength)
  {
    RawBuffer = ArrayPool<byte>.Shared.Rent(minimumLength);
    WrittenCount = 0;
  }

  /// <summary>
  ///   已写入部分的只读内存。
  /// </summary>
  public ReadOnlyMemory<byte> WrittenMemory => RawBuffer.AsMemory(0, WrittenCount);

  /// <summary>
  ///   已写入部分的只读 Span。
  /// </summary>
  public ReadOnlySpan<byte> WrittenSpan => RawBuffer.AsSpan(0, WrittenCount);

  /// <summary>
  ///   已写入的字节数。
  /// </summary>
  public int WrittenCount { get; private set; }

  /// <summary>
  ///   底层原始缓冲区（用于需要直接修改已写入区域的场景，如填写 TCP 长度字段）。
  /// </summary>
  public byte[] RawBuffer { get; private set; }

  /// <inheritdoc />
  public void Advance(int count)
  {
    WrittenCount += count;
  }

  /// <inheritdoc />
  public Memory<byte> GetMemory(int sizeHint = 0)
  {
    return RawBuffer.AsMemory(WrittenCount);
  }

  /// <inheritdoc />
  public Span<byte> GetSpan(int sizeHint = 0)
  {
    return RawBuffer.AsSpan(WrittenCount);
  }

  /// <inheritdoc />
  public void Dispose()
  {
    if (RawBuffer != null!)
    {
      ArrayPool<byte>.Shared.Return(RawBuffer);
      RawBuffer = null!;
    }
  }

  /// <summary>
  ///   计算已写入数据的 CRC16 并追加到缓冲区（小端序）。
  /// </summary>
  public void WriteCrc16()
  {
    var crc = Crc16.CalculateCrc16(WrittenSpan);

    var crcBytes = GetSpan(2);
    crcBytes[0] = (byte)crc;
    crcBytes[1] = (byte)(crc >> 8);
    Advance(2);
  }
}
