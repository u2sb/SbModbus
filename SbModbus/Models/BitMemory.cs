using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using SbModbus.Utils;

#pragma warning disable CS1591 // 缺少对公共可见类型或成员的 XML 注释

namespace SbModbus.Models;

public readonly struct BitMemory : IEquatable<BitMemory>
{
  private readonly Memory<byte> _memory;
  private readonly int _startBitOffset;

  public MemoryHandle Pin()
  {
    return _memory.Pin();
  }

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
      var (start, length) = range.GetOffsetAndLength(Length);
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

  public bool Equals(BitMemory other)
  {
    return _memory.Equals(other._memory) &&
           _startBitOffset == other._startBitOffset &&
           Length == other.Length;
  }

  public override bool Equals(object? obj)
  {
    return obj is BitMemory other && Equals(other);
  }

  public override int GetHashCode()
  {
    return HashCode.Combine(_memory, _startBitOffset, Length);
  }

  public static bool operator ==(BitMemory left, BitMemory right)
  {
    return left.Equals(right);
  }

  public static bool operator !=(BitMemory left, BitMemory right)
  {
    return !left.Equals(right);
  }

  #region Helper Methods

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