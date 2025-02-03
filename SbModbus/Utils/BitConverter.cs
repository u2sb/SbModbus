using System;
using System.Buffers;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SbModbus.Models;
using static SbModbus.Utils.Utils;

namespace SbModbus.Utils;

/// <summary>
///   转换类
/// </summary>
public static class BitConverter
{
  /// <summary>
  ///   转换为long类型
  /// </summary>
  /// <param name="data">数据</param>
  /// <param name="useBigEndianMode">是否使用大端模式</param>
  /// <returns></returns>
  public static long ToInt64(ReadOnlySpan<byte> data, bool useBigEndianMode = false)
  {
    return ToT<long>(data, useBigEndianMode);
  }

  /// <summary>
  ///   转换为 ulong 类型
  /// </summary>
  /// <param name="data">数据</param>
  /// <param name="useBigEndianMode">是否使用大端模式</param>
  /// <returns></returns>
  public static ulong ToUInt64(ReadOnlySpan<byte> data, bool useBigEndianMode = false)
  {
    return ToT<ulong>(data, useBigEndianMode);
  }

  /// <summary>
  ///   转换为 int 类型
  /// </summary>
  /// <param name="data">数据</param>
  /// <param name="useBigEndianMode">是否使用大端模式</param>
  /// <returns></returns>
  public static int ToInt32(ReadOnlySpan<byte> data, bool useBigEndianMode = false)
  {
    return ToT<int>(data, useBigEndianMode);
  }

  /// <summary>
  ///   转换为 uint 类型
  /// </summary>
  /// <param name="data">数据</param>
  /// <param name="useBigEndianMode">是否使用大端模式</param>
  /// <returns></returns>
  public static uint ToUInt32(ReadOnlySpan<byte> data, bool useBigEndianMode = false)
  {
    return ToT<uint>(data, useBigEndianMode);
  }

  /// <summary>
  ///   转换为 short 类型
  /// </summary>
  /// <param name="data">数据</param>
  /// <param name="useBigEndianMode">是否使用大端模式</param>
  /// <returns></returns>
  public static short ToInt16(ReadOnlySpan<byte> data, bool useBigEndianMode = false)
  {
    return ToT<short>(data, useBigEndianMode);
  }

  /// <summary>
  ///   转换为 ushort 类型
  /// </summary>
  /// <param name="data">数据</param>
  /// <param name="useBigEndianMode">是否使用大端模式</param>
  /// <returns></returns>
  public static ushort ToUInt16(ReadOnlySpan<byte> data, bool useBigEndianMode = false)
  {
    return ToT<ushort>(data, useBigEndianMode);
  }

  /// <summary>
  ///   转换为 float 类型
  /// </summary>
  /// <param name="data">数据</param>
  /// <param name="useBigEndianMode">是否使用大端模式</param>
  /// <returns></returns>
  public static float ToSingle(ReadOnlySpan<byte> data, bool useBigEndianMode = false)
  {
    return ToT<float>(data, useBigEndianMode);
  }

  /// <summary>
  ///   转换为 double 类型
  /// </summary>
  /// <param name="data">数据</param>
  /// <param name="useBigEndianMode">是否使用大端模式</param>
  /// <returns></returns>
  public static double ToDouble(ReadOnlySpan<byte> data, bool useBigEndianMode = false)
  {
    return ToT<double>(data, useBigEndianMode);
  }

  #region 类型解释

  /// <summary>
  ///   解释为 byte[]
  /// </summary>
  /// <typeparam name="T"></typeparam>
  /// <param name="value">数据</param>
  /// <returns></returns>
  public static ReadOnlySpan<byte> AsReadOnlyByteSpan<T>(this T value) where T : unmanaged
  {
    unsafe
    {
      var bytePtr = &value;
      var span = new ReadOnlySpan<byte>(bytePtr, Unsafe.SizeOf<T>());
      return span;
    }
  }

  /// <summary>
  ///   解释为 byte[]
  /// </summary>
  /// <param name="data"></param>
  /// <returns></returns>
  public static ReadOnlySpan<byte> AsReadOnlyByteSpan(this ReadOnlySpan<ushort> data)
  {
    return MemoryMarshal.AsBytes(data);
  }

