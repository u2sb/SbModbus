using System;
using System.Linq;

namespace SbModbus.Utils;

/// <summary>
///   日志帮助类
/// </summary>
internal static class SbModbusLogger
{
  /// <summary>
  ///   将字节数据格式化为十六进制字符串
  /// </summary>
  public static string ToHexString(ReadOnlySpan<byte> data)
  {
    if (data.IsEmpty) return string.Empty;
#if NETSTANDARD2_0
    var parts = new string[data.Length];
    for (var i = 0; i < data.Length; i++)
      parts[i] = data[i].ToString("X2");
    return string.Join(" ", parts);
#else
    return string.Join(" ", data.ToArray().Select(b => b.ToString("X2")));
#endif
  }
}
