// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming

namespace SbModbus.Models;

/// <summary>
///   大小端编码方式
/// </summary>
public enum BigAndSmallEndianEncodingMode : byte
{
  /// <summary>
  ///   小端模式
  /// </summary>
  DCBA = 0,

  /// <summary>
  ///   大端模式
  /// </summary>
  ABCD = 1,

  /// <summary>
  ///   前后顺序不变 二字节内部翻转
  /// </summary>
  BADC = 2,

  /// <summary>
  ///   二字节内部不变 前后顺序翻转
  /// </summary>
  CDAB = 3
}