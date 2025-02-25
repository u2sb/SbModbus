using System;
using System.Collections;
using System.Runtime.CompilerServices;
using SbModbus.Utils;
using BitConverter = SbModbus.Utils.BitConverter;

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

  public BitSpan(Span<byte> buffer)
  {
    _span = buffer;
    Length = _span.Length * 8;
    _startBitOffset = 0;
  }

  public BitSpan(Span<ushort> buffer)
  {
    _span = buffer.AsByteSpan();
    Length = _span.Length * 8;
    _startBitOffset = 0;
  }

  public BitSpan(Span<short> buffer)
  {
    _span = buffer.AsByteSpan();
    Length = _span.Length * 8;
    _startBitOffset = 0;
  }

  public int Length { get; }

  public Span<byte> Span => _span;

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
      var (start, length) = range.GetOffsetAndLength(Length);
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

    int fullBytes;
    int lastBits;

    // 优化起始对齐情况
    if (_startBitOffset == 0)
    {
      // 直接批量处理完整字节
      fullBytes = Length >> 3;
      if (fullBytes > 0)
      {
        var pattern = value ? (byte)0xFF : (byte)0x00;
        _span.Slice(0, fullBytes).Fill(pattern);
      }

      // 处理末尾剩余位
      lastBits = Length & 0x07;
      if (lastBits <= 0) return;

      var mask = (byte)(0xFF >> (8 - lastBits));
      _span[fullBytes] = value ? (byte)(_span[fullBytes] | mask) : (byte)(_span[fullBytes] & ~mask);

      return;
    }

    // 原逻辑处理非对齐情况
    var firstByteBits = Math.Min(8 - _startBitOffset, Length);
    SetBits(0, firstByteBits, value);

    var remainingBits = Length - firstByteBits;
    if (remainingBits <= 0) return;

    fullBytes = remainingBits >> 3;
    var patternFull = value ? (byte)0xFF : (byte)0x00;
    var startByte = (_startBitOffset + firstByteBits) >> 3;
    _span.Slice(startByte, fullBytes).Fill(patternFull);

    lastBits = remainingBits & 0x07;
    if (lastBits > 0)
    {
      var lastStart = firstByteBits + (fullBytes << 3);
      var lastMask = (byte)(0xFF >> (8 - lastBits));
      var lastBytePos = (_startBitOffset + lastStart) >> 3;
      _span[lastBytePos] = value ? (byte)(_span[lastBytePos] | lastMask) : (byte)(_span[lastBytePos] & ~lastMask);
    }
  }

  private void SetBits(int startBit, int bitCount, bool value)
  {
    for (var i = 0; i < bitCount; i++) this[startBit + i] = value;
  }

  public void Not()
  {
    // 优化起始对齐情况
    int remainingBits;
    int fullBytes;
    if (_startBitOffset == 0)
    {
      fullBytes = Length >> 3;
      if (fullBytes > 0)
        for (var i = 0; i < fullBytes; i++)
          _span[i] = (byte)~_span[i];

      remainingBits = Length & 0x07;
      if (remainingBits > 0) NotBits(fullBytes << 3, remainingBits);
      return;
    }

    // 处理起始不完整字节
    var firstByteBits = Math.Min(8 - _startBitOffset, Length);
    NotBits(0, firstByteBits);

    remainingBits = Length - firstByteBits;
    if (remainingBits <= 0) return;

    // 反转完整字节
    fullBytes = remainingBits >> 3;
    var startByte = (_startBitOffset + firstByteBits) >> 3;
    for (var i = 0; i < fullBytes; i++) _span[startByte + i] = (byte)~_span[startByte + i];

    // 处理末尾剩余位
    var lastBits = remainingBits & 0x07;
    if (lastBits > 0) NotBits(firstByteBits + (fullBytes << 3), lastBits);
  }

  private void NotBits(int startBit, int bitCount)
  {
    for (var i = 0; i < bitCount; i++)
    {
      var index = startBit + i;
      this[index] = !this[index];
    }
  }

  public void And(BitSpan other)
  {
    ApplyBitwise(other, (a, b) => a & b);
  }

  public void Or(BitSpan other)
  {
    ApplyBitwise(other, (a, b) => a | b);
  }

  public void Xor(BitSpan other)
  {
    ApplyBitwise(other, (a, b) => a ^ b);
  }

  private void ApplyBitwise(BitSpan other, Func<bool, bool, bool> op)
  {
    if (other.Length != Length)
      ThrowLengthMismatch();

    for (var i = 0; i < Length; i++) this[i] = op(this[i], other[i]);
  }

  public BitArray ToBitArray()
  {
    if (_startBitOffset == 0 && (Length & 0x07) == 0) return new BitArray(_span.ToArray());

    return new BitArray(ToBoolArray());
  }

  public void CopyTo(Span<bool> destination)
  {
    if (destination.Length < Length)
      ThrowDestinationTooShort();

    for (var i = 0; i < Length; i++)
      destination[i] = Get(i);
  }

  public bool[] ToBoolArray()
  {
    var result = new bool[Length];
    CopyTo(result);
    return result;
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

  #region 获取数据

  public byte GetByte(int offset, int length = 8)
  {
    return GetT<byte>(offset, length);
  }

  public ushort GetUInt16(int offset, int length = 16, bool useBigEndianMode = true)
  {
    return GetT<ushort>(offset, length, useBigEndianMode);
  }

  public uint GetUInt32(int offset, int length = 32, bool useBigEndianMode = true)
  {
    return GetT<uint>(offset, length, useBigEndianMode);
  }

  public T GetT<T>(int offset, int length, bool useBigEndianMode = true) where T : unmanaged
  {
    Span<byte> span = stackalloc byte[Unsafe.SizeOf<T>()];
    CopyTo(span, offset, length);
    return BitConverter.ToT<T>(span, useBigEndianMode);
  }

  public void CopyTo(Span<byte> destination, int bitOffset, int bitCount)
  {
    // 参数校验
    if (bitOffset < 0 || bitCount < 0)
      throw new ArgumentOutOfRangeException(nameof(bitOffset));
    if (bitOffset + bitCount > Length)
      throw new ArgumentException("Exceeds available bits");
    if (destination.Length * 8 < bitCount)
      throw new ArgumentException("Destination too short");

    // 清空目标区域（可选，根据需求决定）
    destination.Clear();

    var srcBit = _startBitOffset + bitOffset;
    var dstBit = 0;
    var remaining = bitCount;

    while (remaining > 0)
    {
      // 计算源字节和位偏移
      var srcByte = srcBit >> 3;
      var srcBitInByte = srcBit & 0x07;
      var copyBits = Math.Min(8 - srcBitInByte, remaining);

      // 读取源数据
      var srcValue = (byte)((_span[srcByte] >> srcBitInByte) & (0xFF >> (8 - copyBits)));

      // 计算目标位置
      var dstByte = dstBit >> 3;
      var dstBitInByte = dstBit & 0x07;

      // 合并到目标字节
      if (dstBitInByte == 0 && copyBits == 8)
      {
        // 快速路径：完整字节复制
        destination[dstByte] = srcValue;
      }
      else
      {
        // 分步合并
        var availableBits = 8 - dstBitInByte;
        if (availableBits >= copyBits)
        {
          // 单字节写入
          var mask = (byte)((0xFF >> (8 - copyBits)) << dstBitInByte);
          destination[dstByte] = (byte)((destination[dstByte] & ~mask) | (srcValue << dstBitInByte));
        }
        else
        {
          // 跨字节写入
          // 低位部分
          var lowMask = (byte)(0xFF >> (8 - availableBits));
          destination[dstByte] |= (byte)(srcValue << dstBitInByte);

          // 高位部分
          var highBits = copyBits - availableBits;
          var highValue = (byte)(srcValue >> availableBits);
          var highMask = (byte)(0xFF >> (8 - highBits));
          destination[dstByte + 1] |= (byte)(highValue & highMask);
        }
      }

      // 更新指针
      srcBit += copyBits;
      dstBit += copyBits;
      remaining -= copyBits;
    }

    // 处理末尾未使用位（强制清零）
    if (bitCount % 8 != 0)
    {
      var validBits = bitCount % 8;
      var lastByte = bitCount / 8;
      destination[lastByte] &= (byte)(0xFF >> (8 - validBits));
    }
  }

  #endregion

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