using System;
using SbModbus.Models;

namespace SbModbus.Attributes;

/// <summary>
/// </summary>
/// <param name="mode"></param>
[AttributeUsage(AttributeTargets.Struct)]
public class SbModbusStructAttribute(BigAndSmallEndianEncodingMode mode = BigAndSmallEndianEncodingMode.DCBA)
  : Attribute
{
  /// <summary>
  ///   编码方式
  /// </summary>
  public BigAndSmallEndianEncodingMode BigAndSmallEndianEncodingMode { get; } = mode;
}