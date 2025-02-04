using System;
using SbModbus.Models;

namespace SbModbus.Attributes;

/// <summary>
///   SbModbusArray
/// </summary>
/// <param name="elementType">元素类型</param>
/// <param name="length">长度</param>
/// <param name="mode">编码方式</param>
[AttributeUsage(AttributeTargets.Struct)]
public class SbModbusArrayAttribute(
  Type elementType,
  int length,
  BigAndSmallEndianEncodingMode mode = BigAndSmallEndianEncodingMode.DCBA) : Attribute
{
  /// <summary>
  ///   元素类型
  /// </summary>
  public Type ElementType { get; } = elementType;

  /// <summary>
  ///   长度
  /// </summary>
  public int Length { get; } = length;

  /// <summary>
  ///   内存布局方式
  /// </summary>
  public BigAndSmallEndianEncodingMode Mode { get; } = mode;
}