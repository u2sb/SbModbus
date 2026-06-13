using System;

namespace SbModbus.Protocol;

/// <summary>
///   SbModbusException 静态帮助类，避免重复创建异常
/// </summary>
internal static class SbModbusThrow
{
  #region 通用异常

  /// <summary>未连接</summary>
  public static void NotConnected()
  {
    throw new SbModbusException("Not Connected");
  }

  /// <summary>超时</summary>
  public static void Timeout(int readTimeout)
  {
    throw new SbModbusException($"Timeout occurred after {readTimeout} milliseconds");
  }

  /// <summary>响应长度无效</summary>
  public static void InvalidResponseLength()
  {
    throw new SbModbusException("The response message length is invalid.");
  }

  /// <summary>从站地址不匹配</summary>
  public static void SlaveAddressMismatch()
  {
    throw new SbModbusException("The response slave address does not match the request.");
  }

  /// <summary>功能码不匹配</summary>
  public static void FunctionCodeMismatch()
  {
    throw new SbModbusException("The response function code does not match the request.");
  }

  /// <summary>CRC校验错误</summary>
  public static void CrcError()
  {
    throw new SbModbusException("CRC16 check error");
  }

  /// <summary>事务ID无效</summary>
  public static void InvalidTransactionsId()
  {
    throw new SbModbusException("The response TransactionsId is invalid.");
  }

  /// <summary>协议标识无效</summary>
  public static void InvalidProtocolIdentifier()
  {
    throw new SbModbusException("The response protocol identifier is invalid.");
  }

  /// <summary>单元标识符不匹配</summary>
  public static void UnitIdentifierMismatch()
  {
    throw new SbModbusException("The response unit identifier does not match the request.");
  }

  #endregion

  #region Modbus协议异常 (0x01-0x0B)

  /// <summary>01 非法功能码</summary>
  public static void IllegalFunction(ModbusExceptionCode exceptionCode)
  {
    throw new SbModbusException(exceptionCode, ErrorMessages.ModbusClient_0x01_IllegalFunction);
  }

  /// <summary>02 非法数据地址</summary>
  public static void IllegalDataAddress(ModbusExceptionCode exceptionCode)
  {
    throw new SbModbusException(exceptionCode, ErrorMessages.ModbusClient_0x02_IllegalDataAddress);
  }

  /// <summary>03 非法数据值</summary>
  public static void IllegalDataValue(ModbusExceptionCode exceptionCode, ModbusFunctionCode functionCode)
  {
    throw new SbModbusException(exceptionCode, functionCode switch
    {
      ModbusFunctionCode.WriteMultipleRegisters => ErrorMessages.ModbusClient_0x03_IllegalDataValue_0x7B,
      ModbusFunctionCode.ReadHoldingRegisters or ModbusFunctionCode.ReadInputRegisters => ErrorMessages
        .ModbusClient_0x03_IllegalDataValue_0x7D,
      ModbusFunctionCode.ReadCoils or ModbusFunctionCode.ReadDiscreteInputs => ErrorMessages
        .ModbusClient_0x03_IllegalDataValue_0x7D0,
      _ => ErrorMessages.ModbusClient_0x03_IllegalDataValue
    });
  }

  /// <summary>04 服务器设备故障</summary>
  public static void ServerDeviceFailure(ModbusExceptionCode exceptionCode)
  {
    throw new SbModbusException(exceptionCode, ErrorMessages.ModbusClient_0x04_ServerDeviceFailure);
  }

  /// <summary>05 确认</summary>
  public static void Acknowledge(ModbusExceptionCode exceptionCode)
  {
    throw new SbModbusException(exceptionCode, ErrorMessages.ModbusClient_0x05_Acknowledge);
  }

  /// <summary>06 服务器设备忙</summary>
  public static void ServerDeviceBusy(ModbusExceptionCode exceptionCode)
  {
    throw new SbModbusException(exceptionCode, ErrorMessages.ModbusClient_0x06_ServerDeviceBusy);
  }

  /// <summary>08 内存奇偶错误</summary>
  public static void MemoryParityError(ModbusExceptionCode exceptionCode)
  {
    throw new SbModbusException(exceptionCode, ErrorMessages.ModbusClient_0x08_MemoryParityError);
  }

  /// <summary>0A 网关路径不可用</summary>
  public static void GatewayPathUnavailable(ModbusExceptionCode exceptionCode)
  {
    throw new SbModbusException(exceptionCode, ErrorMessages.ModbusClient_0x0A_GatewayPathUnavailable);
  }

  /// <summary>0B 网关目标设备无响应</summary>
  public static void GatewayTargetDeviceFailedToRespond(ModbusExceptionCode exceptionCode)
  {
    throw new SbModbusException(exceptionCode, ErrorMessages.ModbusClient_0x0B_GatewayTargetDeviceFailedToRespond);
  }

  /// <summary>未知Modbus错误</summary>
  public static void UnknownError(ModbusExceptionCode exceptionCode)
  {
    throw new SbModbusException(exceptionCode, string.Format(ErrorMessages.ModbusClient_Unknown_Error, (int)exceptionCode));
  }

  #endregion
}
