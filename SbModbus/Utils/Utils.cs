using System;
using SbModbus.Models;

namespace SbModbus.Utils;

/// <summary>
/// </summary>
public static class Utils
{
  #region 检查长度

  /// <summary>
  ///   检查长度是否符合要求
  /// </summary>
  /// <param name="data"></param>
  /// <param name="expectedLength"></param>
  /// <exception cref="InvalidArrayLengthException"></exception>
  public static void CheckLength(ReadOnlySpan<byte> data, int expectedLength)
  {
    if (data.Length < expectedLength) throw new InvalidArrayLengthException(expectedLength, data.Length);
  }

  /// <summary>
  ///   检查长度是否符合要求
  /// </summary>
  /// <param name="data"></param>
  /// <param name="expectedLength"></param>
  /// <exception cref="InvalidArrayLengthException"></exception>
  public static void CheckLength(Span<byte> data, int expectedLength)
  {
    if (data.Length < expectedLength) throw new InvalidArrayLengthException(expectedLength, data.Length);
  }

  /// <summary>
  ///   检查长度是否符合要求
  /// </summary>
  /// <param name="data"></param>
  /// <param name="expectedLength"></param>
  /// <exception cref="InvalidArrayLengthException"></exception>
  public static void CheckLength(byte[] data, int expectedLength)
  {
    if (data.Length < expectedLength) throw new InvalidArrayLengthException(expectedLength, data.Length);
  }

  #endregion
}