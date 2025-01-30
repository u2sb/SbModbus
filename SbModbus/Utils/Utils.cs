using System;
using SbModbus.Models;

namespace SbModbus.Utils
{
  public static class Utils
  {
    #region 检查长度

    public static void CheckLength(ReadOnlySpan<byte> data, int expectedLength)
    {
      if (data.Length != expectedLength) throw new InvalidArrayLengthException(expectedLength, data.Length);
    }

    public static void CheckLength(Span<byte> data, int expectedLength)
    {
      if (data.Length != expectedLength) throw new InvalidArrayLengthException(expectedLength, data.Length);
    }

    public static void CheckLength(byte[] data, int expectedLength)
    {
      if (data.Length != expectedLength) throw new InvalidArrayLengthException(expectedLength, data.Length);
    }

    #endregion
  }
}