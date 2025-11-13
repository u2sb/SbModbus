using System.Runtime.InteropServices;
using Sb.Extensions.System;
using SbBitConverter.Attributes;

namespace ModbusTest;

[SbBitConverterStruct(BigAndSmallEndianEncodingMode.ABCD)]
[StructLayout(LayoutKind.Explicit, Pack = 2)]
public partial struct MyStruct
{
  [FieldOffset((0x01 - 1) * 2)] private readonly ushort _outputVoltage;

  /// <summary>
  ///   输出电压
  /// </summary>
  public float OutputVoltage => _outputVoltage * 0.01f;


  [FieldOffset((0x02 - 1) * 2)] private readonly ushort _outputCurrent;

  /// <summary>
  ///   输出电流
  /// </summary>
  public float OutputCurrent => _outputCurrent * 0.001f;


  [FieldOffset((0x03 - 1) * 2)] private readonly ushort _outputPower;

  /// <summary>
  ///   输出功率
  /// </summary>
  public float OutputPower => _outputPower * 0.01f;
}