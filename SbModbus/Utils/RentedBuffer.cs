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
  private byte[] _buffer;
  private int _count;

  /// <summary>
  ///   从 ArrayPool 租用指定最小长度的缓冲区。
  /// </summary>
  public RentedBuffer(int minimumLength)
  {
    _buffer = ArrayPool<byte>.Shared.Rent(minimumLength);
    _count = 0;
  }

  /// <summary>
  ///   已写入部分的只读内存。
  /// </summary>
  public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _count);

  /// <summary>
  ///   已写入部分的只读 Span。
  /// </summary>
  public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _count);

  /// <summary>
  ///   已写入的字节数。
  /// </summary>
  public int WrittenCount => _count;

  /// <summary>
  ///   底层原始缓冲区（用于需要直接修改已写入区域的场景，如填写 TCP 长度字段）。
  /// </summary>
  public byte[] RawBuffer => _buffer;

  /// <inheritdoc />
  public void Advance(int count)
  {
    _count += count;
  }

  /// <inheritdoc />
  public Memory<byte> GetMemory(int sizeHint = 0)
  {
    return _buffer.AsMemory(_count);
  }

  /// <inheritdoc />
  public Span<byte> GetSpan(int sizeHint = 0)
  {
    return _buffer.AsSpan(_count);
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

  /// <inheritdoc />
  public void Dispose()
  {
    if (_buffer != null!)
    {
      ArrayPool<byte>.Shared.Return(_buffer);
      _buffer = null!;
    }
  }
}
