namespace SbModbus.Tool.Services.ModbusServices;

public enum ModbusClientType
{
  ModbusVirtual,
  ModbusRTU,
  ModbusTCP,
  ModbusRTUOverTcp
}

public enum ModbusValueType
{
  Bit,
  Short,
  UShort,
  Int,
  UInt,
  Long,
  ULong,
  Float,
  Double
}