  /// <summary>
  ///   解释为 byte[]
  /// </summary>
  /// <param name="data"></param>
  /// <returns></returns>
  public static Span<byte> AsByteSpan(this Span<ushort> data)
  {
    return MemoryMarshal.AsBytes(data);
  }

  /// <summary>
  ///   解释为 ushort[]
  /// </summary>
  /// <param name="data"></param>
  /// <returns></returns>
  public static ReadOnlySpan<ushort> AsReadOnlyUShortSpan(this ReadOnlySpan<byte> data)
  {
    return MemoryMarshal.Cast<byte, ushort>(data);
  }

  /// <summary>
  ///   解释为 ushort[]
  /// </summary>
  /// <param name="data"></param>
  /// <returns></returns>
  public static Span<ushort> AsUShortSpan(this Span<byte> data)
  {
    return MemoryMarshal.Cast<byte, ushort>(data);
  }

  /// <summary>
  ///   解释为 byte[]
  /// </summary>
  /// <param name="data"></param>
  /// <returns></returns>
  public static Memory<byte> AsByteMemory<T>(this Memory<T> data) where T : unmanaged
  {
    using var cmm = new CastMemoryManager<T, byte>(data);
    return cmm.Memory;
  }

  /// <summary>
  ///   Memory转换 类型解释
  /// </summary>
  /// <param name="data"></param>
  /// <typeparam name="TFrom"></typeparam>
  /// <typeparam name="TTo"></typeparam>
  /// <returns></returns>
  public static Memory<TTo> Cast<TFrom, TTo>(this Memory<TFrom> data) where TFrom : unmanaged where TTo : unmanaged
  {
    using var cmm = new CastMemoryManager<TFrom, TTo>(data);
    return cmm.Memory;
  }

  /// <summary>
  ///   转换到Memory
  /// </summary>
  /// <param name="data"></param>
  /// <typeparam name="TFrom"></typeparam>
  /// <typeparam name="TTo"></typeparam>
  /// <returns></returns>
  public static Memory<TTo> AsMemory<TFrom, TTo>(this ReadOnlySpan<TFrom> data)
    where TFrom : unmanaged where TTo : unmanaged
  {
    unsafe
    {
      fixed (void* ptr = data)
      {
        using var cmm = new PointerMemoryManager<TTo>(ptr, data.Length * Unsafe.SizeOf<TFrom>());
        return cmm.Memory;
      }
    }
  }

  /// <summary>
  ///   转换到Memory
  /// </summary>
  /// <param name="data"></param>
  /// <typeparam name="TFrom"></typeparam>
  /// <typeparam name="TTo"></typeparam>
  /// <returns></returns>
  public static Memory<TTo> AsMemory<TFrom, TTo>(this Span<TFrom> data)
    where TFrom : unmanaged where TTo : unmanaged
  {
    unsafe
    {
      fixed (void* ptr = data)
      {
        using var cmm = new PointerMemoryManager<TTo>(ptr, data.Length * Unsafe.SizeOf<TFrom>());
        return cmm.Memory;
      }
    }
  }

  #endregion

  #region MemoryManager类

  private sealed class CastMemoryManager<TFrom, TTo>(Memory<TFrom> from) : MemoryManager<TTo>
    where TFrom : unmanaged
    where TTo : unmanaged
  {
    public override Span<TTo> GetSpan()
    {
      return MemoryMarshal.Cast<TFrom, TTo>(from.Span);
    }

    protected override void Dispose(bool disposing)
    {
    }

    public override MemoryHandle Pin(int elementIndex = 0)
    {
      throw new NotSupportedException();
    }

    public override void Unpin()
    {
    }
  }

  private sealed unsafe class PointerMemoryManager<T> : MemoryManager<T> where T : struct
  {
    private readonly int _length;
    private readonly void* _pointer;

    internal PointerMemoryManager(void* pointer, int length)
    {
      _pointer = pointer;
      _length = length;
    }

    protected override void Dispose(bool disposing)
    {
    }

    public override Span<T> GetSpan()
    {
      return new Span<T>(_pointer, _length);
    }

