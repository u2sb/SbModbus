using System;
using System.Runtime.CompilerServices;

#pragma warning disable CS1591 // 缺少对公共可见类型或成员的 XML 注释

namespace SbModbus.Models;

public readonly ref struct BitSpan
{
  private readonly Span<byte> _span;
  private readonly int _startBitOffset;

  public BitSpan(Span<byte> buffer, int bitCount, int startBitOffset = 0)
  {
    if (bitCount < 0)
      throw new ArgumentOutOfRangeException(nameof(bitCount));
    if (startBitOffset is < 0 or >= 8)
      throw new ArgumentOutOfRangeException(nameof(startBitOffset));

    var maxAvailableBits = buffer.Length * 8 - startBitOffset;
    if (bitCount > maxAvailableBits)
      throw new ArgumentException("Insufficient buffer for bit count and offset.");

    _span = buffer;
    Length = bitCount;
    _startBitOffset = startBitOffset;
  }

  public int Length { get; }

  public bool this[int index]
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => Get(index);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => Set(index, value);
  }

  public BitSpan this[Range range]
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get
    {
      var (start, length) = GetRangeBounds(range);
      return Slice(start, length);
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public BitSpan Slice(int startBit, int length)
  {
    if (startBit < 0 || length < 0 || startBit + length > Length)
      ThrowInvalidSliceArguments();

    var newStartBitOffset = _startBitOffset + startBit;
    var startByte = newStartBitOffset >> 3;
    var newBitOffset = newStartBitOffset & 7;

    var requiredBytes = (length + newBitOffset + 7) >> 3;
    var newSpan = _span.Slice(startByte, requiredBytes);

    return new BitSpan(newSpan, length, newBitOffset);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool Get(int index)
  {
    if ((uint)index >= (uint)Length)
      ThrowIndexOutOfRange();

    var totalBit = _startBitOffset + index;
    var byteIndex = totalBit >> 3;
    var bitOffset = totalBit & 7;
    return (_span[byteIndex] & (1 << bitOffset)) != 0;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Set(int index, bool value)
  {
    if ((uint)index >= (uint)Length)
      ThrowIndexOutOfRange();

    var totalBit = _startBitOffset + index;
    var byteIndex = totalBit >> 3;
    var bitOffset = totalBit & 7;
    ref var target = ref _span[byteIndex];
    if (value)
      target |= (byte)(1 << bitOffset);
    else
      target &= (byte)~(1 << bitOffset);
  }

  public void SetAll(bool value)
  {
    if (Length == 0) return;

    var fullBytes = (Length + _startBitOffset) >> 3;
    var remainingBits = (Length + _startBitOffset) & 7;

    // 批量设置完整字节
    if (fullBytes > 0)
      _span[..fullBytes].Fill(value ? (byte)0xFF : (byte)0x00);

    // 处理剩余位
    if (remainingBits > 0)
    {
      var mask = (byte)((1 << remainingBits) - 1);
      if (value)
        _span[fullBytes] |= mask;
      else
        _span[fullBytes] &= (byte)~mask;
    }
  }

  public byte GetByte(int offset, int length = 8)
  {
    if (length is < 0 or > 8)
      ThrowIndexOutOfRange();
    byte result = 0;
    for (var i = 0; i < length; i++)
      if (Get(offset + i))
        result |= (byte)(1 << i); // LSB-first
    return result;
  }

  public ushort GetUInt16(int offset, int length = 16)
  {
    if (length is < 0 or > 16)
      ThrowIndexOutOfRange();

    ushort result = 0;
    for (var i = 0; i < length; i++)
      if (Get(offset + i))
        result |= (ushort)(1 << i); // LSB-first
    return result;
  }

  public uint GetUInt32(int offset, int length = 32)
  {
    if (length is < 0 or > 32)
      ThrowIndexOutOfRange();

    uint result = 0;
    for (var i = 0; i < length; i++)
      if (Get(offset + i))
        result |= 1u << i; // LSB-first
    return result;
  }


  public void Not()
  {
    if (Length == 0) return;

    var totalBytes = (Length + _startBitOffset + 7) >> 3;
    var remainingBits = (Length + _startBitOffset) & 7;
    if (remainingBits == 0) remainingBits = 8;

    // 反转所有完整字节
    for (var i = 0; i < totalBytes - 1; i++)
      _span[i] = (byte)~_span[i];

    // 处理最后一个字节的部分位
    var lastByteMask = (byte)((1 << remainingBits) - 1);
    _span[totalBytes - 1] ^= lastByteMask;
  }

  public void And(BitSpan other)
  {
    if (other.Length != Length)
      ThrowLengthMismatch();

    for (var i = 0; i < Length; i++)
      if (!other.Get(i))
        Set(i, false);
  }

  public void Or(BitSpan other)
  {
    if (other.Length != Length)
      ThrowLengthMismatch();

    for (var i = 0; i < Length; i++)
      if (other.Get(i))
        Set(i, true);
  }

  public void Xor(BitSpan other)
  {
    if (other.Length != Length)
      ThrowLengthMismatch();

    for (var i = 0; i < Length; i++)
      if (other.Get(i))
        Set(i, !Get(i));
  }

  public void CopyTo(Span<bool> destination)
  {
    if (destination.Length < Length)
      ThrowDestinationTooShort();

    for (var i = 0; i < Length; i++)
      destination[i] = Get(i);
  }

  public Enumerator GetEnumerator()
  {
    return new Enumerator(this);
  }

  public ref struct Enumerator(BitSpan bitSpan)
  {
    private readonly BitSpan _bitSpan = bitSpan;
    private int _index = -1;

    public bool Current
    {
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      get => _bitSpan.Get(_index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
      return ++_index < _bitSpan.Length;
    }
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

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void ThrowIndexOutOfRange()
  {
    throw new ArgumentOutOfRangeException("index");
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void ThrowLengthMismatch()
  {
    throw new ArgumentException("BitSpan lengths must be equal.");
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void ThrowDestinationTooShort()
  {
    throw new ArgumentException("Destination span is too short.");
  }

  #endregion
}