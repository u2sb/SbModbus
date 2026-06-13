using System;

namespace SbModbus.Protocol;

/// <summary>
///   Modbus 异常
/// </summary>
public class SbModbusException : Exception
{
  /// <summary>
  ///   无特定异常码
  /// </summary>
  public const ModbusExceptionCode NoSpecificCode = (ModbusExceptionCode)255;

  /// <summary>
  /// </summary>
  /// <param name="message"></param>
  /// <param name="innerException"></param>
  public SbModbusException(string message, Exception? innerException = null) : base(message, innerException)
  {
    ExceptionCode = NoSpecificCode;
  }

  /// <summary>
  /// </summary>
  /// <param name="exceptionCode"></param>
  /// <param name="message"></param>
  /// <param name="innerException"></param>
  public SbModbusException(ModbusExceptionCode exceptionCode, string message, Exception? innerException = null) : base(
    message, innerException)
  {
    ExceptionCode = exceptionCode;
  }

  /// <summary>
  ///   The Modbus exception code. A value of 255 indicates that there is no specific exception code.
  /// </summary>
  public ModbusExceptionCode ExceptionCode { get; }
}

/// <summary>
///   用于包裹 IO 相关异常，统一对外暴露为 <see cref="SbModbusException" />。
/// </summary>
public class SbModbusIOException : SbModbusException
{
  /// <summary>
  /// </summary>
  /// <param name="message">错误描述</param>
  /// <param name="innerException">底层 IO 异常</param>
  public SbModbusIOException(string message, Exception? innerException = null)
    : base(message, innerException)
  {
  }
}