    public override MemoryHandle Pin(int elementIndex = 0)
    {
      throw new NotSupportedException();
    }

    public override void Unpin()
    {
    }
  }

  #endregion

  #region 通用类型转换

  /// <summary>
  ///   转换到 byte[]
  /// </summary>
  /// <typeparam name="T"></typeparam>
  /// <param name="value">数据</param>
  /// <param name="useBigEndianMode">是否使用大端模式</param>
  /// <returns></returns>
  public static byte[] ToBytes<T>(this T value, bool useBigEndianMode = false) where T : unmanaged
  {
    return ToBytes(value, useBigEndianMode ? BigAndSmallEndianEncodingMode.ABCD : BigAndSmallEndianEncodingMode.DCBA);
  }

  /// <summary>
  ///   转换到 byte[]
  /// </summary>
  /// <param name="value"></param>
  /// <param name="mode"></param>
  /// <typeparam name="T"></typeparam>
  /// <returns></returns>
  public static byte[] ToBytes<T>(this T value, byte mode) where T : unmanaged
  {
    return ToBytes(value, (BigAndSmallEndianEncodingMode)mode);
  }

  /// <summary>
  ///   转换到 byte[]
  /// </summary>
  /// <param name="value"></param>
  /// <param name="mode"></param>
  /// <typeparam name="T"></typeparam>
  /// <returns></returns>
  public static byte[] ToBytes<T>(this T value, BigAndSmallEndianEncodingMode mode) where T : unmanaged
  {
    var size = Unsafe.SizeOf<T>();
    var result = new byte[size];
    WriteTo(value, result.AsSpan(), mode);
    return result;
  }

  /// <summary>
  ///   转换到 byte[]
  /// </summary>
  /// <param name="value"></param>
  /// <param name="data"></param>
  /// <param name="mode"></param>
  /// <typeparam name="T"></typeparam>
  public static void WriteTo<T>(this T value, Span<byte> data, byte mode)
    where T : unmanaged
  {
    WriteTo(value, data, (BigAndSmallEndianEncodingMode)mode);
  }

  /// <summary>
  ///   转换到 byte[]
  /// </summary>
  /// <param name="value"></param>
  /// <param name="data"></param>
  /// <param name="mode"></param>
  /// <typeparam name="T"></typeparam>
  /// <exception cref="ArgumentOutOfRangeException"></exception>
  public static void WriteTo<T>(this T value, Span<byte> data, BigAndSmallEndianEncodingMode mode)
    where T : unmanaged
  {
    var size = Unsafe.SizeOf<T>();
    CheckLength(data, size);
    Unsafe.As<byte, T>(ref MemoryMarshal.GetReference(data)) = value;

    // 如果是单字节，也就是 byte 类型，直接返回
    if (size == 1) return;

    // 先按小端模式处理
    switch (mode)
    {
      case BigAndSmallEndianEncodingMode.DCBA:
        // 小端模式不做处理
        break;
      case BigAndSmallEndianEncodingMode.ABCD:
        // 大端模式整体翻转
        data.Reverse();
        break;
      case BigAndSmallEndianEncodingMode.BADC:
        // 二字节翻转，前后不翻转
        // 已经判断过，必须为 2 的倍数
        for (var i = 0; i < size; i += 2)
        {
          var sp = data.Slice(i, 2);
          sp.Reverse();
        }

        break;
      case BigAndSmallEndianEncodingMode.CDAB:
        // 二字节不翻转，前后翻转
        // 解释为ushort，然后整体翻转
        var us = data.AsUShortSpan();
        us.Reverse();
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
    }

    // 如果不是小端模式，做一次整体翻转
    if (!System.BitConverter.IsLittleEndian) data.Reverse();
  }

  /// <summary>
  ///   转换到T
  /// </summary>
  /// <typeparam name="T"></typeparam>
  /// <param name="data">数据</param>
  /// <param name="useBigEndianMode">是否使用大端模式</param>
  /// <returns></returns>
  public static T ToT<T>(this ReadOnlySpan<byte> data, bool useBigEndianMode = false) where T : unmanaged
  {
    return ToT<T>(data, useBigEndianMode ? BigAndSmallEndianEncodingMode.ABCD : BigAndSmallEndianEncodingMode.DCBA);
  }

