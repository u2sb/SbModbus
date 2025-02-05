using System;
using System.Runtime.CompilerServices;
using SbModbus.Utils;

#pragma warning disable CS1591 // 缺少对公共可见类型或成员的 XML 注释

namespace SbModbus.Models;

public readonly struct BitMemory
{
  private readonly Memory<byte> _memory;
  private readonly int _startBitOffset;

  public BitMemory(Memory<byte> memory, int bitCount, int startBitOffset = 0)
  {
    if (bitCount < 0)
      throw new ArgumentOutOfRangeException(nameof(bitCount));
    if (startBitOffset is < 0 or >= 8)
      throw new ArgumentOutOfRangeException(nameof(startBitOffset));

    var maxAvailableBits = memory.Length * 8 - startBitOffset;
    if (bitCount > maxAvailableBits)
      throw new ArgumentException("Insufficient buffer for bit count and offset.");

    _memory = memory;
    Length = bitCount;
    _startBitOffset = startBitOffset;
  }

  public BitMemory(Memory<byte> memory)
  {
    _memory = memory;
    Length = _memory.Length * 8;
    _startBitOffset = 0;
  }

  public BitMemory(Memory<ushort> memory)
  {
    _memory = memory.AsByteMemory();
    Length = _memory.Length * 8;
    _startBitOffset = 0;
  }

  public int Length { get; }

  public BitSpan Span => new(_memory.Span, Length, _startBitOffset);

  public BitMemory this[Range range]
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get
    {
      var (start, length) = GetRangeBounds(range);
      return Slice(start, length);
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public BitMemory Slice(int startBit, int length)
  {
    if (startBit < 0 || length < 0 || startBit + length > Length)
      ThrowInvalidSliceArguments();

    var newStartBitOffset = _startBitOffset + startBit;
    var startByte = newStartBitOffset >> 3;
    var newBitOffset = newStartBitOffset & 7;

    var requiredBytes = (length + newBitOffset + 7) >> 3;
    var newMemory = _memory.Slice(startByte, requiredBytes);

    return new BitMemory(newMemory, length, newBitOffset);
  }

  #region Helper Methods

  private (int Start, int Length) GetRangeBounds(Range range)
  {
    var start = range.Start.IsFromEnd ? Length - range.Start.Value : range.Start.Value;
    var end = range.End.IsFromEnd ? Length - range.End.Value : range.End.Value;

    if (start < 0 || end > Length || start > end)
      ThrowInvalidRange();

    return (start, end - start);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void ThrowInvalidSliceArguments()
  {
    throw new ArgumentOutOfRangeException("Invalid slice arguments");
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void ThrowInvalidRange()
  {
    throw new ArgumentOutOfRangeException("Invalid range specified");
  }

  #endregion
}