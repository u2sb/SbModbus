using System;
using System.Buffers;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SbModbus.Models;
using static SbModbus.Utils.Utils;

namespace SbModbus.Utils
{
  public static class BitConverter
  {
    public static long ToInt64(ReadOnlySpan<byte> data, bool useBigEndianMode = false)
    {
      return ToT<long>(data, useBigEndianMode);
    }

    public static ulong ToUInt64(ReadOnlySpan<byte> data, bool useBigEndianMode = false)
    {
      return ToT<ulong>(data, useBigEndianMode);
    }

    public static int ToInt32(ReadOnlySpan<byte> data, bool useBigEndianMode = false)
    {
      return ToT<int>(data, useBigEndianMode);
    }

    public static uint ToUInt32(ReadOnlySpan<byte> data, bool useBigEndianMode = false)
    {
      return ToT<uint>(data, useBigEndianMode);
    }

    public static short ToInt16(ReadOnlySpan<byte> data, bool useBigEndianMode = false)
    {
      return ToT<short>(data, useBigEndianMode);
    }

    public static ushort ToUInt16(ReadOnlySpan<byte> data, bool useBigEndianMode = false)
    {
      return ToT<ushort>(data, useBigEndianMode);
    }

    public static float ToSingle(ReadOnlySpan<byte> data, bool useBigEndianMode = false)
    {
      return ToT<float>(data, useBigEndianMode);
    }

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
    public static ReadOnlySpan<byte> AsBytes<T>(this T value) where T : unmanaged
    {
      unsafe
      {
        var bytePtr = &value;
        var span = new ReadOnlySpan<byte>(bytePtr, Unsafe.SizeOf<T>());
        return span;
      }
    }

    public static ReadOnlySpan<byte> AsBytes(this ReadOnlySpan<ushort> data)
    {
      return MemoryMarshal.AsBytes(data);
    }

    public static Span<byte> AsBytes(this Span<ushort> data)
    {
      return MemoryMarshal.AsBytes(data);
    }

    public static ReadOnlySpan<ushort> AsUShorts(this ReadOnlySpan<byte> data)
    {
      return MemoryMarshal.Cast<byte, ushort>(data);
    }

    public static Span<ushort> AsUShorts(this Span<byte> data)
    {
      return MemoryMarshal.Cast<byte, ushort>(data);
    }

    public static Memory<byte> AsBytes<T>(this Memory<T> data) where T : unmanaged
    {
      using var cmm = new CastMemoryManager<T, byte>(data);
      return cmm.Memory;
    }

    public static Memory<TTo> Cast<TFrom, TTo>(this Memory<TFrom> data) where TFrom : unmanaged where TTo : unmanaged
    {
      using var cmm = new CastMemoryManager<TFrom, TTo>(data);
      return cmm.Memory;
    }

    #endregion

    #region MemoryManager类

    private sealed class CastMemoryManager<TFrom, TTo> : MemoryManager<TTo>
      where TFrom : unmanaged
      where TTo : unmanaged
    {
      private readonly Memory<TFrom> _from;

      public CastMemoryManager(Memory<TFrom> from)
      {
        _from = from;
      }

      public override Span<TTo> GetSpan()
      {
        return MemoryMarshal.Cast<TFrom, TTo>(_from.Span);
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

    public sealed unsafe class PointerMemoryManager<T> : MemoryManager<T> where T : struct
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
      Span<byte> result = stackalloc byte[Unsafe.SizeOf<T>()];

      Unsafe.As<byte, T>(ref result[0]) = value;

      if (System.BitConverter.IsLittleEndian == useBigEndianMode) result.Reverse();
      return result.ToArray();
    }

    public static byte[] ToBytes<T>(this T value, byte mode) where T : unmanaged
    {
      return ToBytes(value, (BigAndSmallEndianEncodingMode)mode);
    }

    public static byte[] ToBytes<T>(this T value, BigAndSmallEndianEncodingMode mode) where T : unmanaged
    {
      var size = Unsafe.SizeOf<T>();
      var result = new byte[size];
      ToBytes(value, result.AsSpan(), mode);
      return result;
    }

    public static void ToBytes<T>(this T value, Span<byte> data, byte mode)
      where T : unmanaged
    {
      ToBytes(value, data, (BigAndSmallEndianEncodingMode)mode);
    }

    public static void ToBytes<T>(this T value, Span<byte> data, BigAndSmallEndianEncodingMode mode)
      where T : unmanaged
    {
      var size = Unsafe.SizeOf<T>();
      CheckLength(data, size);
      Unsafe.As<byte, T>(ref data[0]) = value;

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
          var us = data.AsUShorts();
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
    public static T ToT<T>(ReadOnlySpan<byte> data, bool useBigEndianMode = false) where T : unmanaged
    {
      var size = Unsafe.SizeOf<T>();
      CheckLength(data, size);

      Span<byte> span = stackalloc byte[size];
      data.CopyTo(span);

      // 使用小端模式
      if (System.BitConverter.IsLittleEndian == useBigEndianMode) span.Reverse();

      return MemoryMarshal.Read<T>(span);
    }

    public static T ToT<T>(ReadOnlySpan<byte> data, byte mode) where T : unmanaged
    {
      return ToT<T>(data, (BigAndSmallEndianEncodingMode)mode);
    }

    /// <summary>
    ///   转换为类型T
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="data"></param>
    /// <param name="mode"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static T ToT<T>(ReadOnlySpan<byte> data, BigAndSmallEndianEncodingMode mode) where T : unmanaged
    {
      var size = Unsafe.SizeOf<T>();
      if (size % 2 != 0)
        throw new ArgumentException(
          $"The size of the {nameof(T)} type is not a multiple of 2. Actual size is {size} bytes.");
      CheckLength(data, size);

      Span<byte> span = stackalloc byte[size];
      data.CopyTo(span);

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
          var us = span.AsUShorts();
          us.Reverse();
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
      }

      // 如果不是小端模式，做一次整体翻转
      if (!System.BitConverter.IsLittleEndian) span.Reverse();

      return MemoryMarshal.Read<T>(span);
    }

    #endregion

    #region BitArray操作

    public static BitArray ToBitArray(ReadOnlySpan<byte> data)
    {
      return new BitArray(data.ToArray());
    }

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
}