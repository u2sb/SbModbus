namespace SbModbus.Tool.Services.ModbusServices;

public enum ModbusClientType
{
  ModbusVirtual,
  ModbusRTU,
  ModbusTCP,
  ModbusRTUOverTcp,
  ModbusRTUOverUdp
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