namespace SbModbus.Models;

/// <summary>
///   Modbus 错误消息常量
/// </summary>
internal static class ErrorMessages
{
  internal const string ModbusClient_0x01_IllegalFunction =
    "The function code received in the query is not an allowable action for the server. This may be because the function code is only applicable to newer devices, and was not implemented in the unit selected. It could also indicate that the server is in the wrong state to process a request of this type, for example because it is unconfigured and is being asked to return register values.";

  internal const string ModbusClient_0x02_IllegalDataAddress =
    "The data address received in the query is not an allowable address for the server. More specifically, the combination of reference number and transfer length is invalid.";

  internal const string ModbusClient_0x03_IllegalDataValue =
    "A value contained in the query data field is not an allowable value for server. This indicates a fault in the structure of the remainder of a complex request, such as that the implied length is incorrect.";

  internal const string ModbusClient_0x03_IllegalDataValue_0x7B =
    "The quantity of registers is out of range (1..123). Make sure to request a minimum of one register. If you use the generic overload methods, please note that a single register consists of 2 bytes. If, for example, 1 x int32 value is requested, this results in a read operation of 2 registers.";

  internal const string ModbusClient_0x03_IllegalDataValue_0x7D =
    "The quantity of registers is out of range (1..125). Make sure to request a minimum of one register. If you use the generic overload methods, please note that a single register consists of 2 bytes. If, for example, 1 x int32 value is requested, this results in a read operation of 2 registers.";

  internal const string ModbusClient_0x03_IllegalDataValue_0x7D0 =
    "The quantity of coils is out of range (1..2000).";

  internal const string ModbusClient_0x04_ServerDeviceFailure =
    "An unrecoverable error occurred while the server was attempting to perform the requested action.";

  internal const string ModbusClient_0x05_Acknowledge =
    "The server has accepted the request and is processing it, but a long duration of time will be required to do so.";

  internal const string ModbusClient_0x06_ServerDeviceBusy =
    "The server is engaged in processing a long–duration program command.";

  internal const string ModbusClient_0x08_MemoryParityError =
    "The server attempted to read record file, but detected a parity error in the memory.";

  internal const string ModbusClient_0x0A_GatewayPathUnavailable =
    "The gateway was unable to allocate an internal communication path from the input port to the output port for processing the request.";

  internal const string ModbusClient_0x0B_GatewayTargetDeviceFailedToRespond =
    "No response was obtained from the target device";

  internal const string ModbusClient_InvalidUnitIdentifier =
    "The unit identifier is invalid. Valid node addresses are in the range of 0 - 247. Use address '0' to broadcast write command to all available servers.";

  internal const string Modbus_InvalidValueUShort =
    "The value is invalid. Valid values are in the range of 0 - 65535.";

  internal const string ModbusClient_Unknown_Error =
    "Unknown {0} Modbus error received.";
}
