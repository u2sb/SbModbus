using System.Runtime.InteropServices;
using SbModbus.Attributes;
using SbModbus.Models;

namespace ModbusTest;

[SbModbusStruct(BigAndSmallEndianEncodingMode.ABCD)]
[StructLayout(LayoutKind.Explicit, Pack = 2)]
public partial struct MyStruct
{
  public MyStruct()
  {
  }

  [FieldOffset(0)] private int _a;

  [field: FieldOffset(4)] public int B { get; set; }

  [field: FieldOffset(0)] public MyStruct2 MyStruct2 { get; set; }

  [field: FieldOffset(8)] public MyEnum MyEnum { get; set; }
}

[SbModbusStruct(BigAndSmallEndianEncodingMode.ABCD)]
[StructLayout(LayoutKind.Explicit, Pack = 2)]
public partial struct MyStruct2
{
  [FieldOffset(0)] private int _a;

  [field: FieldOffset(4)] public int B { get; set; }
}

public enum MyEnum : ushort
{
  OK = 0,
  NoOK = 1
}