  /// <summary>
  ///   转换到T
  /// </summary>
  /// <param name="data"></param>
  /// <param name="mode"></param>
  /// <typeparam name="T"></typeparam>
  /// <returns></returns>
  public static T ToT<T>(this ReadOnlySpan<byte> data, byte mode) where T : unmanaged
  {
    return ToT<T>(data, (BigAndSmallEndianEncodingMode)mode);
  }

  /// <summary>
  ///   转换到T
  /// </summary>
  /// <typeparam name="T"></typeparam>
  /// <param name="data"></param>
  /// <param name="mode"></param>
  /// <returns></returns>
  /// <exception cref="ArgumentException"></exception>
  /// <exception cref="ArgumentOutOfRangeException"></exception>
  public static T ToT<T>(this ReadOnlySpan<byte> data, BigAndSmallEndianEncodingMode mode) where T : unmanaged
  {
    T value = default;
    data.CopyTo(ref value, mode);
    return value;
  }

  /// <summary>
  ///   写入到 T
  /// </summary>
  /// <typeparam name="T"></typeparam>
  /// <param name="data"></param>
  /// <param name="mode"></param>
  /// <param name="value"></param>
  /// <returns></returns>
  /// <exception cref="ArgumentException"></exception>
  /// <exception cref="ArgumentOutOfRangeException"></exception>
  public static void CopyTo<T>(this ReadOnlySpan<byte> data, ref T value,
    BigAndSmallEndianEncodingMode mode)
    where T : unmanaged
  {
    var size = Unsafe.SizeOf<T>();
    CheckLength(data, size);

    unsafe
    {
      fixed (void* ptr = &value)
      {
        var span = new Span<byte>(ptr, size);
        data.CopyTo(span);

        // 如果是单字节，也就是 byte 类型，直接返回
        if (size == 1) return;
        // 先按小端模式处理
        switch (mode)
        {
          case BigAndSmallEndianEncodingMode.DCBA:
            // 小端模式不做处理
            break;
          case BigAndSmallEndianEncodingMode.ABCD:
            // 大端模式整体翻转
            span.Reverse();
            break;
          case BigAndSmallEndianEncodingMode.BADC:
            // 二字节翻转，前后不翻转
            // 已经判断过，必须为 2 的倍数
            for (var i = 0; i < size; i += 2)
            {
              var sp = span.Slice(i, 2);
              sp.Reverse();
            }

            break;
          case BigAndSmallEndianEncodingMode.CDAB:
            // 二字节不翻转，前后翻转
            // 解释为ushort，然后整体翻转
            var us = span.AsUShortSpan();
            us.Reverse();
            break;
          default:
            throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }

        // 如果不是小端模式，做一次整体翻转
        if (!System.BitConverter.IsLittleEndian) span.Reverse();
      }
    }
  }

  #endregion

  #region BitArray操作

  /// <summary>
  ///   转换到 BitArray
  /// </summary>
  /// <param name="data"></param>
  /// <returns></returns>
  public static BitArray ToBitArray(ReadOnlySpan<byte> data)
  {
    return new BitArray(data.ToArray());
  }

  /// <summary>
  ///   从 BitArray 转换到 byte[]
  /// </summary>
  /// <param name="bitArray"></param>
  /// <returns></returns>
  public static byte[] ConvertBitArrayToByteArray(BitArray bitArray)
  {
    unsafe
    {
      var numBytes = (bitArray.Count + 7) >> 3; // 等价于 (bitArray.Count + 7) / 8
      var byteArray = new byte[numBytes];

      fixed (byte* bytePointer = byteArray)
      {
        var currentByte = bytePointer;
        var bitIndex = 0;

        for (var i = 0; i < bitArray.Count; i++)
        {
          if (bitArray[i]) *currentByte |= (byte)(1 << bitIndex);

          bitIndex++;

          if (bitIndex != 8) continue;

          bitIndex = 0;
          currentByte++;
        }
      }

      return byteArray;
    }
  }

  #endregion
}