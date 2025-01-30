using System;

namespace SbModbus.Models;

/// <summary>
///   数组长度和预期不一致错误
/// </summary>
/// <param name="expectedLength">预期长度</param>
/// <param name="actualLength">真实长度</param>
public class InvalidArrayLengthException(int expectedLength, int actualLength)
  : Exception($"Invalid array length. Expected: {expectedLength}, Actual: {actualLength}")
{
  /// <summary>
  ///   预期长度
  /// </summary>
  public int ExpectedLength { get; } = expectedLength;

  /// <summary>
  ///   真实长度
  /// </summary>
  public int ActualLength { get; } = actualLength;
}

// https://raw.githubusercontent.com/Apollo3zehn/FluentModbus/f2d74c6e45ff4b3b41d33ae7d7b3e6243b128472/src/FluentModbus/ModbusException.cs
/// <summary>
///   Modbus 异常
/// </summary>
public class ModbusException : Exception
{
  /// <summary>
  /// </summary>
  /// <param name="message"></param>
  public ModbusException(string message) : base(message)
  {
    ExceptionCode = (ModbusExceptionCode)255;
  }

  /// <summary>
  /// </summary>
  /// <param name="exceptionCode"></param>
  /// <param name="message"></param>
  public ModbusException(ModbusExceptionCode exceptionCode, string message) : base(message)
  {
    ExceptionCode = exceptionCode;
  }

  /// <summary>
  ///   The Modbus exception code. A value of 255 indicates that there is no specific exception code.
  /// </summary>
  public ModbusExceptionCode ExceptionCode { get; }
}