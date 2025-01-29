using System;
using SbModbus.Models;

namespace SbModbus.Attributes
{
  [AttributeUsage(AttributeTargets.Struct)]
  public class SbModbusStructAttribute : Attribute
  {
    public SbModbusStructAttribute(BigAndSmallEndianEncodingMode mode = BigAndSmallEndianEncodingMode.DCBA)
    {
      BigAndSmallEndianEncodingMode = mode;
    }

    /// <summary>
    ///   编码方式
    /// </summary>
    public BigAndSmallEndianEncodingMode BigAndSmallEndianEncodingMode { get; }
  }
}