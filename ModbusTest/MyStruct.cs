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

  [field: FieldOffset(4)] public ushort B { get; set; }
}