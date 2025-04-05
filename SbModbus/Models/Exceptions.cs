using System;

namespace SbModbus.Models;

/// <summary>
///   Modbus 异常
/// </summary>
public class SbModbusException : Exception
{
  /// <summary>
  /// </summary>
  /// <param name="message"></param>
  internal SbModbusException(string message) : base(message)
  {
    ExceptionCode = (ModbusExceptionCode)255;
  }

  /// <summary>
  /// </summary>
  /// <param name="exceptionCode"></param>
  /// <param name="message"></param>
  public SbModbusException(ModbusExceptionCode exceptionCode, string message) : base(message)
  {
    ExceptionCode = exceptionCode;
  }

  /// <summary>
  ///   The Modbus exception code. A value of 255 indicates that there is no specific exception code.
  /// </summary>
  public ModbusExceptionCode ExceptionCode { get; }
}