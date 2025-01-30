using System;
using SbModbus.Models;

namespace SbModbus.Attributes;

[AttributeUsage(AttributeTargets.Struct)]
public class SbModbusStructAttribute(BigAndSmallEndianEncodingMode mode = BigAndSmallEndianEncodingMode.DCBA)
  : Attribute
{
  /// <summary>
  ///   编码方式
  /// </summary>
  public BigAndSmallEndianEncodingMode BigAndSmallEndianEncodingMode { get; } = mode;
}